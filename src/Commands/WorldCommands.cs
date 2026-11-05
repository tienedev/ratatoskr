using System.Collections.Generic;
using System.Linq;

namespace Ratatoskr.Commands
{
    internal class WorldInfo : RtCommand
    {
        public override string Command => "rtWorld";
        public override string Description => "World overview as JSON: day, time, active raid, global keys, players online. Usage: rtWorld";

        protected override string Handle(Args args, string raw)
        {
            var j = new Json().Obj();
            if (EnvMan.instance != null)
            {
                var frac = EnvMan.instance.GetDayFraction();
                var minutes = (int)(frac * 24 * 60);
                j.Prop("day", EnvMan.instance.GetDay())
                 .Prop("time", $"{minutes / 60:00}:{minutes % 60:00}");
            }
            var rs = RandEventSystem.instance;
            var ev = rs != null ? rs.GetCurrentRandomEvent() ?? rs.m_forcedEvent : null;
            j.Key("raid");
            if (ev != null) j.Val(ev.m_name); else j.Null();
            j.Key("globalKeys").Arr();
            foreach (var k in ZoneSystem.instance.GetGlobalKeys()) j.Val(k);
            j.EndArr();
            j.Prop("players", ZNet.instance.GetPeers().Count)
             .Prop("objects", ZDOMan.instance.m_objectsByID.Count)
             .Prop("indexScanMs", WorldIndex.LastScanMs)
             .Prop("boot", Events.Boot)
             .Prop("seq", Events.Seq)
             .EndObj();
            return j.ToString();
        }
    }

    internal class Players : PagedCommand
    {
        public override string Command => "rtPlayers";
        public override string Description => "Online players as JSON with position, biome and health. Usage: rtPlayers";

        protected override Result Produce(Args args)
        {
            var items = new List<string>();
            foreach (var peer in ZNet.instance.GetPeers())
            {
                var zdo = ZDOMan.instance.GetZDO(peer.m_characterID);
                var pos = zdo != null ? zdo.GetPosition() : peer.m_refPos;
                var j = new Json().Obj()
                    .Prop("name", peer.m_playerName)
                    .Prop("host", peer.m_socket?.GetHostName())
                    .Key("pos").Pos(pos)
                    .Prop("biome", World.Biome(pos));
                if (zdo != null)
                {
                    var id = zdo.GetLong(ZDOVars.s_playerID, 0L);
                    PlayerNames.Learn(id, zdo.GetString(ZDOVars.s_playerName));
                    j.Prop("playerId", id)
                     .Prop("health", (double)zdo.GetFloat(ZDOVars.s_health))
                     .Prop("dead", zdo.GetBool(ZDOVars.s_dead));
                }
                items.Add(j.EndObj().ToString());
            }
            return new Result { Items = items };
        }
    }

    internal class Prefabs : PagedCommand
    {
        public override string Command => "rtPrefabs";
        public override string Description => "Object counts by prefab, most frequent first. Usage: rtPrefabs [-near x z radius] [-offset n]";

        protected override Result Produce(Args args)
        {
            var counts = new Dictionary<string, int>();
            foreach (var zdo in World.All())
            {
                if (!args.InRange(zdo.GetPosition())) continue;
                var name = World.Info(zdo).Name;
                counts.TryGetValue(name, out var c);
                counts[name] = c + 1;
            }
            return new Result
            {
                Items = counts.OrderByDescending(kv => kv.Value)
                    .Select(kv => new Json().Obj().Prop("prefab", kv.Key).Prop("count", kv.Value).EndObj().ToString())
                    .ToList(),
            };
        }
    }

    internal class EventsCommand : RtCommand
    {
        public override string Command => "rtEvents";
        public override string Description => "World events after a sequence number (join, leave, death, biome, boss, raid, raidEnd, chest). Usage: rtEvents [-since seq]";

        protected override string Handle(Args args, string raw) =>
            Pager.Page(Events.Since(args.Since), 0, $"\"boot\":\"{Events.Boot}\",\"seq\":{Events.Seq}");
    }

    internal class Playtime : PagedCommand
    {
        public override string Command => "rtPlaytime";
        public override string Description => "Play time per player (closed + ongoing sessions). Usage: rtPlaytime [-days n] [-offset n]";

        protected override Result Produce(Args args)
        {
            var now = Events.Now;
            var cutoff = args.Days > 0 ? now - (long)(args.Days * 86400000) : 0;
            var rows = Tracker.Sessions.Concat(Tracker.OpenSessions())
                .Where(s => s.End > cutoff)
                .GroupBy(s => s.Name)
                .Select(g => (Name: g.Key,
                    Seconds: g.Sum(s => (s.End - System.Math.Max(s.Start, cutoff)) / 1000),
                    Sessions: g.Count(),
                    LastSeen: g.Max(s => s.End)))
                .OrderByDescending(r => r.Seconds);
            return new Result
            {
                Items = rows.Select(r => new Json().Obj().Prop("name", r.Name).Prop("seconds", r.Seconds)
                    .Prop("sessions", r.Sessions).Prop("lastSeen", r.LastSeen).EndObj().ToString()).ToList(),
            };
        }
    }
}
