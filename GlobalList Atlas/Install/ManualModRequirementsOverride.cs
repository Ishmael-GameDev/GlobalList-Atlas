using System.Collections.Generic;

namespace GlobalListAtlas.Install;

public static class ManualModRequirementsOverride
{
    private static readonly Dictionary<string, string> LumaflyLinksByMapName =
        new(System.StringComparer.OrdinalIgnoreCase)
        {
            ["The Suffering Upon The Silver"] = "https://themulhima.github.io/Lumafly/commands/download/?mods=RandomGravityChange",
            ["MoYAI"] = "https://themulhima.github.io/Lumafly/commands/download/?mods=TestOfTeamwork",
        };

    public static List<string> TryGetRequiredMods(string mapName)
    {
        if (mapName == null)
            return null;

        return LumaflyLinksByMapName.TryGetValue(mapName.Trim(), out var url)
            ? LumaflyLinkParser.ExtractRequiredMods(url)
            : null;
    }
}