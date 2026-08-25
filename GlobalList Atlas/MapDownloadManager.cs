using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GlobalListAtlas.Archive;
using GlobalListAtlas.Configuration;
using GlobalListAtlas.Drive;
using GlobalListAtlas.Install;
using GlobalListAtlas.Logging;
using GlobalListAtlas.Maps;
using GlobalListAtlas.Sheets;
using GlobalListAtlas.Util;
using UnityEngine;

namespace GlobalListAtlas;

public class MapLaunchOutcome
{
    public bool Success;
    public string ErrorMessage;
    public List<MapEditor> MissingEditors = new();
}

public class MapDownloadManager
{
    private List<MapRow> _cachedAllMaps;
    private bool _isBusy;
    private static readonly string CacheDir = Path.Combine(Application.persistentDataPath, "GlobalListAtlas");
    private static readonly string CacheCsvPath = Path.Combine(CacheDir, "catalog_cache.csv");
    private static readonly string CacheXlsxPath = Path.Combine(CacheDir, "catalog_cache.xlsx");
    public event Action<long, long?> CatalogLoadProgressChanged;

    public async Task RefreshCatalogAsync()
    {
        var settings = GlobalListAtlasMod.Instance?.Settings;
        bool isOffline = settings != null && settings.IsOfflineMode;
        bool autoSave = settings != null && settings.IsAutoSaveCatalog;

        if (isOffline)
        {
            Modding.Logger.Log("Оффлайн-режим: загрузка таблицы из локального кэша...");
            if (!File.Exists(CacheCsvPath) || !File.Exists(CacheXlsxPath))
            {
                throw new Exception("Оффлайн-режим включен, но кэш таблицы отсутствует. Сначала загрузите таблицу в онлайне с включенным автосохранением.");
            }

            string csv = File.ReadAllText(CacheCsvPath);
            byte[] bytes = File.ReadAllBytes(CacheXlsxPath);
            string fingerprint = CsvUtils.GetCellAt(csv, SheetConfig.FirstDataRow, 0);

            var allRows = MapSheetParser.Parse(bytes, SheetConfig.Gid, fingerprint);
            _cachedAllMaps = allRows;
            Modding.Logger.Log($"Каталог успешно загружен из кэша. Карт: {allRows.Count}");
            return;
        }

        Modding.Logger.Log("Обновление каталога карт из таблицы...");
        string csvOnline = await GoogleSheetClient.DownloadSheetCsvAsync(SheetConfig.SheetId, SheetConfig.Gid);
        string fingerprintOnline = CsvUtils.GetCellAt(csvOnline, SheetConfig.FirstDataRow, 0);

        byte[] bytesOnline = await GoogleSheetClient.DownloadWorkbookAsync(SheetConfig.SheetId,
            (received, total) => CatalogLoadProgressChanged?.Invoke(received, total));

        if (autoSave)
        {
            try
            {
                if (!Directory.Exists(CacheDir)) Directory.CreateDirectory(CacheDir);
                File.WriteAllText(CacheCsvPath, csvOnline);
                File.WriteAllBytes(CacheXlsxPath, bytesOnline);
                Modding.Logger.Log("Таблица успешно сохранена в локальный кэш для оффлайн-режима.");
            }
            catch (Exception e)
            {
                Modding.Logger.Log($"Ошибка сохранения кэша таблицы: {e.Message}");
            }
        }

        var allRowsOnline = MapSheetParser.Parse(bytesOnline, SheetConfig.Gid, fingerprintOnline);
        _cachedAllMaps = allRowsOnline;
    }

    public async Task<List<MapRow>> GetAllMapsAsync()
    {
        if (_cachedAllMaps == null)
            await RefreshCatalogAsync();
        return _cachedAllMaps;
    }

