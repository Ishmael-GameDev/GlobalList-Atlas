using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using GlobalListAtlas.Logging;
using GlobalListAtlas.Net;

namespace GlobalListAtlas.Sheets;

public static class GoogleSheetClient
{
    public static async Task<byte[]> DownloadWorkbookAsync(string sheetId, Action<long, long?> onProgress = null)
    {
        string url = $"https://docs.google.com/spreadsheets/d/{sheetId}/export?format=xlsx";
        Modding.Logger.Log($"Скачивание таблицы (xlsx): {url}");

        using var client = new HttpClient();
        var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        long? total = response.Content.Headers.ContentLength;

        using var stream = await response.Content.ReadAsStreamAsync();
        using var memory = new MemoryStream();
        var buffer = new byte[81920];
        long totalRead = 0;
        int read;

        while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            await memory.WriteAsync(buffer, 0, read);
            totalRead += read;
            BandwidthTracker.Report(read);
            onProgress?.Invoke(totalRead, total);
        }

        var bytes = memory.ToArray();
        Modding.Logger.Log($"Таблица скачана, размер {bytes.Length} байт");
        return bytes;
    }

    public static async Task<string> DownloadSheetCsvAsync(string sheetId, long gid)
    {
        string url = $"https://docs.google.com/spreadsheets/d/{sheetId}/export?format=csv&gid={gid}";
        Modding.Logger.Log($"Скачивание CSV-отпечатка листа (gid={gid}): {url}");

        using var client = new HttpClient();
        var csv = await client.GetStringAsync(url);

        Modding.Logger.Log($"CSV-отпечаток получен, длина {csv.Length} символов");
        return csv;
    }
}