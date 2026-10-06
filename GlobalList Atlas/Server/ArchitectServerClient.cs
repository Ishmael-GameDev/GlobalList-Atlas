using System;
using System.IO;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GlobalListAtlas.Configuration;
using GlobalListAtlas.Logging;
using GlobalListAtlas.Maps;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GlobalListAtlas.Server;

public static class ArchitectServerClient
{
    private static readonly HttpClient Http = new(new HttpClientHandler
    {
        AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
    }) { Timeout = TimeSpan.FromSeconds(60) };

    private static Task<HttpResponseMessage> PostAsync(string url, HttpContent content, CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        request.Headers.ExpectContinue = false;
        return Http.SendAsync(request, ct);
    }

    public static async Task<List<Dictionary<string, string>>> FetchRawAsync(ArchitectSource source, CancellationToken ct = default)
    {
        return source == ArchitectSource.LegacyArchitect
            ? await FetchLegacyAsync(ct)
            : await FetchModernAllPagesAsync(source, source == ArchitectSource.Silksong
                ? ArchitectServerConfig.SilksongGame
                : ArchitectServerConfig.NewArchitectGame, ct);
    }

    private const int PageConcurrency = 6;

    private static async Task<List<Dictionary<string, string>>> FetchModernAllPagesAsync(ArchitectSource source, string game, CancellationToken ct)
    {
        var (firstPageMaps, totalPages) = await SearchModernPageAsync(source, game, 0, ct);
        Log.Info($"[ArchitectServer] {game}: страниц {totalPages}, карт на странице {firstPageMaps.Count}");

        var all = new List<Dictionary<string, string>>(firstPageMaps);
        int pageCount = Math.Min(totalPages, ArchitectServerConfig.MaxPages);
        ArchitectLoadStatus.Update(source, p => { p.PagesDone = 1; p.PagesTotal = Math.Max(pageCount, 1); p.Maps = all.Count; });
        if (firstPageMaps.Count == 0 || pageCount <= 1)
        {
            Log.Info($"[ArchitectServer] {game}: всего карт {all.Count}");
            return all;
        }

        var pageResults = new Dictionary<int, List<Dictionary<string, string>>>();
        using var gate = new SemaphoreSlim(PageConcurrency);

        async Task FetchPageAsync(int page)
        {
            await gate.WaitAsync();
            try
            {
                var (pageMaps, _) = await SearchModernPageAsync(source, game, page, ct);
                lock (pageResults) pageResults[page] = pageMaps;
                ArchitectLoadStatus.Update(source, p => { p.PagesDone++; p.Maps += pageMaps.Count; });
            }
            finally { gate.Release(); }
        }

        var tasks = new List<Task>();
        for (int page = 1; page < pageCount; page++)
            tasks.Add(FetchPageAsync(page));
        await Task.WhenAll(tasks);

        for (int page = 1; page < pageCount; page++)
            if (pageResults.TryGetValue(page, out var pageMaps))
                all.AddRange(pageMaps);

        Log.Info($"[ArchitectServer] {game}: всего карт {all.Count}");
        return all;
    }

