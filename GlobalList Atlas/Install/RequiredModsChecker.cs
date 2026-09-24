using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GlobalListAtlas.Util;
using Modding;

namespace GlobalListAtlas.Install;

// Проверяет, установлены ли публичные моды, требуемые картой.
public static class RequiredModsChecker
{
    public static List<int> GetMissingModIndices(List<string> requiredMods)
    {
        var missing = new List<int>();
        if (requiredMods == null)
            return missing;

        for (int i = 0; i < requiredMods.Count; i++)
        {
            if (!IsModInstalled(requiredMods[i]))
                missing.Add(i);
        }

        return missing;
    }

    public static bool AllRequiredModsInstalled(List<string> requiredMods) =>
        GetMissingModIndices(requiredMods).Count == 0;

    // Мод считается установленным, если он загружен Modding API, либо уже лежит в папке Mods и ждёт перезапуска игры
    public static bool IsModInstalled(string modName) =>
        IsModLoaded(modName) || IsModPendingRestart(modName);

    // Мод реально загружен в текущей сессии игры (штатный способ Modding API).
    public static bool IsModLoaded(string modName)
    {
        if (string.IsNullOrWhiteSpace(modName))
            return false;

        try
        {
            return ModHooks.GetMod(modName.Trim(), onlyEnabled: false, allowLoadError: true) != null;
        }
        catch
        {
            return false;
        }
    }

    // Мод не загружен, но его папка уже есть в Mods
    public static bool IsModPendingRestart(string modName)
    {
        if (string.IsNullOrWhiteSpace(modName) || IsModLoaded(modName))
            return false;

        string modsFolder = GamePaths.ModsFolder;
        if (!Directory.Exists(modsFolder))
            return false;

        string wanted = Normalize(modName);

        try
        {
            return Directory.GetDirectories(modsFolder)
                .Where(d => !string.Equals(Path.GetFileName(d), "Disabled", StringComparison.OrdinalIgnoreCase))
                .Any(d => Normalize(Path.GetFileName(d)) == wanted);
        }
        catch
        {
            return false;
        }
    }

    // Имена в ModLinks и названия папок иногда расходятся регистром и пробелами
    private static string Normalize(string name) =>
        new string(name.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToLowerInvariant();
}
