using System.Collections.Generic;
using System.IO;
using BepInEx;

namespace Ratatoskr
{
    internal sealed class Store
    {
        private readonly string _path;

        public Store(string name) { _path = Path.Combine(Paths.ConfigPath, name); }

        public void Append(params object[] fields)
        {
            var parts = new string[fields.Length];
            for (var i = 0; i < fields.Length; i++)
                parts[i] = (fields[i]?.ToString() ?? "").Replace('\t', ' ').Replace('\n', ' ');
            File.AppendAllText(_path, string.Join("\t", parts) + "\n");
        }

        public List<string[]> Read(int max = 0)
        {
            var rows = new List<string[]>();
            if (!File.Exists(_path)) return rows;
            var lines = File.ReadAllLines(_path);
            var start = max > 0 ? System.Math.Max(0, lines.Length - max) : 0;
            for (var i = start; i < lines.Length; i++)
                if (lines[i].Length > 0) rows.Add(lines[i].Split('\t'));
            return rows;
        }

        public bool Exists => File.Exists(_path);
    }
}
