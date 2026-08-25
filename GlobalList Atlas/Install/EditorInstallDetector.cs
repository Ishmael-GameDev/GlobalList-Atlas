using System.Collections.Generic;
using System.IO;
using UnityEngine;
using GlobalListAtlas.Configuration;
using System;

namespace GlobalListAtlas.Install;

// Определяет, какие из трех известных редакторов карт установлены у игрока
public static class EditorInstallDetector
{
    private static readonly string DecorationMasterDll = Path.Combine("Mods", "DecorationMaster", "DecorationMaster.dll");
    private static readonly string ArchitectLegacyDll = Path.Combine("Mods", "ArchitectLegacy", "Architect.dll");
    private static readonly string ArchitectDll = Path.Combine("Mods", "Architect", "Architect_HK.dll");

    private static bool IsDllInstalled(string relativeDllPath)
    {
        try
        {
            string fullPath = Path.Combine(Application.dataPath, "Managed", relativeDllPath);

            if (!File.Exists(fullPath))
            {
                UnityEngine.Debug.Log($"[MapDownloader] DLL не найден: {relativeDllPath}");
                return false;
            }

            var dir = new DirectoryInfo(Path.GetDirectoryName(fullPath));
            while (dir != null)
            {
                if (string.Equals(dir.Name, "Disabled", StringComparison.OrdinalIgnoreCase))
                {
                    UnityEngine.Debug.Log($"[MapDownloader] DLL найден, но пропущен (находится в Disabled): {relativeDllPath}");
                    return false;
                }

                dir = dir.Parent;
            }

            UnityEngine.Debug.Log($"[MapDownloader] DLL успешно найден и активен: {relativeDllPath}");
            return true;
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogError($"[MapDownloader] Ошибка при проверке DLL {relativeDllPath}: {ex.Message}");
            return false;
        }
    }

    public static HashSet<MapEditor> GetInstalledEditors()
    {
        var installed = new HashSet<MapEditor>();

        if (IsDllInstalled(DecorationMasterDll)) installed.Add(MapEditor.DecorationMaster);
        if (IsDllInstalled(ArchitectLegacyDll)) installed.Add(MapEditor.LegacyArchitect);
        if (IsDllInstalled(ArchitectDll)) installed.Add(MapEditor.NewArchitect);

        return installed;
    }

    public static bool IsEditorInstalled(MapEditor editor) => GetInstalledEditors().Contains(editor);
}