using System;
using System.Collections.Concurrent;

namespace GlobalListAtlas.Net;

public static class BandwidthTracker
{
    private static readonly ConcurrentQueue<(DateTime Time, long Bytes)> Samples = new();
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(1);

    public static void Report(long bytesDelta)
    {
        if (bytesDelta <= 0) return;
        Samples.Enqueue((DateTime.UtcNow, bytesDelta));
        Trim();
    }

    public static long GetBytesPerSecond()
    {
        Trim();
        long sum = 0;
        foreach (var s in Samples) sum += s.Bytes;
        return sum;
    }

    private static void Trim()
    {
        var cutoff = DateTime.UtcNow - Window;
        while (Samples.TryPeek(out var oldest) && oldest.Time < cutoff)
            Samples.TryDequeue(out _);
    }

    public static string FormatSpeed(long bytesPerSecond)
    {
        double kb = bytesPerSecond / 1024.0;
        return kb >= 1024.0 ? $"{kb / 1024.0:F1} МБ/с" : $"{kb:F0} КБ/с";
    }
}