    public async void OnDownloadRequested(int selectedIndex)
    {
        if (_isBusy)
        {
            Modding.Logger.Log("Загрузка уже выполняется — дождитесь завершения текущей операции");
            return;
        }

        _isBusy = true;
        try
        {
            if (_cachedAllMaps == null)
                await RefreshCatalogAsync();

            if (!MapCatalog.TryGetByIndex(_cachedAllMaps, selectedIndex, out var map))
            {
                Modding.Logger.Log($"Карта с номером {selectedIndex} недоступна для загрузки");
                return;
            }

            if (string.IsNullOrEmpty(map.DriveUrl))
            {
                Modding.Logger.Log($"У карты '{map.Name}' (строка {map.SheetRowNumber}) нет ссылки в колонке " +
                          $"{SheetConfig.LinkColumn} — нечего скачивать");
                return;
            }

            string editorLabels = map.Editors.Count > 0
                ? string.Join(", ", map.Editors.Select(EditorConfig.GetLabel))
                : "редактор не указан";
            Modding.Logger.Log($"Начинаю установку карты '{map.Name}' ({editorLabels})");

            var archiveBytes = await GoogleDriveDownloader.DownloadAsync(map.DriveUrl);
            if (archiveBytes == null)
            {
                Modding.Logger.Log($"Не удалось скачать архив для карты '{map.Name}'");
                return;
            }

            string targetFolder = Path.Combine(SheetConfig.GetMapsRootFolder(), SanitizeFolderName(map.Name));
            bool extracted = ArchiveExtractor.ExtractToFolder(archiveBytes, targetFolder);

            if (extracted)
            {
                Modding.Logger.Log($"Карта '{map.Name}' установлена: {targetFolder}");

                MapFileDistributor.DistributeMapFiles(targetFolder, map.Editors);
                MapFileDistributor.InstallDlls(targetFolder);
            }
            else
            {
                Modding.Logger.Log($"Установка карты '{map.Name}' завершилась с ошибкой распаковки");
            }
        }
        catch (Exception e)
        {
            Log.Error("Необработанная ошибка при установке карты", e);
        }
        finally
        {
            _isBusy = false;
        }
    }

