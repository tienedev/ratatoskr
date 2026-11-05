using System.Collections.Generic;
using System.Text;

namespace Ratatoskr
{
    // ValheimRcon truncates replies above ~4 KB, so lists are paged.
    internal static class Pager
    {
        private const int Budget = 3800;

        public static string Page(IList<string> items, int offset, string extraProps = null)
        {
            var sb = new StringBuilder();
            var used = 0;
            var i = offset;
            for (; i < items.Count; i++)
            {
                var len = Encoding.UTF8.GetByteCount(items[i]) + 1;
                if (i > offset && used + len > Budget - 120) break;
                if (i > offset) sb.Append(',');
                sb.Append(items[i]);
                used += len;
            }

            var head = new StringBuilder();
            head.Append("{\"total\":").Append(items.Count)
                .Append(",\"offset\":").Append(offset)
                .Append(",\"next\":").Append(i < items.Count ? i.ToString() : "null");
            if (!string.IsNullOrEmpty(extraProps)) head.Append(',').Append(extraProps);
            head.Append(",\"items\":[").Append(sb).Append("]}");
            return head.ToString();
        }
    }
}
