using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ValheimRcon.Commands;

namespace Ratatoskr.Commands
{
    internal abstract class RtCommand : RconCommand
    {
        protected override string OnHandle(CommandArgs args)
        {
            if (ZDOMan.instance == null || ZNet.instance == null)
                return new Json().Obj().Prop("error", "world not loaded").EndObj().ToString();
            try
            {
                return Handle(new Args(args.Arguments), string.Join(" ", args.Arguments));
            }
            catch (System.Exception e)
            {
                return new Json().Obj().Prop("error", e.Message).EndObj().ToString();
            }
        }

        protected abstract string Handle(Args args, string raw);
    }

    internal sealed class Result
    {
        public List<string> Items = new List<string>();
        public string Extra;
    }

    internal abstract class PagedCommand : RtCommand
    {
        private static readonly Dictionary<string, (float At, Result R)> Snapshots = new Dictionary<string, (float, Result)>();

        protected override string Handle(Args args, string raw)
        {
            var key = Command + " " + Args.WithoutOffset(raw);
            Result r;
            if (args.Offset == 0 || !Snapshots.TryGetValue(key, out var snap) || Time.time - snap.At > 60f)
            {
                r = Produce(args);
                foreach (var stale in Snapshots.Where(kv => Time.time - kv.Value.At > 60f).Select(kv => kv.Key).ToList())
                    Snapshots.Remove(stale);
                Snapshots[key] = (Time.time, r);
            }
            else r = snap.R;
            return Pager.Page(r.Items, args.Offset, r.Extra);
        }

        protected abstract Result Produce(Args args);
    }
}
