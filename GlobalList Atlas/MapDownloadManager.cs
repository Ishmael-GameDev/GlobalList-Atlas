using System;
using Newtonsoft.Json;
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
    private List<MapRow> _cachedEventMaps;
    private List<MapRow> _cachedServerMaps;
    private bool _useSavedServerCatalog;
    private CancellationTokenSource _serverCts = new();

    public MapCatalogKind CurrentCatalog { get; private set; } = MapCatalogKind.GlobalList;

    public event Action<MapCatalogKind> CatalogChanged;

    private static readonly string ServerCachePath =
        Path.Combine(Application.persistentDataPath, "GlobalListAtlas", "architect_server_cache.json");

    private static readonly string EventCacheXlsxPath =
        Path.Combine(Application.persistentDataPath, "GlobalListAtlas", "event_catalog_cache.xlsx");
    private bool _isBusy;
    private static readonly string CacheDir = Path.Combine(Application.persistentDataPath, "GlobalListAtlas");
    private static readonly string CacheCsvPath = Path.Combine(CacheDir, "catalog_cache.csv");
    private static readonly string CacheXlsxPath = Path.Combine(CacheDir, "catalog_cache.xlsx");
    public event Action<MapCatalogKind, long, long?> CatalogLoadProgressChanged;

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
            (received, total) => CatalogLoadProgressChanged?.Invoke(MapCatalogKind.GlobalList, received, total));

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
        if (CurrentCatalog == MapCatalogKind.EventCommunity)
        {
            if (_cachedEventMaps == null)
                await RefreshEventCatalogAsync();
            return _cachedEventMaps;
        }

        if (CurrentCatalog == MapCatalogKind.ArchitectServer)
        {
            if (_cachedServerMaps == null)
                await RefreshServerCatalogAsync();
            return _cachedServerMaps;
        }

        if (_cachedAllMaps == null)
            await RefreshCatalogAsync();
        return _cachedAllMaps;
    }

    public bool ServerUnreachable { get; private set; }

    public bool HasSavedServerCatalog => File.Exists(ServerCachePath);

    public void InvalidateServerCatalog() => _cachedServerMaps = null;

    public void CancelServerFetches()
    {
        _serverCts.Cancel();
        _serverCts = new CancellationTokenSource();
    }

    public void LoadSavedServerCatalog()
    {
        CancelServerFetches();
        _cachedServerMaps = null;
        _useSavedServerCatalog = true;
    }

    public void SetCatalog(MapCatalogKind catalog)
    {
        if (CurrentCatalog == catalog) return;

        CurrentCatalog = catalog;
        Maps.ActiveMapResolver.Invalidate();
        Log.Info($"Открыт каталог: {catalog}");
        CatalogChanged?.Invoke(catalog);
    }

    public async Task RefreshServerCatalogAsync()
    {
        var settings = GlobalListAtlasMod.Instance?.Settings;
        bool isOffline = settings != null && settings.IsOfflineMode;
        bool autoSave = settings != null && settings.IsAutoSaveCatalog;

        Dictionary<ArchitectSource, List<Dictionary<string, string>>> raw;

        if (isOffline || _useSavedServerCatalog)
        {
            _useSavedServerCatalog = false;
            if (!File.Exists(ServerCachePath))
                throw new Exception("Оффлайн-режим включен, но кэш каталога Architect отсутствует.");
            raw = await Task.Run(() => JsonConvert.DeserializeObject<Dictionary<ArchitectSource, List<Dictionary<string, string>>>>(
                File.ReadAllText(ServerCachePath)));
        }
        else
        {
            Log.Info("Обновление каталога сервера Architect...");
            Server.ArchitectLoadStatus.Begin();
            raw = new Dictionary<ArchitectSource, List<Dictionary<string, string>>>();
            var errors = new List<string>();

            var fetchToken = _serverCts.Token;
            var sources = (ArchitectSource[])Enum.GetValues(typeof(ArchitectSource));
            var fetchTasks = sources.Select(async source =>
            {
                try
                {
                    var maps = await Server.ArchitectServerClient.FetchRawAsync(source, fetchToken);
                    Server.ArchitectLoadStatus.Update(source, p =>
                    {
                        p.State = Server.ArchitectSourceState.Done;
                        p.Maps = maps.Count;
                        p.PagesDone = p.PagesTotal;
                    });
                    return (source, maps, error: (string)null, unreachable: false);
                }
                catch (OperationCanceledException)
                {
                    return (source, maps: (List<Dictionary<string, string>>)null, error: "отменено", unreachable: false);
                }
                catch (Exception e)
                {
                    Log.Error($"[ArchitectServer] Не удалось загрузить {source}: {e.Message}");
                    Server.ArchitectLoadStatus.Update(source, p =>
                    {
                        p.State = Server.ArchitectSourceState.Failed;
                        p.Error = e.Message;
                    });
                    return (source, maps: (List<Dictionary<string, string>>)null, error: $"{source}: {e.Message}",
                        unreachable: e is Server.ArchitectUnreachableException);
                }
            }).ToList();

            var results = await Task.WhenAll(fetchTasks);
            foreach (var result in results)
            {
                if (result.error != null) errors.Add(result.error);
                else raw[result.source] = result.maps;
            }
            Server.ArchitectLoadStatus.Finish();
            if (fetchToken.IsCancellationRequested) throw new OperationCanceledException();

            if (raw.Count == 0)
            {
                ServerUnreachable = results.All(r => r.unreachable);
                throw new Exception("Сервер Architect недоступен: " + string.Join("; ", errors));
            }

            if (autoSave && errors.Count == 0)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(ServerCachePath)!);
                    File.WriteAllText(ServerCachePath, JsonConvert.SerializeObject(raw));
                }
                catch (Exception e)
                {
                    Log.Warn($"Не удалось сохранить кэш каталога Architect: {e.Message}");
                }
            }
        }

        _cachedServerMaps = raw
            .SelectMany(kvp => (kvp.Value ?? new List<Dictionary<string, string>>())
                .Select((r, i) => Server.ArchitectServerClient.ToMapRow(r, kvp.Key, i)))
            .Where(m => !string.IsNullOrEmpty(m.ServerLevelId))
            .ToList();

        int undated = _cachedServerMaps.Count(m => m.Uploaded == null);
        Log.Info($"[ArchitectServer] Каталог: {_cachedServerMaps.Count} карт, без даты загрузки: {undated}");
    }

    public async Task RefreshEventCatalogAsync()
    {
        var settings = GlobalListAtlasMod.Instance?.Settings;
        bool isOffline = settings != null && settings.IsOfflineMode;
        bool autoSave = settings != null && settings.IsAutoSaveCatalog;

        byte[] bytes;
        if (isOffline)
        {
            if (!File.Exists(EventCacheXlsxPath))
                throw new Exception("Оффлайн-режим включен, но кэш таблицы ивентов отсутствует. Сначала загрузите её в онлайне с включенным автосохранением.");

            bytes = File.ReadAllBytes(EventCacheXlsxPath);
        }
        else
        {
            Log.Info("Обновление каталога ивентов из таблицы...");
            bytes = await GoogleSheetClient.DownloadWorkbookAsync(EventCatalogConfig.SheetId,
                (received, total) => CatalogLoadProgressChanged?.Invoke(MapCatalogKind.EventCommunity, received, total));

            if (autoSave)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(EventCacheXlsxPath)!);
                    File.WriteAllBytes(EventCacheXlsxPath, bytes);
                }
                catch (Exception e)
                {
                    Log.Warn($"Не удалось сохранить кэш таблицы ивентов: {e.Message}");
                }
            }
        }

        _cachedEventMaps = EventSheetParser.Parse(bytes);
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

    private async Task<MapInstallOutcome> DownloadServerMapAsync(MapRow map, MapInstallOutcome outcome, MapDownloadProgress progress)
    {
        string lastError = null;
        for (int attempt = 1; attempt <= MaxDownloadAttempts; attempt++)
        {
            if (progress.Cts.IsCancellationRequested)
            {
                outcome.ErrorMessage = "Скачивание отменено";
                return outcome;
            }

            try
            {
                if (Directory.Exists(outcome.TargetFolder)) Directory.Delete(outcome.TargetFolder, recursive: true);

                int scenes = await Server.ArchitectServerClient.DownloadLevelAsync(map, outcome.TargetFolder);
                if (scenes == 0) throw new InvalidDataException("в карте нет ни одной сцены");

                Log.Info($"[ArchitectServer] Карта '{map.Name}' ({map.ServerSource}) скачана: сцен {scenes}");
                lastError = null;
                break;
            }
            catch (Exception e)
            {
                lastError = e.Message;
                Log.Warn($"[ArchitectServer] Скачивание '{map.Name}' сорвалось (попытка {attempt}/{MaxDownloadAttempts}): {e.Message}");
                if (attempt < MaxDownloadAttempts) await Task.Delay(TimeSpan.FromSeconds(2 * attempt));
            }
        }

        if (lastError != null)
        {
            outcome.ErrorMessage = $"Не удалось скачать карту с сервера Architect: {lastError}";
            return outcome;
        }

        foreach (var editor in map.Editors ?? new List<MapEditor>())
        {
            if (!EditorInstallDetector.IsEditorInstalled(editor))
                outcome.MissingEditors.Add(editor);
        }

        outcome.Success = true;
        Maps.ActiveMapResolver.Invalidate();
        return outcome;
    }

    private static readonly byte[] ZipMagic = { 0x50, 0x4B, 0x03, 0x04 };
    private static readonly byte[] RarMagic = { 0x52, 0x61, 0x72, 0x21 };
    private static readonly byte[] SevenZipMagic = { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C };

    private static bool StoreDownloadedFiles(MapRow map, byte[] bytes, string targetFolder)
    {
        if (IsArchive(bytes))
            return ArchiveExtractor.ExtractToFolder(bytes, targetFolder);

        if (!LooksLikeJson(bytes))
        {
            Log.Error($"Файл карты '{map?.Name}' не похож ни на архив, ни на json");
            return false;
        }

        try
        {
            string fileName = !string.IsNullOrWhiteSpace(map?.SourceFileName) &&
                              map.SourceFileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? map.SourceFileName
                : SanitizeFolderName(map?.Name ?? "map") + ".json";

            bool newArchitect = map?.Editors != null && map.Editors.Contains(MapEditor.NewArchitect);
            string folder = newArchitect ? Path.Combine(targetFolder, "Scenes") : targetFolder;

            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, fileName), bytes);
            Log.Info($"Однокомнатная карта '{map?.Name}' сохранена: {Path.Combine(folder, fileName)}");
            return true;
        }
        catch (Exception e)
        {
            Log.Error($"Не удалось сохранить файл карты '{map?.Name}': {e.Message}");
            return false;
        }
    }

    private static bool IsArchive(byte[] bytes) =>
        StartsWith(bytes, ZipMagic) || StartsWith(bytes, RarMagic) || StartsWith(bytes, SevenZipMagic);

    private static bool StartsWith(byte[] bytes, byte[] magic)
    {
        if (bytes == null || bytes.Length < magic.Length) return false;
        for (int i = 0; i < magic.Length; i++)
            if (bytes[i] != magic[i]) return false;
        return true;
    }

    private static bool LooksLikeJson(byte[] bytes)
    {
        if (bytes == null) return false;
        int i = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        for (; i < bytes.Length && i < 64; i++)
        {
            byte b = bytes[i];
            if (b == (byte)' ' || b == (byte)'\t' || b == (byte)'\r' || b == (byte)'\n') continue;
            return b == (byte)'{' || b == (byte)'[';
        }
        return false;
    }

    private static string SanitizeFolderName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }

    public string GetTargetFolder(MapRow map) => map.Catalog switch
    {
        MapCatalogKind.EventCommunity =>
            Path.Combine(SheetConfig.GetMapsRootFolder(), "EventCommunity", SanitizeFolderName(map.Name)),
        MapCatalogKind.ArchitectServer =>
            Path.Combine(SheetConfig.GetMapsRootFolder(), "ArchitectServer", map.ServerSource.ToString(),
                SanitizeFolderName($"{map.ServerLevelId}_{map.Name}")),
        _ => Path.Combine(SheetConfig.GetMapsRootFolder(), SanitizeFolderName(map.Name))
    };

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

        bool extracted = StoreDownloadedFiles(map, archiveBytes, outcome.TargetFolder);
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

    public MapRow FindCachedMap(MapCatalogKind catalog, string name)
    {
        var maps = CatalogMaps(catalog);
        return maps?.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.Ordinal));
    }

    public MapRow FindCachedMapByFolder(MapCatalogKind catalog, string folderName)
    {
        var maps = CatalogMaps(catalog);
        return maps?.FirstOrDefault(m =>
            string.Equals(Path.GetFileName(GetTargetFolder(m)), folderName, StringComparison.OrdinalIgnoreCase));
    }

    public List<MapRow> CachedMaps => CatalogMaps(CurrentCatalog);

    private List<MapRow> CatalogMaps(MapCatalogKind catalog) => catalog switch
    {
        MapCatalogKind.EventCommunity => _cachedEventMaps,
        MapCatalogKind.ArchitectServer => _cachedServerMaps,
        _ => _cachedAllMaps
    };

    private readonly Dictionary<string, long> _archiveSizes = new();

    public bool TryGetArchiveSize(MapRow map, out long bytes)
    {
        bytes = 0;
        return map != null && map.DriveUrl != null && _archiveSizes.TryGetValue(map.DriveUrl, out bytes);
    }

    public async Task<bool> FetchArchiveSizeAsync(MapRow map)
    {
        if (map == null || string.IsNullOrEmpty(map.DriveUrl))
            return false;

        if (_archiveSizes.ContainsKey(map.DriveUrl))
            return true;

        long? size = await FetchDriveFileSizeAsync(map.DriveUrl);
        if (size == null || size.Value <= 0)
            return false;

        _archiveSizes[map.DriveUrl] = size.Value;
        return true;
    }

    private static async Task<long?> FetchDriveFileSizeAsync(string driveUrl)
    {
        string fileId = GoogleDriveDownloader.ExtractFileId(driveUrl);
        if (fileId == null) return null;

        try
        {
            using var handler = new System.Net.Http.HttpClientHandler
            {
                CookieContainer = new System.Net.CookieContainer(),
                AllowAutoRedirect = true
            };
            using var client = new System.Net.Http.HttpClient(handler);
            client.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

            using var response = await client.GetAsync($"https://drive.google.com/uc?export=download&id={fileId}",
                System.Net.Http.HttpCompletionOption.ResponseHeadersRead);

            if ((response.Content.Headers.ContentType?.MediaType ?? "").Contains("text/html"))
            {
                string html = await response.Content.ReadAsStringAsync();
                string confirmUrl = BuildConfirmUrl(html, fileId);
                using var confirmed = await client.GetAsync(confirmUrl, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
                return confirmed.IsSuccessStatusCode ? confirmed.Content.Headers.ContentLength : null;
            }

            return response.IsSuccessStatusCode ? response.Content.Headers.ContentLength : null;
        }
        catch (Exception e)
        {
            Log.Warn($"Не удалось узнать размер файла Drive id={fileId}: {e.Message}");
            return null;
        }
    }

    private static string BuildConfirmUrl(string html, string fileId)
    {
        var formMatch = System.Text.RegularExpressions.Regex.Match(
            html, "<form[^>]*action=[\"']([^\"']+)[\"']",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        string actionUrl = formMatch.Success
            ? System.Net.WebUtility.HtmlDecode(formMatch.Groups[1].Value)
            : "https://drive.usercontent.google.com/download";

        var parameters = new Dictionary<string, string>
        {
            ["id"] = fileId,
            ["export"] = "download",
            ["confirm"] = "t"
        };

        foreach (System.Text.RegularExpressions.Match input in System.Text.RegularExpressions.Regex.Matches(
                     html, "<input[^>]+>", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            var name = System.Text.RegularExpressions.Regex.Match(input.Value, "name=[\"']([^\"']+)[\"']",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!name.Success) continue;

            var value = System.Text.RegularExpressions.Regex.Match(input.Value, "value=[\"']([^\"']*)[\"']",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            parameters[System.Net.WebUtility.HtmlDecode(name.Groups[1].Value)] =
                value.Success ? System.Net.WebUtility.HtmlDecode(value.Groups[1].Value) : "";
        }

        string query = string.Join("&", parameters.Select(kvp =>
            $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));

        return actionUrl + (actionUrl.Contains("?") ? "&" : "?") + query;
    }

    public string DeleteMapFiles(MapRow map)
    {
        if (map == null) return "Карта не выбрана";

        string folder = GetTargetFolder(map);
        if (!Directory.Exists(folder))
            return "Файлы карты не найдены";

        try
        {
            if (MapFileDistributor.IsMapCurrentlyActive(folder, map.Editors))
                MapFileDistributor.RemoveMapFilesFromEditors(folder, map.Editors);

            Directory.Delete(folder, recursive: true);
            if (map.DriveUrl != null) _archiveSizes.Remove(map.DriveUrl);
            Maps.ActiveMapResolver.Invalidate();
            Log.Info($"Файлы карты '{map.Name}' удалены: {folder}");
            return null;
        }
        catch (Exception e)
        {
            Log.Error($"Не удалось удалить файлы карты '{map.Name}': {e.Message}");
            return e.Message;
        }
    }

    public string UnloadMap(MapRow map)
    {
        var editors = map?.Editors != null && map.Editors.Count > 0
            ? map.Editors
            : EditorModRegistry.Entries.Select(e => e.Editor).ToList();

        if (!MapFileDistributor.HasActiveFiles(editors))
            return "NOTHING";

        try
        {
            int cleared = MapFileDistributor.UnloadEditors(editors);
            Maps.ActiveMapResolver.Invalidate();
            Log.Info($"Карта выключена, очищено папок редакторов: {cleared}");
            return null;
        }
        catch (Exception e)
        {
            Log.Error($"Не удалось выключить карту: {e.Message}");
            return e.Message;
        }
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

        bool extracted = StoreDownloadedFiles(map, archiveBytes, outcome.TargetFolder);
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
        if (map?.Catalog == MapCatalogKind.ArchitectServer)
        {
            try
            {
                string folder = GetTargetFolder(map);
                if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            }
            catch (Exception e)
            {
                return new MapInstallOutcome { ErrorMessage = $"Не удалось удалить старые файлы карты: {e.Message}" };
            }
            return await DownloadMapFilesTrackedAsync(map);
        }
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
            byte[] archiveBytes = null;
            string lastError = null;

            for (int attempt = 1; ; attempt++)
            {
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
                catch (OperationCanceledException) when (progress.Cts.IsCancellationRequested)
                {
                    outcome.ErrorMessage = "Скачивание отменено";
                    return outcome;
                }
                catch (Exception e)
                {
                    lastError = e is OperationCanceledException ? "превышено время ожидания" : e.Message;
                    archiveBytes = null;
                }

                if (archiveBytes != null) break;

                if (attempt >= MaxDownloadAttempts)
                {
                    outcome.ErrorMessage = lastError == null
                        ? $"Не удалось скачать архив карты ({MaxDownloadAttempts} попытки)"
                        : $"Не удалось скачать архив карты ({MaxDownloadAttempts} попытки): {lastError}";
                    return outcome;
                }

                Log.Warn($"Скачивание '{map.Name}' сорвалось (попытка {attempt}/{MaxDownloadAttempts}), повтор...");
                progress.BytesReceived = 0;
                MapDownloadProgressChanged?.Invoke(map, 0, progress.TotalBytes);

                try { await Task.Delay(TimeSpan.FromSeconds(2 * attempt), progress.Cts.Token); }
                catch (OperationCanceledException)
                {
                    outcome.ErrorMessage = "Скачивание отменено";
                    return outcome;
                }
            }

            outcome.FileSizeBytes = archiveBytes.Length;

            bool extracted = StoreDownloadedFiles(map, archiveBytes, tempFolder);
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

            if (map.Presets != null && map.Presets.Count > 0)
            {
                bool presetWasActive = Integrations.PresetManager.IsAnyActive(map);
                _presetResults[DownloadKey(map)] = await Integrations.PresetManager.DownloadPresetsAsync(map);
                if (presetWasActive) Integrations.PresetManager.ActivatePresets(map);
            }

            outcome.Success = true;
            Modding.Logger.Log($"[Панель] Карта '{map.Name}' успешно переустановлена: {targetFolder}");
            return outcome;
        }
        finally
        {
            if (outcome.Success || outcome.ErrorMessage == "Скачивание отменено")
                _lastDownloadErrors.Remove(key);
            else if (!string.IsNullOrEmpty(outcome.ErrorMessage))
                _lastDownloadErrors[key] = outcome.ErrorMessage;

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

    private const int MaxDownloadAttempts = 3;

    private static string DownloadKey(MapRow map) =>
        map.Catalog == MapCatalogKind.ArchitectServer ? $"{map.Catalog}|{map.ServerLevelId}" : $"{map.Catalog}|{map.Name}";

    private readonly Dictionary<string, string> _lastDownloadErrors = new();

    private readonly Dictionary<string, Integrations.PresetDownloadResult> _presetResults = new();

    public Integrations.PresetDownloadResult TakePresetResult(MapRow map)
    {
        if (map == null || !_presetResults.TryGetValue(DownloadKey(map), out var result)) return null;
        _presetResults.Remove(DownloadKey(map));
        return result;
    }

    public bool TryGetLastDownloadError(MapRow map, out string error)
    {
        error = null;
        return map != null && _lastDownloadErrors.TryGetValue(DownloadKey(map), out error);
    }

    public void ClearLastDownloadError(MapRow map)
    {
        if (map != null) _lastDownloadErrors.Remove(DownloadKey(map));
    }

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

        if (map.Catalog != MapCatalogKind.ArchitectServer && string.IsNullOrEmpty(map.DriveUrl))
        {
            outcome.ErrorMessage = "У карты нет ссылки на архив (📂) в таблице";
            return outcome;
        }

        var progress = new MapDownloadProgress { Cts = new CancellationTokenSource() };
        _activeDownloads[key] = progress;

        try
        {
            if (map.Catalog == MapCatalogKind.ArchitectServer)
                return await DownloadServerMapAsync(map, outcome, progress);

            byte[] archiveBytes = null;
            string lastError = null;

            for (int attempt = 1; ; attempt++)
            {
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
                catch (OperationCanceledException) when (progress.Cts.IsCancellationRequested)
                {
                    outcome.ErrorMessage = "Скачивание отменено";
                    return outcome;
                }
                catch (Exception e)
                {
                    lastError = e is OperationCanceledException ? "превышено время ожидания" : e.Message;
                    archiveBytes = null;
                }

                if (archiveBytes != null) break;

                if (attempt >= MaxDownloadAttempts)
                {
                    outcome.ErrorMessage = lastError == null
                        ? $"Не удалось скачать архив карты ({MaxDownloadAttempts} попытки)"
                        : $"Не удалось скачать архив карты ({MaxDownloadAttempts} попытки): {lastError}";
                    return outcome;
                }

                Log.Warn($"Скачивание '{map.Name}' сорвалось (попытка {attempt}/{MaxDownloadAttempts}), повтор...");
                progress.BytesReceived = 0;
                MapDownloadProgressChanged?.Invoke(map, 0, progress.TotalBytes);

                try { await Task.Delay(TimeSpan.FromSeconds(2 * attempt), progress.Cts.Token); }
                catch (OperationCanceledException)
                {
                    outcome.ErrorMessage = "Скачивание отменено";
                    return outcome;
                }
            }

            outcome.FileSizeBytes = archiveBytes.Length;

            bool extracted = StoreDownloadedFiles(map, archiveBytes, outcome.TargetFolder);
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

            if (map.Presets != null && map.Presets.Count > 0)
                _presetResults[DownloadKey(map)] = await Integrations.PresetManager.DownloadPresetsAsync(map);

            outcome.Success = true;
            Modding.Logger.Log($"[Панель] Карта '{map.Name}' скачана в папку мода-инсталлятора: {outcome.TargetFolder}");
            return outcome;
        }
        finally
        {
            if (outcome.Success || outcome.ErrorMessage == "Скачивание отменено")
                _lastDownloadErrors.Remove(key);
            else if (!string.IsNullOrEmpty(outcome.ErrorMessage))
                _lastDownloadErrors[key] = outcome.ErrorMessage;

            _activeDownloads.TryRemove(key, out _);
        }
    }

}