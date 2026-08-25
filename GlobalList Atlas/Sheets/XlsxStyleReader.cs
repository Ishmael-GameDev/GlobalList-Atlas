using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using UnityEngine;

namespace GlobalListAtlas.Sheets;

// Читает заливки (цвета фона) ячеек из styles.xml экспортированного xlsx
internal static class XlsxStyleReader
{
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace DrawingMain = "http://schemas.openxmlformats.org/drawingml/2006/main";

    private static readonly Color32[] IndexedPalette =
    {
        new(0x00,0x00,0x00,0xFF), new(0xFF,0xFF,0xFF,0xFF), new(0xFF,0x00,0x00,0xFF), new(0x00,0xFF,0x00,0xFF),
        new(0x00,0x00,0xFF,0xFF), new(0xFF,0xFF,0x00,0xFF), new(0xFF,0x00,0xFF,0xFF), new(0x00,0xFF,0xFF,0xFF),
        new(0x00,0x00,0x00,0xFF), new(0xFF,0xFF,0xFF,0xFF), new(0xFF,0x00,0x00,0xFF), new(0x00,0xFF,0x00,0xFF),
        new(0x00,0x00,0xFF,0xFF), new(0xFF,0xFF,0x00,0xFF), new(0xFF,0x00,0xFF,0xFF), new(0x00,0xFF,0xFF,0xFF),
        new(0x80,0x00,0x00,0xFF), new(0x00,0x80,0x00,0xFF), new(0x00,0x00,0x80,0xFF), new(0x80,0x80,0x00,0xFF),
        new(0x80,0x00,0x80,0xFF), new(0x00,0x80,0x80,0xFF), new(0xC0,0xC0,0xC0,0xFF), new(0x80,0x80,0x80,0xFF),
        new(0x99,0x99,0xFF,0xFF), new(0x99,0x33,0x66,0xFF), new(0xFF,0xFF,0xCC,0xFF), new(0xCC,0xFF,0xFF,0xFF),
        new(0x66,0x00,0x66,0xFF), new(0xFF,0x80,0x80,0xFF), new(0x00,0x66,0xCC,0xFF), new(0xCC,0xCC,0xFF,0xFF),
        new(0x00,0x00,0x80,0xFF), new(0xFF,0x00,0xFF,0xFF), new(0xFF,0xFF,0x00,0xFF), new(0x00,0xFF,0xFF,0xFF),
        new(0x80,0x00,0x80,0xFF), new(0x80,0x00,0x00,0xFF), new(0x00,0x80,0x80,0xFF), new(0x00,0x00,0xFF,0xFF),
        new(0x00,0xCC,0xFF,0xFF), new(0xCC,0xFF,0xFF,0xFF), new(0xCC,0xFF,0xCC,0xFF), new(0xFF,0xFF,0x99,0xFF),
        new(0x99,0xCC,0xFF,0xFF), new(0xFF,0x99,0xCC,0xFF), new(0xCC,0x99,0xFF,0xFF), new(0xFF,0xCC,0x99,0xFF),
        new(0x33,0x66,0xFF,0xFF), new(0x33,0xCC,0xCC,0xFF), new(0x99,0xCC,0x00,0xFF), new(0xFF,0xCC,0x00,0xFF),
        new(0xFF,0x99,0x00,0xFF), new(0xFF,0x66,0x00,0xFF), new(0x66,0x66,0x99,0xFF), new(0x96,0x96,0x96,0xFF),
        new(0x00,0x33,0x66,0xFF), new(0x33,0x99,0x66,0xFF), new(0x00,0x33,0x00,0xFF), new(0x33,0x33,0x00,0xFF),
        new(0x99,0x33,0x00,0xFF), new(0x99,0x33,0x66,0xFF), new(0x33,0x33,0x99,0xFF), new(0x33,0x33,0x33,0xFF),
    };

    public static Dictionary<int, Color32?> LoadStyleIndexToFillColor(ZipArchive zip)
    {
        var result = new Dictionary<int, Color32?>();
        if (zip.GetEntry("xl/styles.xml") == null)
            return result;

        var stylesXml = LoadXml(zip, "xl/styles.xml");
        var themeColors = LoadThemeColors(zip);

        var fills = stylesXml.Root!.Element(Main + "fills")?.Elements(Main + "fill").ToList()
                    ?? new List<XElement>();

        var fillColors = new List<Color32?>();
        foreach (var fill in fills)
        {
            var pattern = fill.Element(Main + "patternFill");
            var patternType = pattern?.Attribute("patternType")?.Value;

            if (pattern == null || patternType != "solid")
            {
                fillColors.Add(null);
                continue;
            }

            var fgColor = pattern.Element(Main + "fgColor");
            fillColors.Add(ResolveColor(fgColor, themeColors));
        }

        var cellXfs = stylesXml.Root!.Element(Main + "cellXfs")?.Elements(Main + "xf").ToList()
                      ?? new List<XElement>();

        for (int i = 0; i < cellXfs.Count; i++)
        {
            var xf = cellXfs[i];
            if (!int.TryParse(xf.Attribute("fillId")?.Value, out int fillId))
            {
                result[i] = null;
                continue;
            }

            result[i] = (fillId >= 0 && fillId < fillColors.Count) ? fillColors[fillId] : null;
        }

        return result;
    }

