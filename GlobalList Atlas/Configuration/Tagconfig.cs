using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using GlobalListAtlas.Logging;

namespace GlobalListAtlas.Configuration;

public enum MapTag
{
    Classic,
    AdvancedTech,
    ModdedTech,
    Glitches,
    Lowgear,
    Fullgear,
    Consistency,
    Sync,
    Presice, // так тег назван/написан в таблице (см. столбец G) — не переименовывать без проверки самой таблицы
    Memory,
    Puzzle,
    Routing,
    Ultrasegmented,
    Combat
}

// Реестр тегов карт (раскрывающийся список в столбце G таблицы) и их цветов
public static class TagConfig
{
    public const string TagSeparator = ", ";

    public static readonly (MapTag Tag, string Label, Color32 Color)[] Palette =
    {
        (MapTag.Classic,        "Classic",        new Color32(0xD4, 0xED, 0xBC, 0xFF)),
        (MapTag.AdvancedTech,   "Advanced Tech",  new Color32(0xFF, 0xE5, 0xA0, 0xFF)),
        (MapTag.ModdedTech,     "Modded Tech",    new Color32(0xFF, 0xC8, 0xAA, 0xFF)),
        (MapTag.Glitches,       "Glitches",       new Color32(0xFF, 0x9C, 0x8F, 0xFF)),
        (MapTag.Lowgear,        "Lowgear",        new Color32(0xE6, 0xCF, 0xF2, 0xFF)),
        (MapTag.Fullgear,       "Fullgear",       new Color32(0xBF, 0xE1, 0xF6, 0xFF)),
        (MapTag.Consistency,    "Consistency",    new Color32(0xE6, 0xE6, 0xE6, 0xFF)),
        (MapTag.Sync,           "Sync",           new Color32(0xE6, 0xE6, 0xE6, 0xFF)),
        (MapTag.Presice,        "Presice",        new Color32(0xE6, 0xE6, 0xE6, 0xFF)),
        (MapTag.Memory,         "Memory",         new Color32(0xE6, 0xE6, 0xE6, 0xFF)),
        (MapTag.Puzzle,         "Puzzle",         new Color32(0xE6, 0xE6, 0xE6, 0xFF)),
        (MapTag.Routing,        "Routing",        new Color32(0xC6, 0xDB, 0xE1, 0xFF)),
        (MapTag.Ultrasegmented, "Ultrasegmented", new Color32(0xC6, 0xDB, 0xE1, 0xFF)),
        (MapTag.Combat,         "Combat",         new Color32(0xC6, 0xDB, 0xE1, 0xFF)),
    };

    private static readonly Dictionary<string, MapTag> LabelToTag =
        Palette.ToDictionary(p => p.Label, p => p.Tag, StringComparer.Ordinal);

    // Разбирает содержимое ячейки столбца G ("Tag1, Tag2, ...") в список тегов
    public static List<MapTag> ParseTags(string rawCellValue)
    {
        var result = new List<MapTag>();
        if (string.IsNullOrWhiteSpace(rawCellValue))
            return result;

        var pieces = rawCellValue.Split(new[] { TagSeparator }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var piece in pieces)
        {
            string label = piece.Trim();
            if (label.Length == 0)
                continue;

            if (LabelToTag.TryGetValue(label, out var tag))
            {
                result.Add(tag);
            }
            else
            {
                Log.Warn($"Нераспознанный тег '{label}' в столбце тегов — пропущен. " +
                         "Если это новый тег, добавьте его в TagConfig.Palette.");
            }
        }

        return result;
    }

    public static string GetLabel(MapTag tag)
    {
        foreach (var (t, label, _) in Palette)
            if (t == tag) return label;
        return tag.ToString();
    }

    public static Color32 GetColor(MapTag tag)
    {
        foreach (var (t, _, color) in Palette)
            if (t == tag) return color;
        return new Color32(255, 255, 255, 255);
    }
}