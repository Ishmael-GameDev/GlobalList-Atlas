using System;
using System.Collections.Generic;
using System.IO;
using GlobalListAtlas.Configuration;
using GlobalListAtlas.Logging;
using GlobalListAtlas.Util;

namespace GlobalListAtlas.Install;

// Определяет, какие из трёх известных редакторов карт установлены у игрока.
// Проверка по конкретным dll: так однозначно различаются Legacy Architect
// (Architect.dll) и New Architect (Architect_HK.dll).
public static class EditorInstallDetector
{
    private static readonly string DecorationMasterDll = Path.Combine("DecorationMaster", "DecorationMaster.dll");
    private static readonly string ArchitectLegacyDll = Path.Combine("ArchitectLegacy", "Architect.dll");
    private static readonly string ArchitectDll = Path.Combine("Architect", "Architect_HK.dll");

    private static bool IsDllInstalled(string dllPathInModsFolder)
    {
        try
        {
            string fullPath = Path.Combine(GamePaths.ModsFolder, dllPathInModsFolder);
            bool exists = File.Exists(fullPath);
            Log.Info($"[EditorInstallDetector] {dllPathInModsFolder}: {(exists ? "найден" : "не найден")}");
            return exists;
        }
        catch (Exception ex)
        {
            Log.Error($"[EditorInstallDetector] Ошибка при проверке {dllPathInModsFolder}: {ex.Message}");
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