    private static Color32? ResolveColor(XElement colorElement, List<Color32> themeColors)
    {
        if (colorElement == null)
            return null;

        var rgbAttr = colorElement.Attribute("rgb")?.Value;
        if (!string.IsNullOrEmpty(rgbAttr))
            return HexToColor32(rgbAttr);

        var indexedAttr = colorElement.Attribute("indexed")?.Value;
        if (indexedAttr != null && int.TryParse(indexedAttr, out int indexedValue)
            && indexedValue >= 0 && indexedValue < IndexedPalette.Length)
        {
            return IndexedPalette[indexedValue];
        }

        var themeAttr = colorElement.Attribute("theme")?.Value;
        if (themeAttr != null && int.TryParse(themeAttr, out int themeIndex)
            && themeIndex >= 0 && themeIndex < themeColors.Count)
        {
            var baseColor = themeColors[themeIndex];
            double tint = 0;
            var tintAttr = colorElement.Attribute("tint")?.Value;
            if (tintAttr != null)
                double.TryParse(tintAttr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out tint);

            return ApplyTint(baseColor, tint);
        }

        return null;
    }

    private static Color32 ApplyTint(Color32 color, double tint)
    {
        byte Adjust(byte channel)
        {
            double c = channel / 255.0;
            double result = tint < 0 ? c * (1 + tint) : c * (1 - tint) + tint;
            return (byte)Mathf.Clamp(Mathf.RoundToInt((float)(result * 255)), 0, 255);
        }

        return new Color32(Adjust(color.r), Adjust(color.g), Adjust(color.b), 255);
    }

    private static List<Color32> LoadThemeColors(ZipArchive zip)
    {
        var result = new List<Color32>(new Color32[12]);
        if (zip.GetEntry("xl/theme/theme1.xml") == null)
            return result;

        try
        {
            var themeXml = LoadXml(zip, "xl/theme/theme1.xml");
            var clrScheme = themeXml.Descendants(DrawingMain + "clrScheme").FirstOrDefault();
            if (clrScheme == null) return result;

            (string, int)[] order =
            {
                ("lt1", 0), ("dk1", 1), ("lt2", 2), ("dk2", 3),
                ("accent1", 4), ("accent2", 5), ("accent3", 6),
                ("accent4", 7), ("accent5", 8), ("accent6", 9),
                ("hlink", 10), ("folHlink", 11)
            };

            foreach (var (elementName, index) in order)
            {
                var el = clrScheme.Element(DrawingMain + elementName);
                var srgb = el?.Element(DrawingMain + "srgbClr")?.Attribute("val")?.Value;
                var sysClrLast = el?.Element(DrawingMain + "sysClr")?.Attribute("lastClr")?.Value;
                string hex = srgb ?? sysClrLast;
                if (hex != null)
                    result[index] = HexToColor32(hex);
            }
        }
        catch
        {
        }

        return result;
    }

    private static Color32 HexToColor32(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 8)
        {
            byte a = Convert.ToByte(hex.Substring(0, 2), 16);
            byte r = Convert.ToByte(hex.Substring(2, 2), 16);
            byte g = Convert.ToByte(hex.Substring(4, 2), 16);
            byte b = Convert.ToByte(hex.Substring(6, 2), 16);
            return new Color32(r, g, b, a);
        }
        if (hex.Length == 6)
        {
            byte r = Convert.ToByte(hex.Substring(0, 2), 16);
            byte g = Convert.ToByte(hex.Substring(2, 2), 16);
            byte b = Convert.ToByte(hex.Substring(4, 2), 16);
            return new Color32(r, g, b, 255);
        }
        return new Color32(255, 255, 255, 255);
    }

    private static XDocument LoadXml(ZipArchive zip, string path)
    {
        var entry = zip.GetEntry(path);
        using var stream = entry!.Open();
        return XDocument.Load(stream);
    }
}