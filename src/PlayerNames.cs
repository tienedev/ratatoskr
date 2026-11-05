using System.Collections.Generic;
using System.IO;
using BepInEx;

namespace Ratatoskr
{
    internal static class PlayerNames
    {
        private static readonly Dictionary<long, string> Names = new Dictionary<long, string>();
        private static string FilePath => Path.Combine(Paths.ConfigPath, "ratatoskr.players.tsv");
        private static bool _dirty;

        public static void Load()
        {
            if (!File.Exists(FilePath)) return;
            foreach (var line in File.ReadAllLines(FilePath))
            {
                var parts = line.Split('\t');
                if (parts.Length == 2 && long.TryParse(parts[0], out var id)) Names[id] = parts[1];
            }
        }

        public static void Learn(long id, string name)
        {
            if (id == 0 || string.IsNullOrEmpty(name)) return;
            if (Names.TryGetValue(id, out var known) && known == name) return;
            Names[id] = name;
            _dirty = true;
        }

        public static string Get(long id) => Names.TryGetValue(id, out var n) ? n : null;

        public static void LearnOnline()
        {
            if (ZNet.instance == null || ZDOMan.instance == null) return;
            foreach (var peer in ZNet.instance.GetPeers())
            {
                var zdo = ZDOMan.instance.GetZDO(peer.m_characterID);
                if (zdo != null) Learn(zdo.GetLong(ZDOVars.s_playerID, 0L), zdo.GetString(ZDOVars.s_playerName));
            }
            Save();
        }

        public static void Save()
        {
            if (!_dirty) return;
            var lines = new List<string>();
            foreach (var kv in Names) lines.Add(kv.Key + "\t" + kv.Value.Replace('\t', ' '));
            File.WriteAllLines(FilePath, lines);
            _dirty = false;
        }
    }
}
