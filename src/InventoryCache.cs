using System.Collections.Generic;

namespace Ratatoskr
{
    internal sealed class Contents
    {
        public uint Revision;
        public int Slots;
        public readonly Dictionary<string, (string Label, string English, int Count)> Items =
            new Dictionary<string, (string, string, int)>();
    }

    // Decoding inventories is the expensive part of storage queries; any write to a ZDO bumps DataRevision.
    internal static class InventoryCache
    {
        private static readonly Dictionary<ZDOID, Contents> Cache = new Dictionary<ZDOID, Contents>();

        public static Contents Get(ZDO zdo, Container container)
        {
            if (Cache.TryGetValue(zdo.m_uid, out var c) && c.Revision == zdo.DataRevision) return c;

            var inv = container != null ? World.LoadInventory(zdo, container) : World.LoadTombInventory(zdo);
            c = new Contents { Revision = zdo.DataRevision };
            foreach (var item in inv.GetAllItems())
            {
                c.Slots++;
                PlayerNames.Learn(item.m_crafterID, item.m_crafterName);
                var id = World.ItemId(item);
                c.Items.TryGetValue(id, out var cur);
                c.Items[id] = (World.ItemLabel(item), World.ItemEnglish(item), cur.Count + item.m_stack);
            }
            Cache[zdo.m_uid] = c;
            return c;
        }

        public static void Prune(List<ZDOID> storage, List<ZDOID> tombs)
        {
            var keep = new HashSet<ZDOID>(storage);
            keep.UnionWith(tombs);
            var stale = new List<ZDOID>();
            foreach (var id in Cache.Keys) if (!keep.Contains(id)) stale.Add(id);
            foreach (var id in stale) Cache.Remove(id);
        }
    }
}
