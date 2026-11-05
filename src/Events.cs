using System;
using System.Collections.Generic;

namespace Ratatoskr
{
    internal static class Events
    {
        private const int Capacity = 1000;
        private static readonly LinkedList<(long Seq, string Json)> Ring = new LinkedList<(long, string)>();

        public static readonly string Boot = Guid.NewGuid().ToString("N").Substring(0, 8);
        public static long Seq { get; private set; }

        public static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        public static void Add(string type, Action<Json> props = null)
        {
            var j = new Json().Obj().Prop("seq", ++Seq).Prop("t", Now).Prop("type", type);
            props?.Invoke(j);
            Ring.AddLast((Seq, j.EndObj().ToString()));
            while (Ring.Count > Capacity) Ring.RemoveFirst();
        }

        public static List<string> Since(long seq)
        {
            var list = new List<string>();
            foreach (var e in Ring)
                if (e.Seq > seq) list.Add(e.Json);
            return list;
        }
    }
}
