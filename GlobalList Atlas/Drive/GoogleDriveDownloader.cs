using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GlobalListAtlas.Logging;

namespace GlobalListAtlas.Drive;

public static class GoogleDriveDownloader
{
    private static readonly Regex FileIdRegex = new(@"/d/([a-zA-Z0-9_-]+)");
    private static readonly Regex FormActionRegex = new(@"<form[^>]*action=[""']([^""']+)[""']", RegexOptions.IgnoreCase);
    private static readonly Regex InputTagRegex = new(@"<input[^>]+>", RegexOptions.IgnoreCase);
    private static readonly Regex NameAttrRegex = new(@"name=[""']([^""']+)[""']", RegexOptions.IgnoreCase);
    private static readonly Regex ValueAttrRegex = new(@"value=[""']([^""']*)[""']", RegexOptions.IgnoreCase);

    public static string ExtractFileId(string driveUrl)
    {
        if (string.IsNullOrEmpty(driveUrl))
            return null;

        var match = FileIdRegex.Match(driveUrl);
        return match.Success ? match.Groups[1].Value : null;
    }

    // Размер файла на Google Drive без скачивания: запрашиваем только заголовки
    public static async Task<long?> TryGetFileSizeAsync(string driveUrl)
    {
        var fileId = ExtractFileId(driveUrl);
        if (fileId == null) return null;

        try
        {
            var cookieContainer = new CookieContainer();
            using var handler = new HttpClientHandler
            {
                CookieContainer = cookieContainer,
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            using var client = new HttpClient(handler);
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

            var response = await client.GetAsync($"https://drive.google.com/uc?export=download&id={fileId}",
                HttpCompletionOption.ResponseHeadersRead);

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";

            // Большой файл — Drive отдаёт страницу подтверждения, реальный размер за ней
            if (contentType.Contains("text/html"))
            {
                string html = await response.Content.ReadAsStringAsync();
                string downloadUrl = BuildConfirmedDownloadUrl(html, fileId);
                response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            }

            return response.IsSuccessStatusCode ? response.Content.Headers.ContentLength : null;
        }
        catch (Exception e)
        {
            Log.Warn($"Не удалось узнать размер файла Drive id={fileId}: {e.Message}");
            return null;
        }
    }

    public static async Task<byte[]> DownloadAsync(
    string driveUrl,
    Action<long> onSizeRetrieved = null,
    Action<long, long?> onProgress = null,
    CancellationToken cancellationToken = default)
    {
        var fileId = ExtractFileId(driveUrl);
        if (fileId == null)
            return null;

        var cookieContainer = new CookieContainer();
        using var handler = new HttpClientHandler
        {
            CookieContainer = cookieContainer,
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };

        using var client = new HttpClient(handler);
        client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

        string baseUrl = $"https://drive.google.com/uc?export=download&id={fileId}";
        Log.Info($"Скачивание файла с Google Drive, id={fileId}");

        var response = await client.GetAsync(baseUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "";

        if (response.Content.Headers.ContentLength.HasValue)
            onSizeRetrieved?.Invoke(response.Content.Headers.ContentLength.Value);

        if (contentType.Contains("text/html"))
        {
            string html = await response.Content.ReadAsStringAsync();
            Log.Info("Файл большой — извлекаем параметры подтверждения из HTML...");

            string downloadUrl = BuildConfirmedDownloadUrl(html, fileId);

            response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (response.Content.Headers.ContentLength.HasValue)
                onSizeRetrieved?.Invoke(response.Content.Headers.ContentLength.Value);
        }

        if (!response.IsSuccessStatusCode)
        {
            Log.Error($"Ошибка скачивания: HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
            return null;
        }

        long? total = response.Content.Headers.ContentLength;
        var finalContentType = response.Content.Headers.ContentType?.MediaType ?? "";

        using var stream = await response.Content.ReadAsStreamAsync();
        using var memory = new MemoryStream();
        var buffer = new byte[81920];
        long totalRead = 0;
        int read;

        while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
        {
            await memory.WriteAsync(buffer, 0, read, cancellationToken);
            totalRead += read;
            Net.BandwidthTracker.Report(read);
            onProgress?.Invoke(totalRead, total);
        }

        var bytes = memory.ToArray();

        if (finalContentType.Contains("text/html"))
        {
            Log.Info("Google Drive снова вернул HTML-страницу вместо файла. Скачивание не удалось.");
            return null;
        }

        Log.Info($"Скачано {bytes.Length} байт для id={fileId}");
        return bytes;
    }

    // Собирает ссылку на скачивание из формы подтверждения, которую Drive показывает
    private static string BuildConfirmedDownloadUrl(string html, string fileId)
    {
        var formMatch = FormActionRegex.Match(html);
        string actionUrl = formMatch.Success
            ? WebUtility.HtmlDecode(formMatch.Groups[1].Value)
            : "https://drive.usercontent.google.com/download";

        var inputs = ParseFormInputs(html);

        if (!inputs.Any(k => k.Key.Equals("id", StringComparison.OrdinalIgnoreCase)))
            inputs.Add(new KeyValuePair<string, string>("id", fileId));
        if (!inputs.Any(k => k.Key.Equals("export", StringComparison.OrdinalIgnoreCase)))
            inputs.Add(new KeyValuePair<string, string>("export", "download"));
        if (!inputs.Any(k => k.Key.Equals("confirm", StringComparison.OrdinalIgnoreCase)))
            inputs.Add(new KeyValuePair<string, string>("confirm", "t"));

        string queryString = string.Join("&", inputs.Select(i => $"{Uri.EscapeDataString(i.Key)}={Uri.EscapeDataString(i.Value)}"));
        string separator = actionUrl.Contains("?") ? "&" : "?";
        return $"{actionUrl}{separator}{queryString}";
    }

    private static List<KeyValuePair<string, string>> ParseFormInputs(string html)
    {
        var inputs = new List<KeyValuePair<string, string>>();
        var inputMatches = InputTagRegex.Matches(html);

        foreach (Match inputMatch in inputMatches)
        {
            string tagHtml = inputMatch.Value;
            var nameMatch = NameAttrRegex.Match(tagHtml);
            var valueMatch = ValueAttrRegex.Match(tagHtml);

            if (nameMatch.Success)
            {
                string name = WebUtility.HtmlDecode(nameMatch.Groups[1].Value);
                string value = valueMatch.Success ? WebUtility.HtmlDecode(valueMatch.Groups[1].Value) : "";
                inputs.Add(new KeyValuePair<string, string>(name, value));
            }
        }

        return inputs;
    }
}