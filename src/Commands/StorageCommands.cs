using System;
using System.Collections.Generic;
using System.Linq;

namespace Ratatoskr.Commands
{
    internal sealed class Stored
    {
        public ZDO Zdo;
        public World.PrefabInfo Info;
        public string Label;
        public Contents Contents;

        public static List<Stored> Scan(Args args)
        {
            var signs = WorldIndex.Signs();
            var list = new List<Stored>();
            foreach (var zdo in WorldIndex.Containers())
            {
                var info = World.Info(zdo);
                if (!World.IsStorage(zdo, info, args.All)) continue;
                var pos = zdo.GetPosition();
                if (!args.InRange(pos)) continue;
                list.Add(new Stored
                {
                    Zdo = zdo, Info = info, Label = World.LabelFor(pos, signs),
                    Contents = InventoryCache.Get(zdo, info.Container),
                });
            }
            PlayerNames.Save();
            return list;
        }

        public static bool Matches(string id, (string Label, string English, int Count) item, string query) =>
            Has(id, query) || Has(item.Label, query) || Has(item.English, query);

        public static bool Has(string s, string q) =>
            s != null && s.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;

        public Json Head(Json j) => j
            .Prop("id", Zdo.m_uid.ToString())
            .Prop("prefab", Info.Name)
            .Prop("label", Label)
            .Key("pos").Pos(Zdo.GetPosition());
    }

    internal class Containers : PagedCommand
    {
        public override string Command => "rtContainers";
        public override string Description => "Player storage (chests, carts, ships) with label from the nearest sign. Usage: rtContainers [-near x z r] [-all] [-offset n]";

        protected override Result Produce(Args args) => new Result
        {
            Items = Stored.Scan(args)
                .OrderBy(s => s.Label == null).ThenBy(s => s.Label)
                .Select(s => s.Head(new Json().Obj())
                    .Prop("slots", s.Contents.Slots)
                    .Prop("capacity", s.Info.Container.m_width * s.Info.Container.m_height)
                    .EndObj().ToString())
                .ToList(),
        };
    }

    internal class ContainerContents : PagedCommand
    {
        public override string Command => "rtContainer";
        public override string Description => "Contents of one container, aggregated by item. Usage: rtContainer <id|sign text> [-offset n]";

        protected override Result Produce(Args args)
        {
            var key = args.Text;
            if (key.Length == 0) throw new ArgumentException("usage: rtContainer <id|sign text>");
            var all = Stored.Scan(new Args(new[] { "-all" }));
            var s = all.FirstOrDefault(x => x.Zdo.m_uid.ToString() == key)
                ?? all.FirstOrDefault(x => Stored.Has(x.Label, key))
                ?? throw new ArgumentException("no container matching " + key);
            return new Result
            {
                Items = s.Contents.Items.OrderByDescending(kv => kv.Value.Count)
                    .Select(kv => new Json().Obj().Prop("item", kv.Key).Prop("name", kv.Value.Label).Prop("count", kv.Value.Count).EndObj().ToString())
                    .ToList(),
                Extra = "\"container\":" + s.Head(new Json().Obj()).EndObj(),
            };
        }
    }

    internal class Find : PagedCommand
    {
        public override string Command => "rtFind";
        public override string Description => "Containers holding an item (substring of prefab, translated or English name), biggest stacks first. Usage: rtFind <query> [-near x z r] [-offset n]";

        protected override Result Produce(Args args)
        {
            var q = args.Text;
            if (q.Length == 0) throw new ArgumentException("usage: rtFind <query>");
            var hits = new List<(Stored S, List<KeyValuePair<string, (string Label, string English, int Count)>> Items, int Total)>();
            foreach (var s in Stored.Scan(args))
            {
                var m = s.Contents.Items.Where(kv => Stored.Matches(kv.Key, kv.Value, q)).ToList();
                if (m.Count > 0) hits.Add((s, m, m.Sum(kv => kv.Value.Count)));
            }
            var totals = new Json().Obj();
            foreach (var g in hits.SelectMany(h => h.Items).GroupBy(kv => kv.Key))
                totals.Prop(g.First().Value.Label, g.Sum(kv => kv.Value.Count));
            return new Result
            {
                Items = hits.OrderByDescending(h => h.Total).Select(h =>
                {
                    var j = h.S.Head(new Json().Obj()).Key("items").Arr();
                    foreach (var kv in h.Items.OrderByDescending(kv => kv.Value.Count))
                        j.Obj().Prop("item", kv.Key).Prop("name", kv.Value.Label).Prop("count", kv.Value.Count).EndObj();
                    return j.EndArr().EndObj().ToString();
                }).ToList(),
                Extra = "\"totals\":" + totals.EndObj(),
            };
        }
    }

    internal class Stock : PagedCommand
    {
        public override string Command => "rtStock";
        public override string Description => "Total of every item across player storage, largest first. Usage: rtStock [query] [-near x z r] [-offset n]";

        protected override Result Produce(Args args)
        {
            var q = args.Text;
            var totals = new Dictionary<string, (string Label, int Count, int Containers)>();
            foreach (var s in Stored.Scan(args))
                foreach (var kv in s.Contents.Items)
                {
                    if (q.Length > 0 && !Stored.Matches(kv.Key, kv.Value, q)) continue;
                    totals.TryGetValue(kv.Key, out var cur);
                    totals[kv.Key] = (kv.Value.Label, cur.Count + kv.Value.Count, cur.Containers + 1);
                }
            return new Result
            {
                Items = totals.OrderByDescending(kv => kv.Value.Count)
                    .Select(kv => new Json().Obj().Prop("item", kv.Key).Prop("name", kv.Value.Label)
                        .Prop("count", kv.Value.Count).Prop("containers", kv.Value.Containers).EndObj().ToString())
                    .ToList(),
            };
        }
    }

    internal class JournalCommand : PagedCommand
    {
        public override string Command => "rtJournal";
        public override string Description => "Chest journal, newest first: who added/took what. Usage: rtJournal [item or chest] [-player name] [-near x z r] [-days n] [-offset n]";

        protected override Result Produce(Args args)
        {
            var q = args.Text;
            var cutoff = args.Days > 0 ? Events.Now - (long)(args.Days * 86400000) : 0;
            var items = new List<string>();
            for (var i = Tracker.Journal.Count - 1; i >= 0; i--)
            {
                var e = Tracker.Journal[i];
                if (e.T < cutoff) break;
                if (args.Player != null && !string.Equals(e.Player, args.Player, StringComparison.OrdinalIgnoreCase)) continue;
                if (!args.InRange(new UnityEngine.Vector3(e.X, 0, e.Z))) continue;
                if (q.Length > 0 && !Stored.Has(e.Item, q) && !Stored.Has(e.Label, q) && !Stored.Has(e.ContainerLabel, q)) continue;
                items.Add(new Json().Obj().Prop("t", e.T).Prop("player", e.Player).Prop("delta", e.Delta)
                    .Prop("item", e.Item).Prop("name", e.Label).Prop("container", e.Container)
                    .Prop("label", e.ContainerLabel).Key("pos").Arr().Val((double)e.X).Val((double)e.Z).EndArr()
                    .EndObj().ToString());
            }
            return new Result { Items = items };
        }
    }
}
