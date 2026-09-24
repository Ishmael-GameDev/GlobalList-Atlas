using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using GlobalListAtlas.Configuration;
using GlobalListAtlas.Logging;
using GlobalListAtlas.Maps;

namespace GlobalListAtlas.Install;

public class DllInstallResult
{
    public int Found;
    public int Installed;
    public int Skipped;
}

public static class MapFileDistributor
{
    private static string DecorationMasterJsonFolder =>
        Path.Combine(Application.dataPath, "Managed", "Mods", "DecorationMasterData");

    private static string LegacyArchitectJsonFolder =>
        Path.Combine(Application.persistentDataPath, "Architect");

    private static string NewArchitectJsonFolder =>
        Path.Combine(Application.persistentDataPath, "Architect");

    private static string ManagedModsFolder =>
        Path.Combine(Application.dataPath, "Managed", "Mods");

    public static void DistributeMapFiles(string extractedArchiveFolder, List<MapEditor> editors)
    {
        if (editors == null || editors.Count == 0)
        {
            Modding.Logger.Log("У карты не указан ни один редактор — распределение файлов пропущено");
            return;
        }

        if (!Directory.Exists(extractedArchiveFolder))
        {
            Modding.Logger.Log($"Папка распакованного архива не найдена: {extractedArchiveFolder}");
            return;
        }

        bool anyHandled = false;

        if (editors.Contains(MapEditor.DecorationMaster))
            anyHandled |= DistributeDecorationMasterFiles(extractedArchiveFolder, DecorationMasterJsonFolder);

        if (editors.Contains(MapEditor.LegacyArchitect))
            anyHandled |= DistributeAllFiles(extractedArchiveFolder, ResolveArchitectFolder(MapEditor.LegacyArchitect));

        if (editors.Contains(MapEditor.NewArchitect))
            anyHandled |= DistributeNewArchitect(extractedArchiveFolder, ResolveArchitectFolder(MapEditor.NewArchitect));

        if (!anyHandled)
        {
            string editorLabels = string.Join(", ", editors.Select(EditorConfig.GetLabel));
            Modding.Logger.Log($"Ни один из редакторов карты ({editorLabels}) не поддерживает распределение файлов — пропускаю");
        }
    }

    // Decoration Master: переносятся файлы с расширениями .json, .txt, .png, .jpg, .jpeg
    private static bool DistributeDecorationMasterFiles(string extractedArchiveFolder, string targetFolder)
    {
        var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".json", ".txt", ".png", ".jpg", ".jpeg" };
        var allFiles = CollectFilesDeduped(extractedArchiveFolder, null);

