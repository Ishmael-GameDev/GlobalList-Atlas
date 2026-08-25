using System;
using System.Collections.Generic;
using System.Linq;

namespace GlobalListAtlas.Install;

// Разбирает ссылку вида https://themulhima.github.io/Lumafly/commands/download/?mods=Satchel/Scattered%20and%20Lost на список имён модов
public static class LumaflyLinkParser
{
    public static List<string> ExtractRequiredMods(string lumaflyUrl)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(lumaflyUrl))
            return result;

        try
        {
            var uri = new Uri(lumaflyUrl);
            string query = uri.Query.TrimStart('?');
            if (string.IsNullOrEmpty(query))
                return result;

            foreach (var pair in query.Split('&'))
            {
                var kv = pair.Split(new[] { '=' }, 2);
                if (kv.Length != 2 || kv[0] != "mods")
                    continue;

                string decoded = Uri.UnescapeDataString(kv[1]);
                result.AddRange(
                    decoded.Split('/')
                        .Select(s => s.Trim())
                        .Where(s => !string.IsNullOrEmpty(s)));
            }
        }
        catch (Exception e)
        {
            Modding.Logger.Log($"Не удалось разобрать ссылку Lumafly '{lumaflyUrl}': {e.Message}");
        }

        return result;
    }
}