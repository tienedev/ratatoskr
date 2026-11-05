using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Ratatoskr
{
    internal sealed class JournalEntry
    {
        public long T;
        public string Player;
        public int Delta;
        public string Item;
        public string Label;
        public string Container;
        public string ContainerLabel;
        public float X, Z;
    }

    internal sealed class Session
    {
        public long PlayerId;
        public string Name;
        public long Start, End;
    }

    internal static class Tracker
    {
        private sealed class PlayerState
        {
            public string Name;
            public long PlayerId;
            public long JoinedAt;
            public ZDOID DeadFor = ZDOID.None;
        }

        private const int JournalInMemory = 5000;

        private static readonly Store BiomeStore = new Store("ratatoskr.biomes.tsv");
        private static readonly Store SessionStore = new Store("ratatoskr.sessions.tsv");
        private static readonly Store JournalStore = new Store("ratatoskr.journal.tsv");

        private static readonly Dictionary<long, PlayerState> Online = new Dictionary<long, PlayerState>();
        private static readonly Dictionary<ZDOID, (uint Rev, Dictionary<string, int> Counts)> Chests =
            new Dictionary<ZDOID, (uint, Dictionary<string, int>)>();
        private static HashSet<string> _knownBiomes;
        private static HashSet<string> _keys;
        private static string _raid;
        private static bool _started;

        public static readonly List<JournalEntry> Journal = new List<JournalEntry>();
        public static readonly List<Session> Sessions = new List<Session>();

        public static IEnumerable<Session> OpenSessions() =>
            Online.Values.Select(p => new Session { PlayerId = p.PlayerId, Name = p.Name, Start = p.JoinedAt, End = Events.Now });

        public static void Tick()
        {
            if (ZNet.instance == null || ZDOMan.instance == null || ZoneSystem.instance == null || EnvMan.instance == null) return;
            if (!_started) Start();
            TrackPlayers();
            TrackKeys();
            TrackRaid();
            TrackChests();
        }

        public static void Shutdown()
        {
            foreach (var s in OpenSessions()) SessionStore.Append(s.PlayerId, s.Name, s.Start, s.End);
            Online.Clear();
        }

        private static void Start()
        {
            _started = true;
            foreach (var r in SessionStore.Read())
                if (r.Length >= 4 && long.TryParse(r[0], out var id) && long.TryParse(r[2], out var a) && long.TryParse(r[3], out var b))
                    Sessions.Add(new Session { PlayerId = id, Name = r[1], Start = a, End = b });
            foreach (var r in JournalStore.Read(JournalInMemory))
                if (r.Length >= 9 && long.TryParse(r[0], out var t) && int.TryParse(r[2], out var d))
                    Journal.Add(new JournalEntry
                    {
                        T = t, Player = r[1], Delta = d, Item = r[3], Label = r[4], Container = r[5],
                        ContainerLabel = r[6].Length > 0 ? r[6] : null,
                        X = float.Parse(r[7], System.Globalization.CultureInfo.InvariantCulture),
                        Z = float.Parse(r[8], System.Globalization.CultureInfo.InvariantCulture),
                    });

            _knownBiomes = new HashSet<string>(BiomeStore.Read().Select(r => r[0]));
            if (!BiomeStore.Exists) SeedBiomes();

            _keys = new HashSet<string>(ZoneSystem.instance.GetGlobalKeys());
            _raid = CurrentRaid()?.m_name;
            foreach (var zdo in WorldIndex.Containers())
            {
                var info = World.Info(zdo);
                if (World.IsStorage(zdo, info, false)) Chests[zdo.m_uid] = (zdo.DataRevision, Counts(zdo, info));
            }
        }

        // Existing world: biomes with builds, tombstones or portals are already discovered.
        private static void SeedBiomes()
        {
            foreach (var zdo in World.All())
            {
                var info = World.Info(zdo);
                if (zdo.GetLong(ZDOVars.s_creator, 0L) == 0L && !info.IsTombstone && !info.IsPortal) continue;
                var biome = World.Biome(zdo.GetPosition());
                if (biome != null && _knownBiomes.Add(biome)) BiomeStore.Append(biome, "", Events.Now);
            }
        }

        private static void TrackPlayers()
        {
            var seen = new HashSet<long>();
            foreach (var peer in ZNet.instance.GetPeers())
            {
                if (peer.m_characterID == ZDOID.None) continue;
                seen.Add(peer.m_uid);
                if (!Online.TryGetValue(peer.m_uid, out var st))
                {
                    st = new PlayerState { Name = peer.m_playerName, JoinedAt = Events.Now };
                    Online[peer.m_uid] = st;
                    var first = Sessions.All(s => s.Name != st.Name);
                    Events.Add("join", j => j.Prop("player", st.Name).Prop("first", first));
                }

                var zdo = ZDOMan.instance.GetZDO(peer.m_characterID);
                if (zdo == null) continue;
                st.PlayerId = zdo.GetLong(ZDOVars.s_playerID, 0L);
                PlayerNames.Learn(st.PlayerId, st.Name);
                var pos = zdo.GetPosition();
                var biome = World.Biome(pos);

                if (zdo.GetBool(ZDOVars.s_dead) && st.DeadFor != peer.m_characterID)
                {
                    st.DeadFor = peer.m_characterID;
                    Events.Add("death", j => j.Prop("player", st.Name).Key("pos").Pos(pos).Prop("biome", biome));
                }

                if (biome != null && biome != "None" && _knownBiomes.Add(biome))
                {
                    BiomeStore.Append(biome, st.Name, Events.Now);
                    Events.Add("biome", j => j.Prop("player", st.Name).Prop("biome", biome).Key("pos").Pos(pos));
                }
            }

            foreach (var uid in Online.Keys.Where(u => !seen.Contains(u)).ToList())
            {
                var st = Online[uid];
                Online.Remove(uid);
                var session = new Session { PlayerId = st.PlayerId, Name = st.Name, Start = st.JoinedAt, End = Events.Now };
                Sessions.Add(session);
                SessionStore.Append(session.PlayerId, session.Name, session.Start, session.End);
                Events.Add("leave", j => j.Prop("player", st.Name).Prop("seconds", (session.End - session.Start) / 1000));
            }
        }

        private static void TrackKeys()
        {
            foreach (var key in ZoneSystem.instance.GetGlobalKeys())
            {
                if (!_keys.Add(key) || !key.StartsWith("defeated_")) continue;
                var names = string.Join(", ", Online.Values.Select(p => p.Name));
                Events.Add("boss", j => j.Prop("key", key).Prop("online", names));
            }
        }

        private static RandomEvent CurrentRaid()
        {
            // GetActiveEvent() depends on a local player, so it is always null on a dedicated server.
            var rs = RandEventSystem.instance;
            return rs == null ? null : rs.GetCurrentRandomEvent() ?? rs.m_forcedEvent;
        }

        private static void TrackRaid()
        {
            var ev = CurrentRaid();
            var name = ev?.m_name;
            if (name == _raid) return;
            if (name != null)
            {
                var pos = ev.m_pos;
                var message = Translations.Get(ev.m_startMessage) ?? Localization.instance?.Localize(ev.m_startMessage);
                Events.Add("raid", j => j.Prop("raid", name).Prop("message", message).Key("pos").Pos(pos)
                    .Prop("biome", World.Biome(pos)).Prop("label", World.LabelFor(pos, WorldIndex.Signs(), 40f)));
            }
            else
            {
                var ended = _raid;
                Events.Add("raidEnd", j => j.Prop("raid", ended));
            }
            _raid = name;
        }

        private static Dictionary<string, int> Counts(ZDO zdo, World.PrefabInfo info) =>
            InventoryCache.Get(zdo, info.Container).Items.ToDictionary(kv => kv.Key, kv => kv.Value.Count);

        private static void TrackChests()
        {
            foreach (var zdo in WorldIndex.Containers())
            {
                var info = World.Info(zdo);
                if (!World.IsStorage(zdo, info, false)) continue;
                var rev = zdo.DataRevision;
                if (!Chests.TryGetValue(zdo.m_uid, out var snap))
                {
                    Chests[zdo.m_uid] = (rev, Counts(zdo, info));
                    continue;
                }
                if (snap.Rev == rev) continue;

                var contents = InventoryCache.Get(zdo, info.Container);
                var now = contents.Items.ToDictionary(kv => kv.Key, kv => kv.Value.Count);
                Chests[zdo.m_uid] = (rev, now);

                var changes = new List<(string Item, string Label, int Delta)>();
                foreach (var id in now.Keys.Union(snap.Counts.Keys))
                {
                    now.TryGetValue(id, out var after);
                    snap.Counts.TryGetValue(id, out var before);
                    if (after == before) continue;
                    var label = contents.Items.TryGetValue(id, out var v) ? v.Label
                        : Journal.LastOrDefault(e => e.Item == id)?.Label ?? id;
                    changes.Add((id, label, after - before));
                }
                if (changes.Count == 0) continue;

                // Opening a container makes the opener its owner.
                var owner = zdo.GetOwner();
                var who = ZNet.instance.GetPeer(owner)?.m_playerName
                    ?? (owner == ZDOMan.GetSessionID() ? "server" : "?");
                var pos = zdo.GetPosition();
                var chestLabel = World.LabelFor(pos, WorldIndex.Signs());
                foreach (var c in changes)
                {
                    var e = new JournalEntry
                    {
                        T = Events.Now, Player = who, Delta = c.Delta, Item = c.Item, Label = c.Label,
                        Container = info.Name, ContainerLabel = chestLabel, X = pos.x, Z = pos.z,
                    };
                    Journal.Add(e);
                    JournalStore.Append(e.T, e.Player, e.Delta, e.Item, e.Label, e.Container, e.ContainerLabel,
                        e.X.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture),
                        e.Z.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture));
                }
                if (Journal.Count > JournalInMemory) Journal.RemoveRange(0, Journal.Count - JournalInMemory);

                Events.Add("chest", j =>
                {
                    j.Prop("player", who).Prop("container", info.Name).Prop("label", chestLabel).Key("pos").Pos(pos).Key("changes").Arr();
                    foreach (var c in changes) j.Obj().Prop("item", c.Item).Prop("name", c.Label).Prop("delta", c.Delta).EndObj();
                    j.EndArr();
                });
            }
        }
    }
}
