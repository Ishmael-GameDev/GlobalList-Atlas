using System;
using UnityEngine;

namespace GlobalListAtlas.Configuration;

public enum ArchitectSource
{
    NewArchitect,
    LegacyArchitect,  // старые /search и /download
    Silksong
}

public enum ServerDifficulty { None, Easy, Medium, Hard, Extreme }
public enum ServerDuration { Tiny, Short, Medium, Long, None }

public enum ServerTag { Platforming, Minigames, Multiplayer, Gauntlets, Areas, Troll, Bosses }

public static class ArchitectServerConfig
{
    public const string Url = "https://cometcake575.pythonanywhere.com";
    public const int RequestTimeoutSeconds = 20;
    public const string MirrorUrl = "https://purple-violet-810f.ishmaelarchitectmirror.workers.dev";

    // Зеркало включается только кнопкой после недоступности сервера и сбрасывается при новой загрузке парсера
    public static bool UseMirror;

    public static string CurrentUrl => UseMirror ? MirrorUrl : Url;

    public const string NewArchitectGame = "hk_modern";
    public const string SilksongGame = "silksong";

    public const int PageSize = 100;

    public const int MaxPages = 400;

    public static readonly Color32 SilksongColor = new(0xD9, 0x8F, 0xA8, 0xFF);

    public static Color32 GetSourceColor(ArchitectSource source) => source switch
    {
        ArchitectSource.NewArchitect => EditorConfig.GetColor(MapEditor.NewArchitect),
        ArchitectSource.LegacyArchitect => EditorConfig.GetColor(MapEditor.LegacyArchitect),
        _ => SilksongColor
    };

    public static string GetSourceLabel(ArchitectSource source) => source switch
    {
        ArchitectSource.NewArchitect => EditorConfig.GetLabel(MapEditor.NewArchitect),
        ArchitectSource.LegacyArchitect => EditorConfig.GetLabel(MapEditor.LegacyArchitect),
        _ => "Silksong Architect"
    };

    public static string GetDifficultyLabel(ServerDifficulty d) => d switch
    {
        ServerDifficulty.Easy => "Easy",
        ServerDifficulty.Medium => "Medium",
        ServerDifficulty.Hard => "Hard",
        ServerDifficulty.Extreme => "Extreme",
        _ => "—"
    };

    public static string GetDurationLabel(ServerDuration d) => d switch
    {
        ServerDuration.Tiny => "< 10 min",
        ServerDuration.Short => "10–30 min",
        ServerDuration.Medium => "30–60 min",
        ServerDuration.Long => "60+ min",
        _ => "—"
    };

    public static string GetTagField(ServerTag tag) => tag.ToString().ToLowerInvariant();

    private static readonly Color32 RedTone = new(0xD0, 0x5A, 0x5A, 0xFF);
    private static readonly Color32 YellowTone = new(0xE0, 0xC0, 0x50, 0xFF);
    private static readonly Color32 GreenTone = new(0x5C, 0xB8, 0x6A, 0xFF);
    private static readonly Color32 BlueTone = new(0x7F, 0xC8, 0xF0, 0xFF);
    private static readonly Color32 NoneTone = new(0x99, 0x99, 0x99, 0xFF);

    public static Color32 GetDifficultyColor(ServerDifficulty d) => d switch
    {
        ServerDifficulty.Easy => BlueTone,
        ServerDifficulty.Medium => GreenTone,
        ServerDifficulty.Hard => YellowTone,
        ServerDifficulty.Extreme => RedTone,
        _ => NoneTone
    };

    public static Color32 GetDurationColor(ServerDuration d) => d switch
    {
        ServerDuration.Tiny => BlueTone,
        ServerDuration.Short => GreenTone,
        ServerDuration.Medium => YellowTone,
        ServerDuration.Long => RedTone,
        _ => NoneTone
    };

    public static Color32 GetTagColor(ServerTag tag) => tag switch
    {
        ServerTag.Platforming => new Color32(0xBF, 0xE1, 0xF6, 0xFF),
        ServerTag.Minigames => new Color32(0xD4, 0xED, 0xBC, 0xFF),
        ServerTag.Multiplayer => new Color32(0xE6, 0xCF, 0xF2, 0xFF),
        ServerTag.Gauntlets => new Color32(0xFF, 0x9C, 0x8F, 0xFF),
        ServerTag.Areas => new Color32(0xFF, 0xE5, 0xA0, 0xFF),
        ServerTag.Troll => new Color32(0xFF, 0xC8, 0xAA, 0xFF),
        _ => new Color32(0xC6, 0xDB, 0xE1, 0xFF)
    };
}
