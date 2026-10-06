using System;
using System.Collections.Generic;
using UnityEngine;
using GlobalListAtlas.Configuration;

namespace GlobalListAtlas.Maps;

public class MapRow
{
    public string Name;
    public string DriveUrl;

    public List<string> RequiredPublicMods = new();

    public List<MapEditor> Editors = new();
    public string Creator;
    public List<MapTag> Tags = new();
    public int Stars;
    public bool Verified;
    public string VerifierName;
    public DateTime? VerificationDate;
    public Color32 CellColor;
    public League League;
    public int SheetRowNumber;

    public MapCatalogKind Catalog = MapCatalogKind.GlobalList;
    public EventMapType EventType = EventMapType.Unknown;
    public string Description;
    public string PreviewUrl;
    public string RulesUrl;

    public string SourceFileName;

    public List<GlobalListAtlas.Integrations.MapPresetRef> Presets = new();

    public string ServerLevelId;
    public ArchitectSource ServerSource;
    public string CreatorId;
    public ServerDifficulty Difficulty = ServerDifficulty.None;
    public ServerDuration Duration = ServerDuration.None;
    public List<ServerTag> ServerTags = new();
    public int Downloads;
    public int Likes;
    public DateTime? Uploaded;

    public int ServerOrder;

    public bool HiddenDuplicate;
    public DateTime? Updated;
    public bool HasSave;

    public bool HasServerMetadata;

    public MapFileKind FileKind
    {
        get
        {
            if (Catalog == MapCatalogKind.ArchitectServer)
                return string.IsNullOrEmpty(ServerLevelId) ? MapFileKind.None : MapFileKind.Archive;

            if (string.IsNullOrEmpty(DriveUrl)) return MapFileKind.None;

            string name = SourceFileName ?? "";
            if (name.EndsWith(".dat", StringComparison.OrdinalIgnoreCase)) return MapFileKind.Save;
            if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return MapFileKind.SingleJson;
            return MapFileKind.Archive;
        }
    }
}

public enum MapFileKind
{
    None,
    Archive,     // архив карты
    SingleJson,  // одна комната сразу json-файлом
    Save         // файл сохранения игры (.dat)
}