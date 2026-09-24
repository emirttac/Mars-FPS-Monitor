using System;
using System.Text.RegularExpressions;

namespace FPSOverlay
{
    /// <summary>Normalize and compare dotted version strings (tag-friendly).</summary>
    public static class AppVersionComparer
    {
        public static string Normalize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            var m = Regex.Match(raw.Trim(), @"\d+(?:\.\d+)*");
            return m.Success ? m.Value : "";
        }

        /// <summary>Returns &gt;0 if a is newer than b.</summary>
        public static int Compare(string a, string b)
        {
            var pa = a.Split('.', StringSplitOptions.RemoveEmptyEntries);
            var pb = b.Split('.', StringSplitOptions.RemoveEmptyEntries);
            int n = Math.Max(pa.Length, pb.Length);
            for (int i = 0; i < n; i++)
            {
                int va = i < pa.Length && int.TryParse(pa[i], out var x) ? x : 0;
                int vb = i < pb.Length && int.TryParse(pb[i], out var y) ? y : 0;
                if (va != vb) return va.CompareTo(vb);
            }
            return 0;
        }
    }
}