    private static string SanitizeFolderName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }

    public string GetTargetFolder(MapRow map) =>
        Path.Combine(SheetConfig.GetMapsRootFolder(), SanitizeFolderName(map.Name));

    public bool IsMapDownloaded(MapRow map) =>
        map != null && Directory.Exists(GetTargetFolder(map));

    public bool IsMapLaunched(MapRow map) =>
        map != null && MapFileDistributor.IsMapCurrentlyActive(GetTargetFolder(map), map.Editors);

    public bool CanLaunchMap(MapRow map) =>
        map != null && MapFileDistributor.HasLaunchableTarget(map.Editors);

    public async Task<MapInstallOutcome> DownloadMapFilesAsync(MapRow map)
    {
        var outcome = new MapInstallOutcome();

        if (map == null)
        {
            outcome.ErrorMessage = "Карта не выбрана";
            return outcome;
        }

        outcome.TargetFolder = GetTargetFolder(map);

        if (string.IsNullOrEmpty(map.DriveUrl))
        {
            outcome.ErrorMessage = "У карты нет ссылки на архив (📂) в таблице";
            return outcome;
        }

        var archiveBytes = await GoogleDriveDownloader.DownloadAsync(map.DriveUrl);
        if (archiveBytes == null)
        {
            outcome.ErrorMessage = "Не удалось скачать архив карты";
            return outcome;
        }

        outcome.FileSizeBytes = archiveBytes.Length;

        if (outcome.IsLargeFile)
        {
            float sizeMb = outcome.FileSizeBytes / (1024f * 1024f);
            Modding.Logger.Log($"[Скачивание] Карта '{map.Name}' имеет большой размер: {sizeMb:F2} МБ");
        }

        bool extracted = ArchiveExtractor.ExtractToFolder(archiveBytes, outcome.TargetFolder);
        if (!extracted)
        {
            outcome.ErrorMessage = "Ошибка распаковки архива";
            return outcome;
        }

        var txtScan = TxtFileDetector.Scan(outcome.TargetFolder);
        outcome.TxtFileNames = txtScan.FileNames;
        outcome.HasReadmeNamedTxt = txtScan.HasReadmeNamed;

        Modding.Logger.Log($"[Скачивание] Сканирование редакторов для карты '{map.Name}'...");
        foreach (var editor in map.Editors ?? new List<MapEditor>())
        {
            bool isInstalled = EditorInstallDetector.IsEditorInstalled(editor);
            Modding.Logger.Log($"[Скачивание] Редактор {editor}: {(isInstalled ? "Установлен" : "НЕ УСТАНОВЛЕН")}");
            if (!isInstalled)
                outcome.MissingEditors.Add(editor);
        }

        outcome.Success = true;
        Modding.Logger.Log($"[Панель] Карта '{map.Name}' скачана в папку мода-инсталлятора: {outcome.TargetFolder}");
        return outcome;
    }

    public MapLaunchOutcome LaunchMap(MapRow map)
    {
        var outcome = new MapLaunchOutcome();

        if (map == null)
        {
            outcome.ErrorMessage = "Карта не выбрана";
            return outcome;
        }

        string targetFolder = GetTargetFolder(map);
        if (!Directory.Exists(targetFolder))
        {
            outcome.ErrorMessage = "Карта ещё не скачана — сначала нажмите \"Скачать\"";
            return outcome;
        }

        Modding.Logger.Log($"[Запуск] Сканирование редакторов для карты '{map.Name}'...");
        foreach (var editor in map.Editors ?? new List<MapEditor>())
        {
            bool isInstalled = EditorInstallDetector.IsEditorInstalled(editor);
            Modding.Logger.Log($"[Запуск] Редактор {editor}: {(isInstalled ? "Установлен" : "НЕ УСТАНОВЛЕН")}");
            if (!isInstalled)
                outcome.MissingEditors.Add(editor);
        }

        MapFileDistributor.DistributeMapFiles(targetFolder, map.Editors);
        outcome.Success = true;
        Modding.Logger.Log($"[Панель] Карта '{map.Name}' запущена (файлы разложены по папке редактора)");
        return outcome;
    }

    public DllInstallResult InstallAdditionalMods(MapRow map)
    {
        string targetFolder = GetTargetFolder(map);
        return MapFileDistributor.InstallDlls(targetFolder);
    }

    public async Task<MapInstallOutcome> ReinstallMapAsync(MapRow map)
    {
        var outcome = new MapInstallOutcome();

        if (map == null)
        {
            outcome.ErrorMessage = "Карта не выбрана";
            return outcome;
        }

        string targetFolder = GetTargetFolder(map);

        try
        {
            if (Directory.Exists(targetFolder))
                Directory.Delete(targetFolder, recursive: true);
        }
        catch (Exception e)
        {
            outcome.ErrorMessage = $"Не удалось удалить старые файлы карты: {e.Message}";
            return outcome;
        }

        return await DownloadMapFilesAsync(map);
    }
    public async Task<MapInstallOutcome> DownloadMapFilesAsync(MapRow map, Action<long> onSizeRetrieved = null)
    {
        var outcome = new MapInstallOutcome();

        if (map == null)
        {
            outcome.ErrorMessage = "Карта не выбрана";
            return outcome;
        }

        outcome.TargetFolder = GetTargetFolder(map);

        if (string.IsNullOrEmpty(map.DriveUrl))
        {
            outcome.ErrorMessage = "У карты нет ссылки на архив (📂) в таблице";
            return outcome;
        }

        var archiveBytes = await GoogleDriveDownloader.DownloadAsync(map.DriveUrl, onSizeRetrieved);
        if (archiveBytes == null)
        {
            outcome.ErrorMessage = "Не удалось скачать архив карты";
            return outcome;
        }

        outcome.FileSizeBytes = archiveBytes.Length;

        bool extracted = ArchiveExtractor.ExtractToFolder(archiveBytes, outcome.TargetFolder);
        if (!extracted)
        {
            outcome.ErrorMessage = "Ошибка распаковки архива";
            return outcome;
        }

        var txtScan = TxtFileDetector.Scan(outcome.TargetFolder);
        outcome.TxtFileNames = txtScan.FileNames;
        outcome.HasReadmeNamedTxt = txtScan.HasReadmeNamed;

        foreach (var editor in map.Editors ?? new List<MapEditor>())
        {
            if (!EditorInstallDetector.IsEditorInstalled(editor))
                outcome.MissingEditors.Add(editor);
        }

        outcome.Success = true;
        Modding.Logger.Log($"[Панель] Карта '{map.Name}' скачана в папку мода-инсталлятора: {outcome.TargetFolder}");
        return outcome;
    }

    public async Task<MapInstallOutcome> ReinstallMapAsync(MapRow map, Action<long> onSizeRetrieved = null)
    {
        var outcome = new MapInstallOutcome();
        if (map == null)
        {
            outcome.ErrorMessage = "Карта не выбрана";
            return outcome;
        }

        string key = DownloadKey(map);
        if (_activeDownloads.ContainsKey(key))
        {
            outcome.ErrorMessage = "Операция с этой картой уже выполняется";
            return outcome;
        }

        string targetFolder = GetTargetFolder(map);
        string tempFolder = targetFolder + "_reinstall_tmp";

        if (Directory.Exists(tempFolder))
        {
            try { Directory.Delete(tempFolder, true); } catch { }
        }

        if (string.IsNullOrEmpty(map.DriveUrl))
        {
            outcome.ErrorMessage = "У карты нет ссылки на архив (📂) в таблице";
            return outcome;
        }

        var progress = new MapDownloadProgress { Cts = new CancellationTokenSource() };
        _activeDownloads[key] = progress;

        try
        {
            byte[] archiveBytes;
            try
            {
                archiveBytes = await GoogleDriveDownloader.DownloadAsync(
                    map.DriveUrl,
                    onSizeRetrieved: size =>
                    {
                        progress.TotalBytes = size;
                        onSizeRetrieved?.Invoke(size);
                        MapDownloadProgressChanged?.Invoke(map, progress.BytesReceived, progress.TotalBytes);
                    },
                    onProgress: (received, total) =>
                    {
                        progress.BytesReceived = received;
                        progress.TotalBytes = total;
                        MapDownloadProgressChanged?.Invoke(map, received, total);
                    },
                    cancellationToken: progress.Cts.Token);
            }
            catch (OperationCanceledException)
            {
                outcome.ErrorMessage = "Скачивание отменено";
                return outcome;
            }

            if (archiveBytes == null)
            {
                outcome.ErrorMessage = "Не удалось скачать архив карты";
                return outcome;
            }

            outcome.FileSizeBytes = archiveBytes.Length;

            bool extracted = ArchiveExtractor.ExtractToFolder(archiveBytes, tempFolder);
            if (!extracted)
            {
                outcome.ErrorMessage = "Ошибка распаковки архива";
                return outcome;
            }

            try
            {
                if (Directory.Exists(targetFolder))
                    Directory.Delete(targetFolder, true);

                Directory.Move(tempFolder, targetFolder);
            }
            catch (Exception e)
            {
                outcome.ErrorMessage = $"Не удалось заменить старые файлы карты: {e.Message}";
                return outcome;
            }

            var txtScan = TxtFileDetector.Scan(targetFolder);
            outcome.TargetFolder = targetFolder;
            outcome.TxtFileNames = txtScan.FileNames;
            outcome.HasReadmeNamedTxt = txtScan.HasReadmeNamed;

            foreach (var editor in map.Editors ?? new List<MapEditor>())
            {
                if (!EditorInstallDetector.IsEditorInstalled(editor))
                    outcome.MissingEditors.Add(editor);
            }

            outcome.Success = true;
            Modding.Logger.Log($"[Панель] Карта '{map.Name}' успешно переустановлена: {targetFolder}");
            return outcome;
        }
        finally
        {
            _activeDownloads.TryRemove(key, out _);
            if (Directory.Exists(tempFolder))
            {
                try { Directory.Delete(tempFolder, true); } catch { }
            }
        }
    }
    public class MapDownloadProgress
    {
        public long BytesReceived;
        public long? TotalBytes;
        public CancellationTokenSource Cts;
    }

    private readonly ConcurrentDictionary<string, MapDownloadProgress> _activeDownloads = new();

    public event Action<MapRow, long, long?> MapDownloadProgressChanged;

    private static string DownloadKey(MapRow map) => map.Name;

    public bool IsMapDownloading(MapRow map) => map != null && _activeDownloads.ContainsKey(DownloadKey(map));

    public (long received, long? total) GetMapDownloadProgress(MapRow map)
    {
        if (map != null && _activeDownloads.TryGetValue(DownloadKey(map), out var p))
            return (p.BytesReceived, p.TotalBytes);
        return (0, null);
    }
    public void CancelMapDownload(MapRow map)
    {
        if (map != null && _activeDownloads.TryGetValue(DownloadKey(map), out var p))
            p.Cts.Cancel();
    }

    public async Task<MapInstallOutcome> DownloadMapFilesTrackedAsync(MapRow map, Action<long> onSizeRetrieved = null)
    {
        var outcome = new MapInstallOutcome();

        if (map == null)
        {
            outcome.ErrorMessage = "Карта не выбрана";
            return outcome;
        }

        string key = DownloadKey(map);
        if (_activeDownloads.ContainsKey(key))
        {
            outcome.ErrorMessage = "Загрузка этой карты уже выполняется";
            return outcome;
        }

        outcome.TargetFolder = GetTargetFolder(map);

        if (string.IsNullOrEmpty(map.DriveUrl))
        {
            outcome.ErrorMessage = "У карты нет ссылки на архив (📂) в таблице";
            return outcome;
        }

        var progress = new MapDownloadProgress { Cts = new CancellationTokenSource() };
        _activeDownloads[key] = progress;

        try
        {
            byte[] archiveBytes;
            try
            {
                archiveBytes = await GoogleDriveDownloader.DownloadAsync(
                    map.DriveUrl,
                    onSizeRetrieved: size =>
                    {
                        progress.TotalBytes = size;
                        onSizeRetrieved?.Invoke(size);
                        MapDownloadProgressChanged?.Invoke(map, progress.BytesReceived, progress.TotalBytes);
                    },
                    onProgress: (received, total) =>
                    {
                        progress.BytesReceived = received;
                        progress.TotalBytes = total;
                        MapDownloadProgressChanged?.Invoke(map, received, total);
                    },
                    cancellationToken: progress.Cts.Token);
            }
            catch (OperationCanceledException)
            {
                outcome.ErrorMessage = "Скачивание отменено";
                return outcome;
            }

            if (archiveBytes == null)
            {
                outcome.ErrorMessage = "Не удалось скачать архив карты";
                return outcome;
            }

            outcome.FileSizeBytes = archiveBytes.Length;

            bool extracted = ArchiveExtractor.ExtractToFolder(archiveBytes, outcome.TargetFolder);
            if (!extracted)
            {
                outcome.ErrorMessage = "Ошибка распаковки архива";
                return outcome;
            }

            var txtScan = TxtFileDetector.Scan(outcome.TargetFolder);
            outcome.TxtFileNames = txtScan.FileNames;
            outcome.HasReadmeNamedTxt = txtScan.HasReadmeNamed;

            foreach (var editor in map.Editors ?? new List<MapEditor>())
            {
                if (!EditorInstallDetector.IsEditorInstalled(editor))
                    outcome.MissingEditors.Add(editor);
            }

            outcome.Success = true;
            Modding.Logger.Log($"[Панель] Карта '{map.Name}' скачана в папку мода-инсталлятора: {outcome.TargetFolder}");
            return outcome;
        }
        finally
        {
            _activeDownloads.TryRemove(key, out _);
        }
    }

}