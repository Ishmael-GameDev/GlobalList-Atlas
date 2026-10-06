using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using GlobalListAtlas.Logging;
using GlobalListAtlas.Net;

namespace GlobalListAtlas.Sheets;

public static class GoogleSheetClient
{
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(30);

    public static async Task<T> WithTimeout<T>(Task<T> task, TimeSpan timeout)
    {
        if (await Task.WhenAny(task, Task.Delay(timeout)) != task)
            throw new TimeoutException($"таблица не отвечает {timeout.TotalSeconds:F0} с");
        return await task;
    }

    public static async Task<byte[]> DownloadWorkbookAsync(string sheetId, Action<long, long?> onProgress = null)
    {
        string url = $"https://docs.google.com/spreadsheets/d/{sheetId}/export?format=xlsx";
        Log.Info($"Скачивание таблицы (xlsx): {url}");

        using var client = new HttpClient();
        var response = await WithTimeout(client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead), IdleTimeout);
        response.EnsureSuccessStatusCode();

        long? total = response.Content.Headers.ContentLength;

        using var stream = await response.Content.ReadAsStreamAsync();
        using var memory = new MemoryStream();
        var buffer = new byte[81920];
        long totalRead = 0;
        int read;

        while ((read = await WithTimeout(stream.ReadAsync(buffer, 0, buffer.Length), IdleTimeout)) > 0)
        {
            await memory.WriteAsync(buffer, 0, read);
            totalRead += read;
            BandwidthTracker.Report(read);
            onProgress?.Invoke(totalRead, total);
        }

        var bytes = memory.ToArray();
        Log.Info($"Таблица скачана, размер {bytes.Length} байт");
        return bytes;
    }

    public static async Task<string> DownloadSheetCsvAsync(string sheetId, long gid)
    {
        string url = $"https://docs.google.com/spreadsheets/d/{sheetId}/export?format=csv&gid={gid}";
        Log.Info($"Скачивание CSV-отпечатка листа (gid={gid}): {url}");

        using var client = new HttpClient();
        var csv = await WithTimeout(client.GetStringAsync(url), IdleTimeout);

        Log.Info($"CSV-отпечаток получен, длина {csv.Length} символов");
        return csv;
    }
}