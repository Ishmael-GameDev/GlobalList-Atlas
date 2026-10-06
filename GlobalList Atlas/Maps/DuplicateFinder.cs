using System;
using System.Collections.Generic;
using System.Linq;

namespace GlobalListAtlas.Maps;

public static class DuplicateFinder
{
    public static string MetadataKey(MapRow m)
    {
        static string Sorted<T>(IEnumerable<T> items) => string.Join(",", items.Select(i => i.ToString()).OrderBy(x => x, StringComparer.Ordinal));
        return string.Join("", new[]
        {
            m.Catalog.ToString(), m.Name ?? "", m.Creator ?? "", m.Stars.ToString(), m.Verified.ToString(),
            m.Downloads.ToString(), m.Likes.ToString(), m.Description ?? "",
            m.Uploaded?.Ticks.ToString() ?? "", m.Updated?.Ticks.ToString() ?? "",
            m.Difficulty.ToString(), m.Duration.ToString(),
            Sorted(m.ServerTags), Sorted(m.Tags), Sorted(m.Editors)
        });
    }

    public static List<List<MapRow>> FindCandidates(IEnumerable<MapRow> maps) =>
        maps.GroupBy(MetadataKey).Where(g => g.Count() > 1).Select(g => g.ToList()).ToList();

    public static void HideOlder(IReadOnlyList<MapRow> group)
    {
        var newest = group
            .OrderByDescending(m => m.Uploaded ?? DateTime.MinValue)
            .ThenBy(m => m.ServerOrder)
            .First();
        foreach (var m in group)
            if (m != newest) m.HiddenDuplicate = true;
    }
}
