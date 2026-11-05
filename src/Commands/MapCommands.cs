using System;
using System.Collections.Generic;
using System.Linq;

namespace Ratatoskr.Commands
{
    internal class Tombs : PagedCommand
    {
        public override string Command => "rtTombs";
        public override string Description => "Player tombstones with position, biome, in-game day of death and most valuable items. Usage: rtTombs [player] [-offset n]";

        protected override Result Produce(Args args)
        {
            var who = args.Text;
            var tombs = new List<(DateTime? When, string Json)>();
            foreach (var zdo in WorldIndex.Tombs())
            {
                var owner = zdo.GetString(ZDOVars.s_ownerName);
                if (who.Length > 0 && owner.IndexOf(who, StringComparison.OrdinalIgnoreCase) < 0) continue;
                PlayerNames.Learn(zdo.GetLong(ZDOVars.s_owner, 0L), owner);

                // World time, not wall-clock time.
                var ticks = zdo.GetLong(ZDOVars.s_timeOfDeath, 0L);
                DateTime? when = ticks > 0 ? new DateTime(ticks) : (DateTime?)null;
                var pos = zdo.GetPosition();
                var j = new Json().Obj()
                    .Prop("id", zdo.m_uid.ToString())
                    .Prop("owner", owner)
                    .Key("pos").Pos(pos)
                    .Prop("biome", World.Biome(pos))
                    .Key("diedDay");
                if (when != null && EnvMan.instance != null)
                    j.Val(EnvMan.instance.GetDay((when.Value - DateTime.MinValue).TotalSeconds));
                else
                    j.Null();
                var inv = World.LoadTombInventory(zdo);
                j.Prop("stacks", inv.NrOfItems()).Key("top").Arr();
                foreach (var item in inv.GetAllItems().OrderByDescending(i => i.m_shared.m_value * i.m_stack + i.m_quality).Take(5))
                    j.Val(World.ItemLabel(item) + (item.m_stack > 1 ? " x" + item.m_stack : ""));
                tombs.Add((when, j.EndArr().EndObj().ToString()));
            }
            PlayerNames.Save();
            return new Result { Items = tombs.OrderByDescending(t => t.When ?? DateTime.MinValue).Select(t => t.Json).ToList() };
        }
    }

    internal class Portals : PagedCommand
    {
        public override string Command => "rtPortals";
        public override string Description => "Portals grouped by tag; flags unpaired ones. Usage: rtPortals [-offset n]";

        protected override Result Produce(Args args)
        {
            var portals = WorldIndex.Portals()
                .Select(z => (Zdo: z, Info: World.Info(z), Tag: z.GetString(ZDOVars.s_tag)))
                .ToList();
            var byTag = portals.GroupBy(p => p.Tag).ToDictionary(g => g.Key, g => g.Count());
            return new Result
            {
                Items = portals.OrderBy(p => p.Tag).Select(p =>
                {
                    var pos = p.Zdo.GetPosition();
                    return new Json().Obj()
                        .Prop("tag", p.Tag)
                        .Prop("prefab", p.Info.Name)
                        .Key("pos").Pos(pos)
                        .Prop("biome", World.Biome(pos))
                        .Prop("paired", byTag[p.Tag] == 2)
                        .Prop("sameTag", byTag[p.Tag])
                        .EndObj().ToString();
                }).ToList(),
            };
        }
    }

    internal class Builds : PagedCommand
    {
        public override string Command => "rtBuilds";
        public override string Description => "Build pieces per creator (name when known). Usage: rtBuilds [-near x z r] [-offset n]";

        protected override Result Produce(Args args)
        {
            var counts = new Dictionary<long, int>();
            foreach (var zdo in World.All())
            {
                var creator = zdo.GetLong(ZDOVars.s_creator, 0L);
                if (creator == 0L || !args.InRange(zdo.GetPosition())) continue;
                counts.TryGetValue(creator, out var c);
                counts[creator] = c + 1;
            }
            return new Result
            {
                Items = counts.OrderByDescending(kv => kv.Value)
                    .Select(kv => new Json().Obj().Prop("playerId", kv.Key).Prop("name", PlayerNames.Get(kv.Key)).Prop("pieces", kv.Value).EndObj().ToString())
                    .ToList(),
            };
        }
    }
}
