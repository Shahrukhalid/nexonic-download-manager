using System.Globalization;
using System.Text.RegularExpressions;

namespace Nexonic.Core;

/// <summary>
/// Expands download patterns such as https://example.com/file[001-100].jpg, photo[a-z].png or part[0-90:10].bin
/// (from–to, optional step) or a list such as {jpg,png,webp}. Several ranges multiply. Brackets that are not ranges stay as they are.
/// </summary>
public static class BatchPattern
{
    public const int Limit = 10_000;
    static readonly Regex Range = new(@"\[(?:(\d+)-(\d+)(?::(\d+))?|([a-zA-Z])-([a-zA-Z]))\]|\{([^{}]*,[^{}]*)\}", RegexOptions.Compiled);

    public static long Count(string pattern) => Parts(pattern).Aggregate(1L, (n, p) => n > int.MaxValue ? n : n * p.Count);

    public static List<string> Expand(string pattern)
    {
        var total = Count(pattern);
        if (total > Limit) throw new ArgumentException($"The pattern makes {total} links; the limit is {Limit}");
        var outList = new List<string> { "" };
        foreach (var p in Parts(pattern)) outList = outList.SelectMany(prefix => p.Select(v => prefix + v)).ToList();
        return outList;
    }

    static List<List<string>> Parts(string pattern)
    {
        var parts = new List<List<string>>();
        var at = 0;
        foreach (Match m in Range.Matches(pattern))
        {
            parts.Add(new() { pattern[at..m.Index] });
            at = m.Index + m.Length;
            if (m.Groups[1].Success)
            {
                string a = m.Groups[1].Value, b = m.Groups[2].Value;
                long from = long.Parse(a, CultureInfo.InvariantCulture), to = long.Parse(b, CultureInfo.InvariantCulture);
                var step = m.Groups[3].Success ? Math.Max(1, long.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture)) : 1;
                var width = (a.Length > 1 && a.StartsWith('0')) || a.Length == b.Length ? a.Length : 0;
                var values = new List<string>();
                var n = Math.Abs(to - from) / step + 1;
                if (n > Limit) n = Limit + 1; // enough to report "too many" without building millions
                for (long i = 0; i < n; i++)
                {
                    var v = from <= to ? from + i * step : from - i * step;
                    values.Add(width > 0 ? v.ToString(new string('0', width), CultureInfo.InvariantCulture) : v.ToString(CultureInfo.InvariantCulture));
                }
                parts.Add(values);
            }
            else if (m.Groups[6].Success) parts.Add(m.Groups[6].Value.Split(',').Select(v => v.Trim()).ToList());
            else
            {
                char a = m.Groups[4].Value[0], b = m.Groups[5].Value[0];
                var values = new List<string>();
                if (a <= b) for (var c = a; c <= b; c++) values.Add(c.ToString());
                else for (var c = a; c >= b; c--) values.Add(c.ToString());
                parts.Add(values);
            }
        }
        parts.Add(new() { pattern[at..] });
        return parts;
    }
}
