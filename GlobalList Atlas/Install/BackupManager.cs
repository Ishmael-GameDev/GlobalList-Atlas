using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using GlobalListAtlas.Configuration;
using GlobalListAtlas.Logging;
using GlobalListAtlas.Maps;

namespace GlobalListAtlas.Install;

// Бекапы, которые MapFileDistributor создаёт в папке редактора перед каждым запуском карты (GlobalistInstaller_backup_дата)
public static class BackupManager
{
    public const string BackupPrefix = "GlobalistInstaller_backup_";
    private const string LegacyPrefix = "backup_";
    private const string TimestampFormat = "yyyy-MM-dd_HH-mm-ss";

    public class BackupInfo
    {
        public MapEditor Editor;
        public string Path;
        public string FolderName;
        public DateTime? Created;
        public int FileCount;
        public long TotalBytes;
    }

    public static bool IsBackupFolderName(string name) =>
        name.StartsWith(BackupPrefix, StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith(LegacyPrefix, StringComparison.OrdinalIgnoreCase);

    // Все бекапы во всех папках редакторов, новые сверху
    public static List<BackupInfo> ListAll()
    {
        var result = new List<BackupInfo>();

        foreach (MapEditor editor in Enum.GetValues(typeof(MapEditor)))
        {
            string folder = MapFileDistributor.GetEditorFolder(editor);
            if (folder == null || !Directory.Exists(folder)) continue;

            foreach (var dir in SafeGetDirectories(folder))
            {
                string name = Path.GetFileName(dir);
                if (!IsBackupFolderName(name)) continue;

                result.Add(Describe(editor, dir, name));
            }
        }

        return result.OrderByDescending(b => b.Created ?? DateTime.MinValue).ToList();
    }

    private static BackupInfo Describe(MapEditor editor, string path, string name)
    {
        var info = new BackupInfo { Editor = editor, Path = path, FolderName = name, Created = ParseTimestamp(name) };

        try
        {
            var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
            info.FileCount = files.Length;
            info.TotalBytes = files.Sum(f => new FileInfo(f).Length);
        }
        catch (Exception e)
        {
            Log.Warn($"Не удалось прочитать бекап {path}: {e.Message}");
        }

        return info;
    }

    private static DateTime? ParseTimestamp(string folderName)
    {
        string raw = folderName.StartsWith(BackupPrefix, StringComparison.OrdinalIgnoreCase)
            ? folderName.Substring(BackupPrefix.Length)
            : folderName.Substring(LegacyPrefix.Length);

        return DateTime.TryParseExact(raw, TimestampFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var dt) ? dt : (DateTime?)null;
    }

    // Возвращает содержимое бекапа обратно в папку редактора
    public static string Restore(BackupInfo backup)
    {
        if (backup == null || !Directory.Exists(backup.Path))
            return "Бекап не найден";

        string editorFolder = Path.GetDirectoryName(backup.Path);
        if (editorFolder == null)
            return "Не удалось определить папку редактора";

        try
        {
            MapFileDistributor.BackupCurrentEditorFiles(editorFolder, "перед восстановлением");

            foreach (var file in Directory.GetFiles(backup.Path, "*", SearchOption.AllDirectories))
            {
                string relative = file.Substring(backup.Path.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string target = Path.Combine(editorFolder, relative);

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: true);
            }

            Directory.Delete(backup.Path, recursive: true);
            Log.Info($"Бекап {backup.FolderName} восстановлен в {editorFolder}");
            return null;
        }
        catch (Exception e)
        {
            Log.Error($"Не удалось восстановить бекап {backup.FolderName}: {e.Message}");
            return e.Message;
        }
    }

    public static string Delete(BackupInfo backup)
    {
        if (backup == null || !Directory.Exists(backup.Path))
            return "Бекап не найден";

        try
        {
            Directory.Delete(backup.Path, recursive: true);
            Log.Info($"Бекап удалён: {backup.FolderName}");
            return null;
        }
        catch (Exception e)
        {
            Log.Error($"Не удалось удалить бекап {backup.FolderName}: {e.Message}");
            return e.Message;
        }
    }

    // Удаляет все бекапы, кроме keepNewest самых свежих в каждой папке редактора
    public static int DeleteOldKeepingNewest(int keepNewest)
    {
        int deleted = 0;

        foreach (var group in ListAll().GroupBy(b => b.Editor))
        {
            foreach (var backup in group.OrderByDescending(b => b.Created ?? DateTime.MinValue).Skip(keepNewest))
            {
                if (Delete(backup) == null) deleted++;
            }
        }

        Log.Info($"Очистка бекапов: удалено {deleted}, оставлено по {keepNewest} свежих на редактор");
        return deleted;
    }

    private static string[] SafeGetDirectories(string path)
    {
        try { return Directory.GetDirectories(path); }
        catch { return Array.Empty<string>(); }
    }
}
