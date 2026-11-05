using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Ratatoskr
{
    internal sealed class Args
    {
        public readonly List<string> Positional = new List<string>();
        public Vector3? NearCenter;
        public float NearRadius;
        public int Offset;
        public bool All;
        public string Player;
        public float Days;
        public long Since;

        public Args(IReadOnlyList<string> raw)
        {
            for (var i = 0; i < raw.Count; i++)
            {
                switch (raw[i])
                {
                    case "-near":
                        NearCenter = new Vector3(F(raw, i + 1), 0f, F(raw, i + 2));
                        NearRadius = F(raw, i + 3);
                        i += 3;
                        break;
                    case "-offset":
                        Offset = Mathf.Max(0, (int)F(raw, i + 1));
                        i += 1;
                        break;
                    case "-player":
                        Player = i + 1 < raw.Count ? raw[i + 1] : throw new System.ArgumentException("-player needs a name");
                        i += 1;
                        break;
                    case "-days":
                        Days = F(raw, i + 1);
                        i += 1;
                        break;
                    case "-since":
                        Since = long.TryParse(i + 1 < raw.Count ? raw[i + 1] : "", out var since) ? since
                            : throw new System.ArgumentException("-since needs a number");
                        i += 1;
                        break;
                    case "-all":
                        All = true;
                        break;
                    default:
                        Positional.Add(raw[i]);
                        break;
                }
            }
        }

        public static string WithoutOffset(string raw) =>
            System.Text.RegularExpressions.Regex.Replace(raw, @"(^|\s)-offset\s+\S+", "").Trim();

        public string Text => string.Join(" ", Positional).Trim();

        public bool InRange(Vector3 p)
        {
            if (NearCenter == null) return true;
            var c = NearCenter.Value;
            var dx = p.x - c.x;
            var dz = p.z - c.z;
            return dx * dx + dz * dz <= NearRadius * NearRadius;
        }

        private static float F(IReadOnlyList<string> raw, int i)
        {
            if (i >= raw.Count || !float.TryParse(raw[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                throw new System.ArgumentException("bad or missing number at argument " + i);
            return v;
        }
    }
}