        var targetFiles = allFiles.Where(kvp => allowedExtensions.Contains(Path.GetExtension(kvp.Key)))
                                  .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);

        if (targetFiles.Count == 0)
        {
            Modding.Logger.Log("В архиве карты не найдено нужных файлов (.json, .txt, .png, .jpg) — распределять для Decoration Master нечего");
            return false;
        }

        Directory.CreateDirectory(targetFolder);
        BackupExistingFiles(targetFolder, "файлов Decoration Master");

        int copied = CopyFilesFlat(targetFiles, targetFolder);
        Modding.Logger.Log($"Распределено {copied} файлов в '{targetFolder}' (Decoration Master)");
        return true;
    }

    private static bool DistributeAllFiles(string extractedArchiveFolder, string targetFolder)
    {
        var allFiles = CollectFilesDeduped(extractedArchiveFolder, null);
        if (allFiles.Count == 0)
        {
            Modding.Logger.Log($"В архиве карты не найдено файлов — распределять в '{targetFolder}' нечего");
            return false;
        }

        Directory.CreateDirectory(targetFolder);
        BackupExistingFiles(targetFolder, "файлов карты");

        int copied = CopyFilesFlat(allFiles, targetFolder);
        Modding.Logger.Log($"Распределены ВСЕ файлы архива ({copied} шт.) плоским списком в '{targetFolder}' (Legacy Architect)");
        return true;
    }

    private static bool DistributeNewArchitect(string extractedArchiveFolder, string targetFolder)
    {
        string baseFolder = FindNewArchitectBase(extractedArchiveFolder);
        if (baseFolder == null)
        {
            Modding.Logger.Log("В архиве New Architect не найдена папка 'Scenes', копируем весь архив с сохранением иерархии.");
            baseFolder = extractedArchiveFolder;
        }

        var sourceFiles = Directory.GetFiles(baseFolder, "*.*", SearchOption.AllDirectories)
            .Where(f => !IsPathInFolder(f, "Backups"))
            .ToList();

        if (sourceFiles.Count == 0)
        {
            Modding.Logger.Log($"В архиве карты (после исключения Backups) не найдено файлов — распределять в '{targetFolder}' нечего");
            return false;
        }

        Directory.CreateDirectory(targetFolder);
        BackupExistingFiles(targetFolder, "файлов New Architect");

        int copied = 0;
        foreach (string newPath in sourceFiles)
        {
            try
            {
                string relativePath = newPath.Substring(baseFolder.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string destPath = Path.Combine(targetFolder, relativePath);

                string destDir = Path.GetDirectoryName(destPath);
                if (!Directory.Exists(destDir))
                    Directory.CreateDirectory(destDir);

                File.Copy(newPath, destPath, overwrite: true);
                copied++;
            }
            catch (Exception e)
            {
                Modding.Logger.Log($"Не удалось скопировать {newPath}: {e.Message}");
            }
        }

        Modding.Logger.Log($"Распределены файлы New Architect с иерархией ({copied} шт.) в '{targetFolder}'");
        return true;
    }

    private static string FindNewArchitectBase(string root)
    {
        var scenesDirs = Directory.GetDirectories(root, "Scenes", SearchOption.AllDirectories)
            .Where(d => !IsPathInFolder(d, "Backups"))
            .ToList();

        if (scenesDirs.Count > 0)
        {
            return Directory.GetParent(scenesDirs[0]).FullName;
        }
        return null;
    }

    private static bool IsPathInFolder(string path, string folderName)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Contains(folderName, StringComparer.OrdinalIgnoreCase);
    }

    private static int CopyFilesFlat(Dictionary<string, string> files, string targetFolder)
    {
        int copied = 0;
        foreach (var kvp in files)
        {
            string fileName = kvp.Key;
            string sourcePath = kvp.Value;
            string destPath = Path.Combine(targetFolder, fileName);

            try
            {
                File.Copy(sourcePath, destPath, overwrite: true);
                copied++;
            }
            catch (Exception e)
            {
                Modding.Logger.Log($"Не удалось скопировать {fileName} в {targetFolder}: {e.Message}");
            }
        }

        return copied;
    }

    public static string GetEditorFolder(MapEditor editor) => editor switch
    {
        MapEditor.DecorationMaster => DecorationMasterJsonFolder,
        MapEditor.LegacyArchitect => LegacyArchitectJsonFolder,
        MapEditor.NewArchitect => NewArchitectJsonFolder,
        _ => null
    };

    public static void BackupCurrentEditorFiles(string editorFolder, string label) =>
        BackupExistingFiles(editorFolder, label);

    public static int RemoveMapFilesFromEditors(string extractedArchiveFolder, List<MapEditor> editors)
    {
        if (editors == null || editors.Count == 0 || !Directory.Exists(extractedArchiveFolder))
            return 0;

        var sourceFiles = CollectFilesDeduped(extractedArchiveFolder, null);
        if (sourceFiles.Count == 0)
            return 0;

        int removed = 0;

        foreach (var editor in editors.Distinct())
        {
            string editorFolder = GetEditorFolder(editor);
            if (editorFolder == null || !Directory.Exists(editorFolder))
                continue;

            foreach (var targetPath in Directory.GetFiles(editorFolder, "*", SearchOption.AllDirectories))
            {
                // Бекапы — это история, удаление карты их не касается
                string relative = targetPath.Substring(editorFolder.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        .Any(part => part.StartsWith("GlobalistInstaller_backup_", StringComparison.OrdinalIgnoreCase)
                                  || part.StartsWith("backup_", StringComparison.OrdinalIgnoreCase)))
                    continue;

                if (!sourceFiles.TryGetValue(Path.GetFileName(targetPath), out var sourcePath))
                    continue;

                try
                {
                    if (new FileInfo(sourcePath).Length != new FileInfo(targetPath).Length)
                        continue;

                    File.Delete(targetPath);
                    removed++;
                }
                catch (Exception e)
                {
                    Log.Warn($"Не удалось удалить файл карты из папки редактора ({targetPath}): {e.Message}");
                }
            }

            RemoveEmptyDirectories(editorFolder);
        }

        if (removed > 0)
            Log.Info($"Из папок редакторов удалено файлов карты: {removed}");

        return removed;
    }

    // После удаления файлов остаются пустые подпапки (актуально для New Architect)
    private static void RemoveEmptyDirectories(string root)
    {
        foreach (var dir in Directory.GetDirectories(root))
        {
            string name = Path.GetFileName(dir);
            if (name.StartsWith("GlobalistInstaller_backup_", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("backup_", StringComparison.OrdinalIgnoreCase))
                continue;

            RemoveEmptyDirectories(dir);

            try
            {
                if (Directory.GetFileSystemEntries(dir).Length == 0)
                    Directory.Delete(dir);
            }
            catch { /* папка занята или недоступна — оставляем как есть */ }
        }
    }

    public static bool IsMapCurrentlyActive(string extractedArchiveFolder, List<MapEditor> editors)
    {
        if (editors == null || editors.Count == 0)
            return false;

        if (!Directory.Exists(extractedArchiveFolder))
            return false;

        bool anyChecked = false;

        if (editors.Contains(MapEditor.DecorationMaster))
        {
            var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".json", ".txt", ".png", ".jpg", ".jpeg" };
            var allFiles = CollectFilesDeduped(extractedArchiveFolder, null);
            var sourceFiles = allFiles.Where(kvp => allowedExtensions.Contains(Path.GetExtension(kvp.Key)))
                                      .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);

            if (sourceFiles.Count > 0)
            {
                anyChecked = true;
                if (!AreFilesPresentBySize(sourceFiles, DecorationMasterJsonFolder, "*"))
                    return false;
            }
        }

        if (editors.Contains(MapEditor.LegacyArchitect))
        {
            var sourceAll = CollectFilesDeduped(extractedArchiveFolder, null);
            if (sourceAll.Count > 0)
            {
                anyChecked = true;
                if (!AreFilesPresentBySize(sourceAll, LegacyArchitectJsonFolder, "*"))
                    return false;
            }
        }

        if (editors.Contains(MapEditor.NewArchitect))
        {
            string baseFolder = FindNewArchitectBase(extractedArchiveFolder) ?? extractedArchiveFolder;
            var sourceFiles = Directory.GetFiles(baseFolder, "*.*", SearchOption.AllDirectories)
                .Where(f => !IsPathInFolder(f, "Backups"))
                .ToList();

            if (sourceFiles.Count > 0)
            {
                anyChecked = true;
                if (!AreNewArchitectFilesPresent(sourceFiles, baseFolder, NewArchitectJsonFolder))
                    return false;
            }
        }

        return anyChecked;
    }

    private static bool AreFilesPresentBySize(Dictionary<string, string> sourceFiles, string targetFolder, string searchPattern)
    {
        if (!Directory.Exists(targetFolder))
            return false;

        var destFiles = Directory.GetFiles(targetFolder, searchPattern, SearchOption.TopDirectoryOnly);
        var destByName = destFiles.ToDictionary(Path.GetFileName, f => f, StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in sourceFiles)
        {
            if (!destByName.TryGetValue(kvp.Key, out var destPath))
                return false;

            try
            {
                if (new FileInfo(kvp.Value).Length != new FileInfo(destPath).Length)
                    return false;
            }
            catch (Exception e)
            {
                Modding.Logger.Log($"Не удалось сравнить размеры файлов '{kvp.Value}' и '{destPath}': {e.Message}");
                return false;
            }
        }

        return true;
    }

    private static bool AreNewArchitectFilesPresent(List<string> sourceFiles, string sourceBase, string targetFolder)
    {
        if (!Directory.Exists(targetFolder)) return false;

        foreach (var sourceFile in sourceFiles)
        {
            string relPath = sourceFile.Substring(sourceBase.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string destPath = Path.Combine(targetFolder, relPath);

            if (!File.Exists(destPath))
                return false;

            try
            {
                if (new FileInfo(sourceFile).Length != new FileInfo(destPath).Length)
                    return false;
            }
            catch
            {
                return false;
            }
        }
        return true;
    }

    public static bool HasLaunchableTarget(List<MapEditor> editors)
    {
        if (editors == null) return false;
        return editors.Any(e => e == MapEditor.DecorationMaster || e == MapEditor.LegacyArchitect || e == MapEditor.NewArchitect);
    }

    private static string ResolveArchitectFolder(MapEditor editor) =>
        editor == MapEditor.LegacyArchitect ? LegacyArchitectJsonFolder : NewArchitectJsonFolder;

    public static DllInstallResult InstallDlls(string extractedArchiveFolder)
    {
        var result = new DllInstallResult();

        if (!Directory.Exists(extractedArchiveFolder))
        {
            Modding.Logger.Log($"Папка распакованного архива не найдена: {extractedArchiveFolder}");
            return result;
        }

        var dllFiles = CollectFilesDeduped(extractedArchiveFolder, ".dll");
        result.Found = dllFiles.Count;

        if (dllFiles.Count == 0)
        {
            return result;
        }

        foreach (var kvp in dllFiles)
        {
            string fileName = kvp.Key;
            string sourcePath = kvp.Value;
            string dllNameNoExt = Path.GetFileNameWithoutExtension(fileName);
            string targetFolder = Path.Combine(ManagedModsFolder, dllNameNoExt);

            if (Directory.Exists(targetFolder))
            {
                Modding.Logger.Log($"Мод '{dllNameNoExt}' уже установлен (папка {targetFolder} существует) — пропускаю {fileName}");
                result.Skipped++;
                continue;
            }

            string destPath = Path.Combine(targetFolder, fileName);

            try
            {
                Directory.CreateDirectory(targetFolder);
                File.Copy(sourcePath, destPath, overwrite: false);
                Modding.Logger.Log($"Установлена dll: {fileName} -> {targetFolder}");
                result.Installed++;
            }
            catch (Exception e)
            {
                Modding.Logger.Log($"Не удалось установить {fileName}: {e.Message}");
            }
        }

        return result;
    }

    public static bool HasDllFiles(string extractedArchiveFolder)
    {
        if (!Directory.Exists(extractedArchiveFolder))
            return false;
        return Directory.GetFiles(extractedArchiveFolder, "*.dll", SearchOption.AllDirectories).Length > 0;
    }

    private static Dictionary<string, string> CollectFilesDeduped(string rootFolder, string extension)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string pattern = extension == null ? "*" : $"*{extension}";

        var allFiles = Directory.GetFiles(rootFolder, pattern, SearchOption.AllDirectories);
        foreach (var filePath in allFiles)
        {
            if (IsPathInFolder(filePath, "Backups")) continue;

            string fileName = Path.GetFileName(filePath);
            if (result.ContainsKey(fileName))
            {
                continue;
            }
            result[fileName] = filePath;
        }

        return result;
    }

    public static int UnloadEditors(IEnumerable<MapEditor> editors)
    {
        int cleared = 0;

        foreach (var editor in editors.Distinct())
        {
            string folder = GetEditorFolder(editor);
            if (folder == null || !Directory.Exists(folder))
                continue;

            BackupExistingFiles(folder, $"выключение карты ({EditorConfig.GetLabel(editor)})");
            cleared++;
        }

        return cleared;
    }

    public static bool HasActiveFiles(IEnumerable<MapEditor> editors)
    {
        foreach (var editor in editors.Distinct())
        {
            string folder = GetEditorFolder(editor);
            if (folder == null || !Directory.Exists(folder))
                continue;

            if (Directory.GetFiles(folder).Length > 0)
                return true;

            bool hasNonBackupDir = Directory.GetDirectories(folder)
                .Select(Path.GetFileName)
                .Any(name => !name.StartsWith("GlobalistInstaller_backup_", StringComparison.OrdinalIgnoreCase)
                          && !name.StartsWith("backup_", StringComparison.OrdinalIgnoreCase));

            if (hasNonBackupDir) return true;
        }

        return false;
    }

    private static void BackupExistingFiles(string targetFolder, string label)
    {
        if (!Directory.Exists(targetFolder))
            return;

        var existingFiles = Directory.GetFiles(targetFolder);
        var existingDirs = Directory.GetDirectories(targetFolder);

        var dirsToMove = existingDirs.Where(d => {
            string name = Path.GetFileName(d);
            return !name.StartsWith("GlobalistInstaller_backup_", StringComparison.OrdinalIgnoreCase) &&
                   !name.StartsWith("backup_", StringComparison.OrdinalIgnoreCase);
        }).ToList();

        if (existingFiles.Length == 0 && dirsToMove.Count == 0)
            return;

        string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string backupFolderName = $"GlobalistInstaller_backup_{timestamp}";
        string backupFolderPath = Path.Combine(targetFolder, backupFolderName);

        Directory.CreateDirectory(backupFolderPath);

        int moved = 0;

        foreach (var sourcePath in existingFiles)
        {
            string fileName = Path.GetFileName(sourcePath);
            string backupPath = Path.Combine(backupFolderPath, fileName);
            try
            {
                File.Move(sourcePath, backupPath);
                moved++;
            }
            catch (Exception e)
            {
                Modding.Logger.Log($"Не удалось перенести в бекап файл {fileName}: {e.Message}");
            }
        }

        foreach (var sourceDir in dirsToMove)
        {
            string dirName = Path.GetFileName(sourceDir);
            string backupPath = Path.Combine(backupFolderPath, dirName);
            try
            {
                Directory.Move(sourceDir, backupPath);
                moved++;
            }
            catch (Exception e)
            {
                Modding.Logger.Log($"Не удалось перенести в бекап папку {dirName}: {e.Message}");
            }
        }

        Modding.Logger.Log($"В бекап '{backupFolderName}' перенесено {moved} элементов ({label})");
    }

    public static List<string> ListDllFileNames(string extractedArchiveFolder)
    {
        if (!Directory.Exists(extractedArchiveFolder))
            return new List<string>();

        return CollectFilesDeduped(extractedArchiveFolder, ".dll").Keys.ToList();
    }

    public static bool IsDllModInstalled(string dllFileName)
    {
        string nameNoExt = Path.GetFileNameWithoutExtension(dllFileName);
        string folder = Path.Combine(ManagedModsFolder, nameNoExt);
        return Directory.Exists(folder);
    }
}