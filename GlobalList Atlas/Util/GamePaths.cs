using System.IO;
using UnityEngine;

namespace GlobalListAtlas.Util;

// Пути игры с учётом платформы. Логика папки Managed повторяет ModLoader из Modding API: на macOS она лежит в Resources/Data внутри .app-бандла.
public static class GamePaths
{
    private static string _managedFolder;

    public static string ManagedFolder => _managedFolder ??= ResolveManagedFolder();

    public static string ModsFolder => Path.Combine(ManagedFolder, "Mods");

    public static string DisabledModsFolder => Path.Combine(ModsFolder, "Disabled");

    public static string DecorationMasterDataFolder => Path.Combine(ModsFolder, "DecorationMasterData");

    // persistentDataPath Unity уже возвращает корректно для каждой платформы
    public static string ArchitectDataFolder => Path.Combine(Application.persistentDataPath, "Architect");

    private static string ResolveManagedFolder() => SystemInfo.operatingSystemFamily switch
    {
        OperatingSystemFamily.MacOSX => Path.Combine(Application.dataPath, "Resources", "Data", "Managed"),
        _ => Path.Combine(Application.dataPath, "Managed")
    };
}