    private static async Task<(List<Dictionary<string, string>> Maps, int Pages)> SearchModernPageAsync(ArchitectSource source, string game, int page, CancellationToken ct)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["result_count"] = ArchitectServerConfig.PageSize.ToString(CultureInfo.InvariantCulture),
            ["offset"] = page.ToString(CultureInfo.InvariantCulture),
            ["game"] = game,
            ["key_filter"] = "False",
            ["sorting_rule"] = "New",
            ["incl_diff"] = "[]", ["excl_diff"] = "[]",
            ["incl_dur"] = "[]", ["excl_dur"] = "[]",
            ["incl_tags"] = "[]", ["excl_tags"] = "[]"
        });

        using var response = await SendTrackedAsync(source, ArchitectServerConfig.CurrentUrl + "/search-new", form, TimeSpan.FromSeconds(ArchitectServerConfig.RequestTimeoutSeconds), ct);
        string body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"/search-new ({game}): HTTP {(int)response.StatusCode} {Truncate(body)}");

        var list = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(body);
        if (list == null || list.Count == 0)
            return (new List<Dictionary<string, string>>(), 0);

        if (list[0].TryGetValue("error", out var error))
            throw new HttpRequestException($"/search-new ({game}): {error}");

        var meta = list[0];
        list.RemoveAt(0);
        int pages = meta.TryGetValue("pages", out var p) && int.TryParse(p, out var parsed) ? parsed : 1;


        return (list, pages);
    }

    private static async Task<List<Dictionary<string, string>>> FetchLegacyAsync(CancellationToken ct)
    {
        const ArchitectSource source = ArchitectSource.LegacyArchitect;
        var all = new List<Dictionary<string, string>>();
        var clock = System.Diagnostics.Stopwatch.StartNew();

        for (int page = 0; page < ArchitectServerConfig.MaxPages; page++)
        {
            using var content = new StringContent("{}", Encoding.UTF8, "application/json");
            using var response = await SendTrackedAsync(source,
                ArchitectServerConfig.CurrentUrl + "/legacy?page=" + page, content, TimeSpan.FromSeconds(ArchitectServerConfig.RequestTimeoutSeconds), ct);
            string body = await response.Content.ReadAsStringAsync();

            if ((int)response.StatusCode == 404 && page == 0)
                return await FetchLegacySingleAsync(ct);

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"/legacy (page {page}): HTTP {(int)response.StatusCode} {Truncate(body)}");

            var list = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(body) ?? new List<Dictionary<string, string>>();
            all.AddRange(list);
            if (list.Count < ArchitectServerConfig.PageSize) break;
        }

        Log.Info($"[ArchitectServer] legacy: карт {all.Count} за {clock.ElapsedMilliseconds} мс");
        return all;
    }

    private static async Task<List<Dictionary<string, string>>> FetchLegacySingleAsync(CancellationToken ct)
    {
        const ArchitectSource source = ArchitectSource.LegacyArchitect;
        var json = JsonConvert.SerializeObject(new { desc = "", creator = "" });
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await SendTrackedAsync(source, ArchitectServerConfig.CurrentUrl + "/search", content,
            TimeSpan.FromSeconds(60), ct);
        string body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"/search (legacy): HTTP {(int)response.StatusCode} {Truncate(body)}");

        return JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(body) ?? new List<Dictionary<string, string>>();
    }

    private static async Task<HttpResponseMessage> SendTrackedAsync(ArchitectSource source, string url, HttpContent content, TimeSpan timeout, CancellationToken ct = default)
    {
        ArchitectLoadStatus.Update(source, p => p.RequestSentAt = DateTime.UtcNow);
        try
        {
            // Таймаут считаем сами: HttpClient.Timeout в Unity не срабатывает на асинхронных запросах
            var sending = PostAsync(url, content, ct);
            if (await Task.WhenAny(sending, Task.Delay(timeout, ct)) != sending)
                throw new TimeoutException($"нет ответа за {timeout.TotalSeconds:F0} с");

            var response = await sending;
            ArchitectLoadStatus.Update(source, p => { p.Responses++; p.State = ArchitectSourceState.Loading; });
            return response;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException || e is TimeoutException)
        {
            throw new ArchitectUnreachableException(e);
        }
        finally
        {
            ArchitectLoadStatus.Update(source, p => p.RequestSentAt = null);
        }
    }

    public static async Task<int> DownloadLevelAsync(MapRow map, string targetFolder)
    {
        if (string.IsNullOrEmpty(map?.ServerLevelId))
            throw new ArgumentException("У карты нет level_id");

        bool legacy = map.ServerSource == ArchitectSource.LegacyArchitect;
        string endpoint = legacy ? "/download" : "/download_level";
        var clock = System.Diagnostics.Stopwatch.StartNew();
        string body = await PostJsonAsync(endpoint, new JObject { ["level_id"] = map.ServerLevelId });
        Log.Info($"[ArchitectServer] {endpoint} '{map.Name}' ({map.ServerLevelId}): {body.Length} симв. за {clock.ElapsedMilliseconds} мс");

        Directory.CreateDirectory(targetFolder);
        int written = 0;

        if (legacy)
        {
            var scenes = JsonConvert.DeserializeObject<Dictionary<string, string>>(body)
                         ?? throw new InvalidDataException("пустой ответ сервера");
            foreach (var kvp in scenes)
            {
                File.WriteAllText(Path.Combine(targetFolder, SceneFileName(kvp.Key)), kvp.Value);
                written++;
            }
            return written;
        }

        var root = JObject.Parse(body);
        string levelJson = root["level"]?.ToString();
        if (string.IsNullOrEmpty(levelJson))
            throw new InvalidDataException("в ответе нет данных карты");

        string scenesFolder = Path.Combine(targetFolder, "Scenes");
        string prefabsFolder = Path.Combine(targetFolder, "Prefabs");
        Directory.CreateDirectory(scenesFolder);

        foreach (var prop in JObject.Parse(levelJson).Properties())
        {
            bool prefab = prop.Name.StartsWith("Prefab_", StringComparison.Ordinal);
            string folder = prefab ? prefabsFolder : scenesFolder;
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, SceneFileName(prop.Name)), prop.Value.ToString(Formatting.Indented));
            written++;
        }

        string workshop = root["workshop"]?.ToString();
        if (!string.IsNullOrWhiteSpace(workshop) && workshop != "null")
            File.WriteAllText(Path.Combine(targetFolder, "workshop.json"), workshop);

        return written;
    }

    public static async Task<byte[]> DownloadSaveAsync(MapRow map)
    {
        var content = new StringContent(new JObject { ["level_id"] = map.ServerLevelId }.ToString(Formatting.None),
            Encoding.UTF8, "application/json");
        using var response = await PostAsync(ArchitectServerConfig.CurrentUrl + "/download_save", content);
        var bytes = await response.Content.ReadAsByteArrayAsync();

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"/download_save: HTTP {(int)response.StatusCode} {Truncate(Encoding.UTF8.GetString(bytes))}");

        return bytes;
    }

    private static async Task<string> PostJsonAsync(string endpoint, JObject payload)
    {
        var content = new StringContent(payload.ToString(Formatting.None), Encoding.UTF8, "application/json");
        using var response = await PostAsync(ArchitectServerConfig.CurrentUrl + endpoint, content);
        string body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"{endpoint}: HTTP {(int)response.StatusCode} {Truncate(body)}");

        return body;
    }

    private static string SceneFileName(string sceneName)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) sceneName = sceneName.Replace(c, '_');
        return sceneName + ".architect.json";
    }

    public static MapRow ToMapRow(Dictionary<string, string> raw, ArchitectSource source, int serverOrder)
    {
        string Get(string key) => raw.TryGetValue(key, out var v) ? v : null;
        int Int(string key) => int.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
        bool Flag(string key) => Get(key) == "1" || string.Equals(Get(key), "true", StringComparison.OrdinalIgnoreCase);

        var map = new MapRow
        {
            Catalog = MapCatalogKind.ArchitectServer,
            ServerSource = source,
            ServerLevelId = Get("level_id"),
            Name = Get("level_name")?.Trim() ?? "?",
            Description = Get("level_desc"),
            Creator = Get("username"),
            CreatorId = Get("user_id"),
            PreviewUrl = NormalizePreviewUrl(Get("icon_url") ?? Get("url")),
            Downloads = Int("downloads"),
            Likes = Int("likes"),
            HasSave = Flag("has_save"),
            Uploaded = ParseDate(Get("uploaded")),
            ServerOrder = serverOrder,
            Updated = ParseDate(Get("updated")),
            Editors = source switch
            {
                ArchitectSource.NewArchitect => new List<MapEditor> { MapEditor.NewArchitect },
                ArchitectSource.LegacyArchitect => new List<MapEditor> { MapEditor.LegacyArchitect },
                _ => new List<MapEditor>()
            },
            CellColor = ArchitectServerConfig.GetSourceColor(source),
            League = League.Unknown
        };

        if (raw.ContainsKey("difficulty"))
        {
            map.HasServerMetadata = true;
            map.Difficulty = Int("difficulty") switch
            {
                1 => ServerDifficulty.Easy, 2 => ServerDifficulty.Medium,
                3 => ServerDifficulty.Hard, 4 => ServerDifficulty.Extreme,
                _ => ServerDifficulty.None
            };
            map.Duration = Int("duration") switch
            {
                0 => ServerDuration.Tiny, 1 => ServerDuration.Short,
                2 => ServerDuration.Medium, 3 => ServerDuration.Long,
                _ => ServerDuration.None
            };
            foreach (ServerTag tag in Enum.GetValues(typeof(ServerTag)))
                if (Flag(ArchitectServerConfig.GetTagField(tag)))
                    map.ServerTags.Add(tag);
        }

        return map;
    }

    private static DateTime? ParseDate(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        string[] formats = { "yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ss.FFFFFFF", "dd.MM.yyyy" };
        if (DateTime.TryParseExact(raw.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var exact))
            return exact.ToUniversalTime();

        return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : (DateTime?)null;
    }

    private static string NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string NormalizePreviewUrl(string raw)
    {
        raw = NullIfEmpty(raw);
        if (raw == null) return null;
        if (raw.StartsWith("//", StringComparison.Ordinal)) raw = "https:" + raw;
        else if (raw.StartsWith("/", StringComparison.Ordinal)) raw = ArchitectServerConfig.CurrentUrl + raw;

        return Uri.TryCreate(raw, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? raw
            : null;
    }

    private static string Truncate(string s) => s == null ? "" : (s.Length > 200 ? s.Substring(0, 200) + "…" : s);
}

public class ArchitectUnreachableException : Exception
{
    public ArchitectUnreachableException(Exception inner)
        : base("нет связи с сервером Architect: " + inner.Message, inner) { }
}
