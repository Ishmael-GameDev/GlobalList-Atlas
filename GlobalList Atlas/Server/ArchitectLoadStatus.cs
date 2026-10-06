using System;
using System.Collections.Generic;
using GlobalListAtlas.Configuration;

namespace GlobalListAtlas.Server;

public enum ArchitectSourceState { Waiting, Loading, Done, Failed }

public class ArchitectSourceProgress
{
    public ArchitectSourceState State = ArchitectSourceState.Waiting;
    public int PagesDone;
    public int PagesTotal;
    public int Maps;
    public int Responses;
    public DateTime? RequestSentAt;
    public string Error;

    public ArchitectSourceProgress Copy() => (ArchitectSourceProgress)MemberwiseClone();
}

public static class ArchitectLoadStatus
{
    private static readonly object Gate = new();
    private static readonly Dictionary<ArchitectSource, ArchitectSourceProgress> Sources = new();

    public static DateTime? StartedAt { get; private set; }
    public static DateTime? FinishedAt { get; private set; }

    public static void Begin()
    {
        lock (Gate)
        {
            Sources.Clear();
            foreach (ArchitectSource source in Enum.GetValues(typeof(ArchitectSource)))
                Sources[source] = new ArchitectSourceProgress();
            StartedAt = DateTime.UtcNow;
            FinishedAt = null;
        }
    }

    public static void Finish()
    {
        lock (Gate) FinishedAt = DateTime.UtcNow;
    }

    public static void Update(ArchitectSource source, Action<ArchitectSourceProgress> change)
    {
        lock (Gate)
        {
            if (!Sources.TryGetValue(source, out var progress))
                Sources[source] = progress = new ArchitectSourceProgress();
            change(progress);
        }
    }

    public static List<(ArchitectSource Source, ArchitectSourceProgress Progress)> Snapshot()
    {
        lock (Gate)
        {
            var list = new List<(ArchitectSource, ArchitectSourceProgress)>();
            foreach (var kvp in Sources) list.Add((kvp.Key, kvp.Value.Copy()));
            return list;
        }
    }
}
