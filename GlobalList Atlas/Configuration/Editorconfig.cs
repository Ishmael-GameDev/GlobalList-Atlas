using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using GlobalListAtlas.Logging;

namespace GlobalListAtlas.Configuration;

public enum MapEditor
{
    DecorationMaster,
    LegacyArchitect,
    NewArchitect,
    CustomMod,
    CustomEngine
}

// Реестр редакторов карт (столбец F таблицы) и их цветов
public static class EditorConfig
{
    public const string EditorSeparator = ", ";
    public static readonly (MapEditor Editor, string Label, Color32 Color)[] Palette =
    {
        (MapEditor.DecorationMaster, "Decoration Master", new Color32(0xBF, 0xE1, 0xF6, 0xFF)),
        (MapEditor.LegacyArchitect,  "Legacy Architect",  new Color32(0xC6, 0xDB, 0xE1, 0xFF)),
        (MapEditor.NewArchitect,     "New Architect",     new Color32(0xE6, 0xCF, 0xF2, 0xFF)),
        (MapEditor.CustomMod,        "Custom Mod",        new Color32(0xE6, 0xE6, 0xE6, 0xFF)),
        (MapEditor.CustomEngine,     "Custom Engine",     new Color32(0xFF, 0x9A, 0x8D, 0xFF)),
    };

    private static readonly Dictionary<string, MapEditor> LabelToEditor =
        Palette.ToDictionary(p => p.Label, p => p.Editor, StringComparer.Ordinal);

    // Разбирает содержимое ячейки столбца F ("Editor1, Editor2, ...") в список редакторов
    public static List<MapEditor> ParseEditors(string rawCellValue)
    {
        var result = new List<MapEditor>();
        if (string.IsNullOrWhiteSpace(rawCellValue))
            return result;

        var pieces = rawCellValue.Split(new[] { EditorSeparator }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var piece in pieces)
        {
            string label = piece.Trim();
            if (label.Length == 0)
                continue;

            if (LabelToEditor.TryGetValue(label, out var editor))
            {
                result.Add(editor);
            }
            else
            {
                Log.Warn($"Нераспознанный редактор '{label}' в столбце F — пропущен. " +
                         "Если это новый редактор, добавьте его в EditorConfig.Palette.");
            }
        }

        return result;
    }

    public static string GetLabel(MapEditor editor)
    {
        foreach (var (e, label, _) in Palette)
            if (e == editor) return label;
        return editor.ToString();
    }

    public static Color32 GetColor(MapEditor editor)
    {
        foreach (var (e, _, color) in Palette)
            if (e == editor) return color;
        return new Color32(255, 255, 255, 255);
    }
}