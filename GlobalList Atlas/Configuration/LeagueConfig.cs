using UnityEngine;
using GlobalListAtlas.Configuration;

namespace GlobalListAtlas.Configuration;

public enum League
{
    Void,
    Diamond,
    Gold,
    Silver,
    Bronze,
    White,
    Unknown
}

// Палитра лиг для группировки и подсветки карт по цвету их ячейки в таблице
public static class LeagueConfig
{
    public const string ColorSourceColumn = SheetConfig.NameColumn;

    public static readonly (League League, string Label, Color32 Color)[] Palette =
    {
        (League.Void,    "VOID LEAGUE",    new Color32(0x43, 0x43, 0x43, 0xFF)),
        (League.Diamond, "DIAMOND LEAGUE", new Color32(0x76, 0xA5, 0xAF, 0xFF)),
        (League.Gold,    "GOLD LEAGUE",    new Color32(0xFF, 0xD9, 0x66, 0xFF)),
        (League.Silver,  "SILVER LEAGUE",  new Color32(0xCC, 0xCC, 0xCC, 0xFF)),
        (League.Bronze,  "BRONZE LEAGUE",  new Color32(0xF6, 0xB2, 0x6B, 0xFF)),
        (League.White,   "WHITE LEAGUE",   new Color32(0xEF, 0xEF, 0xEF, 0xFF)),
    };

    // Ищет ближайший (по евклидовому расстоянию в RGB) цвет лиги к заданному цвету ячейки
    public static League ClassifyColor(Color32? cellColor)
    {
        if (cellColor == null)
            return League.Unknown;
        var c = cellColor.Value;
        League best = League.Unknown;
        double bestDist = double.MaxValue;
        foreach (var (league, _, paletteColor) in Palette)
        {
            double dr = c.r - paletteColor.r;
            double dg = c.g - paletteColor.g;
            double db = c.b - paletteColor.b;
            double dist = dr * dr + dg * dg + db * db;
            if (dist < bestDist)
            {
                bestDist = dist;
                best = league;
            }
        }
        return best;
    }

    public static string GetLabel(League league)
    {
        foreach (var (l, label, _) in Palette)
            if (l == league) return label;
        return "OTHER";
    }

    public static string GetLocalizedLabel(League league)
    {
        return Localization.Get($"league.{league.ToString().ToLower()}");
    }

    public static int GetOrder(League league)
    {
        for (int i = 0; i < Palette.Length; i++)
            if (Palette[i].League == league) return i;
        return Palette.Length; // Unknown — в самый конец
    }
}