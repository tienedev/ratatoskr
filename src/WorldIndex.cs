using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace Ratatoskr
{
    internal static class WorldIndex
    {
        public const float MaxAge = 30f;

        private static readonly List<ZDOID> StorageIds = new List<ZDOID>();
        private static readonly List<ZDOID> TombIds = new List<ZDOID>();
        private static readonly List<ZDOID> PortalIds = new List<ZDOID>();
        private static List<World.SignText> _signs = new List<World.SignText>();
        private static float _builtAt = float.NegativeInfinity;

        public static double LastScanMs { get; private set; }

        public static void EnsureFresh()
        {
            if (Time.time - _builtAt >= MaxAge) Rebuild();
        }

        public static void Rebuild()
        {
            var sw = Stopwatch.StartNew();
            StorageIds.Clear();
            TombIds.Clear();
            PortalIds.Clear();
            var signs = new List<World.SignText>();
            foreach (var zdo in World.All())
            {
                var info = World.Info(zdo);
                if (info.IsTombstone) TombIds.Add(zdo.m_uid);
                else if (info.Container != null) StorageIds.Add(zdo.m_uid);
                else if (info.IsPortal) PortalIds.Add(zdo.m_uid);
                else if (info.IsSign)
                {
                    var text = World.Clean(zdo.GetString(ZDOVars.s_text));
                    if (!string.IsNullOrEmpty(text)) signs.Add(new World.SignText { Pos = zdo.GetPosition(), Text = text });
                }
            }
            _signs = signs;
            _builtAt = Time.time;
            LastScanMs = sw.Elapsed.TotalMilliseconds;
            InventoryCache.Prune(StorageIds, TombIds);
        }

        public static IEnumerable<ZDO> Containers() { EnsureFresh(); return Live(StorageIds); }
        public static IEnumerable<ZDO> Tombs() { EnsureFresh(); return Live(TombIds); }
        public static IEnumerable<ZDO> Portals() { EnsureFresh(); return Live(PortalIds); }
        public static List<World.SignText> Signs() { EnsureFresh(); return _signs; }

        private static IEnumerable<ZDO> Live(List<ZDOID> ids)
        {
            var man = ZDOMan.instance;
            foreach (var id in ids)
            {
                var zdo = man.GetZDO(id);
                if (zdo != null) yield return zdo;
            }
        }
    }
}
