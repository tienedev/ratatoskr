using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Ratatoskr
{
    internal static class World
    {
        internal sealed class PrefabInfo
        {
            public string Name;
            public Container Container;
            public bool IsSign;
            public bool IsPortal;
            public bool IsTombstone;
        }

        private static readonly Dictionary<int, PrefabInfo> Prefabs = new Dictionary<int, PrefabInfo>();
        private static readonly Regex Tags = new Regex("<[^>]*>", RegexOptions.Compiled);

        public static IEnumerable<ZDO> All() => ZDOMan.instance.m_objectsByID.Values;

        public static PrefabInfo Info(ZDO zdo)
        {
            var hash = zdo.GetPrefab();
            if (Prefabs.TryGetValue(hash, out var info)) return info;

            info = new PrefabInfo();
            var prefab = ZNetScene.instance ? ZNetScene.instance.GetPrefab(hash) : null;
            if (prefab != null)
            {
                info.Name = prefab.name;
                info.Container = prefab.GetComponentInChildren<Container>();
                info.IsSign = prefab.GetComponent<Sign>() != null;
                info.IsPortal = prefab.GetComponent<TeleportWorld>() != null;
                info.IsTombstone = prefab.GetComponent<TombStone>() != null;
            }
            else
            {
                info.Name = hash.ToString();
            }
            Prefabs[hash] = info;
            return info;
        }

        public static bool IsStorage(ZDO zdo, PrefabInfo info, bool all)
        {
            if (info.Container == null || info.IsTombstone) return false;
            return all || zdo.GetLong(ZDOVars.s_creator, 0L) != 0L;
        }

        public static Inventory LoadInventory(ZDO zdo, Container c)
        {
            var inv = new Inventory(c.m_name, null, c.m_width, c.m_height);
            var data = zdo.GetByteArray(ZDOVars.s_items, null);
            if (data != null) inv.Load(new ZPackage(data));
            return inv;
        }

        public static Inventory LoadTombInventory(ZDO zdo)
        {
            var inv = new Inventory("tomb", null, 8, 8);
            var data = zdo.GetByteArray(ZDOVars.s_items, null);
            if (data != null) inv.Load(new ZPackage(data));
            return inv;
        }

        public static string ItemId(ItemDrop.ItemData item) =>
            item.m_dropPrefab != null ? item.m_dropPrefab.name : item.m_shared.m_name;

        public static string ItemEnglish(ItemDrop.ItemData item)
        {
            var token = item.m_shared.m_name;
            return Localization.instance != null ? Localization.instance.Localize(token) : token;
        }

        public static string ItemLabel(ItemDrop.ItemData item) =>
            Translations.Get(item.m_shared.m_name) ?? ItemEnglish(item);

        public static string Biome(Vector3 pos) =>
            WorldGenerator.instance != null ? WorldGenerator.instance.GetBiome(pos).ToString() : null;

        public static string Clean(string s) => string.IsNullOrEmpty(s) ? s : Tags.Replace(s, "").Trim();

        internal struct SignText
        {
            public Vector3 Pos;
            public string Text;
        }

        public static string LabelFor(Vector3 pos, List<SignText> signs, float range = 3f)
        {
            string best = null;
            var bestD = range * range;
            foreach (var s in signs)
            {
                var d = (s.Pos - pos).sqrMagnitude;
                if (d < bestD) { bestD = d; best = s.Text; }
            }
            return best;
        }
    }
}
