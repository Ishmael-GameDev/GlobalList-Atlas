using System;
using UnityEngine;

namespace GlobalListAtlas.Configuration;

public enum MapCatalogKind
{
    GlobalList,
    EventCommunity,
    ArchitectServer
}

public enum EventMapType
{
    Unknown,
    Event,
    PvP
}

public static class EventCatalogConfig
{
    public const string SheetId = "163jEMa9pzX-IQs0tPO6hNftxTbQwLsPzddUxhlraC2w";

    public const string MapsSheetName = "Maps";

    public const long MapsSheetGid = 91423732;

    public const int FirstDataRow = 2;

    public const string NameColumn = "A";
    public const string AuthorColumn = "B";
    public const string FileColumn = "C";
    public const string EditorColumn = "D";
    public const string TypeColumn = "E";
    public const string DescriptionColumn = "F";
    public const string PreviewColumn = "G";
    public const string RulesColumn = "H";
    public const string ModsColumn = "I";

    public const string TeleportMasterPresetColumn = "J";

    public static readonly Color32 EventColor = new(0x3E, 0x6F, 0xB5, 0xFF); // синий
    public static readonly Color32 PvPColor = new(0xB5, 0x44, 0x4B, 0xFF);   // красный
    public static readonly Color32 UnknownColor = new(0x55, 0x5A, 0x66, 0xFF);

    public static string GetRowUrl(int sheetRowNumber) =>
        $"https://docs.google.com/spreadsheets/d/{SheetId}/edit?gid={MapsSheetGid}#gid={MapsSheetGid}&range=A{sheetRowNumber}";

    public static EventMapType ParseType(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return EventMapType.Unknown;

        string value = raw.Trim();
        if (value.Equals("PvP", StringComparison.OrdinalIgnoreCase)) return EventMapType.PvP;
        if (value.Equals("Event", StringComparison.OrdinalIgnoreCase)) return EventMapType.Event;
        return EventMapType.Unknown;
    }

    public static Color32 GetTypeColor(EventMapType type) => type switch
    {
        EventMapType.Event => EventColor,
        EventMapType.PvP => PvPColor,
        _ => UnknownColor
    };

    public static string GetTypeLabel(EventMapType type) => type switch
    {
        EventMapType.Event => "Event",
        EventMapType.PvP => "PvP",
        _ => "—"
    };
}
