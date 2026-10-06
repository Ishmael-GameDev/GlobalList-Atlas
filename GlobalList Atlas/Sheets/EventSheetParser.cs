using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using GlobalListAtlas.Configuration;
using GlobalListAtlas.Install;
using GlobalListAtlas.Integrations;
using GlobalListAtlas.Logging;
using GlobalListAtlas.Maps;

namespace GlobalListAtlas.Sheets;

public static class EventSheetParser
{
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PkgRel = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static List<MapRow> Parse(byte[] xlsxBytes)
    {
        var result = new List<MapRow>();

        using var zip = new ZipArchive(new MemoryStream(xlsxBytes), ZipArchiveMode.Read);

        var workbook = LoadXml(zip, "xl/workbook.xml");
        var workbookRels = LoadXml(zip, "xl/_rels/workbook.xml.rels");
        var relTargets = workbookRels.Root!
            .Elements(PkgRel + "Relationship")
            .ToDictionary(e => e.Attribute("Id")!.Value, e => e.Attribute("Target")!.Value);

        var sheet = workbook.Root!.Element(Main + "sheets")!.Elements(Main + "sheet")
            .FirstOrDefault(s => string.Equals(s.Attribute("name")?.Value, EventCatalogConfig.MapsSheetName,
                StringComparison.OrdinalIgnoreCase));

        if (sheet == null)
        {
            Log.Error($"[EventSheet] В таблице нет листа '{EventCatalogConfig.MapsSheetName}'");
            return result;
        }

        string sheetPath = NormalizeSheetPath(relTargets[sheet.Attribute(R + "id")!.Value]);
        var sharedStrings = LoadSharedStrings(zip);
        var sheetXml = LoadXml(zip, sheetPath);
        var hyperlinks = LoadHyperlinks(zip, sheetXml, sheetPath);

        var sheetData = sheetXml.Root!.Element(Main + "sheetData");
        if (sheetData == null) return result;

        var rowsByNumber = sheetData.Elements(Main + "row")
            .Where(r => int.TryParse(r.Attribute("r")?.Value, out _))
            .ToDictionary(r => int.Parse(r.Attribute("r")!.Value), r => r);

        for (int rowNumber = EventCatalogConfig.FirstDataRow; rowsByNumber.TryGetValue(rowNumber, out var row); rowNumber++)
        {
            var cells = new Dictionary<string, string>();
            foreach (var cell in row.Elements(Main + "c"))
            {
                string cellRef = cell.Attribute("r")?.Value;
                if (cellRef == null) continue;
                string column = new string(cellRef.TakeWhile(char.IsLetter).ToArray());
                cells[column] = GetCellValue(cell, sharedStrings);
            }

            string Text(string col) => cells.TryGetValue(col, out var v) ? v?.Trim() ?? "" : "";
            string Link(string col) => ResolveLink(Text(col), hyperlinks, $"{col}{rowNumber}");

            string name = Text(EventCatalogConfig.NameColumn);
            if (string.IsNullOrWhiteSpace(name))
                break;

            var type = EventCatalogConfig.ParseType(Text(EventCatalogConfig.TypeColumn));
            string fileLink = Link(EventCatalogConfig.FileColumn);

            var presets = new List<MapPresetRef>();
            string tmLink = Link(EventCatalogConfig.TeleportMasterPresetColumn);
            if (tmLink != null)
            {
                presets.Add(new MapPresetRef
                {
                    IntegrationId = TeleportMasterIntegration.IntegrationId,
                    Url = tmLink,
                    FileName = Text(EventCatalogConfig.TeleportMasterPresetColumn)
                });
            }

            result.Add(new MapRow
            {
                Catalog = MapCatalogKind.EventCommunity,
                Name = name,
                Creator = Text(EventCatalogConfig.AuthorColumn),
                Editors = EditorConfig.ParseEditors(Text(EventCatalogConfig.EditorColumn)),
                EventType = type,
                Description = Text(EventCatalogConfig.DescriptionColumn),
                DriveUrl = fileLink,
                SourceFileName = fileLink != null ? Text(EventCatalogConfig.FileColumn) : null,
                PreviewUrl = Link(EventCatalogConfig.PreviewColumn),
                RulesUrl = Link(EventCatalogConfig.RulesColumn),
                RequiredPublicMods = LumaflyLinkParser.ExtractRequiredMods(Link(EventCatalogConfig.ModsColumn)),
                CellColor = EventCatalogConfig.GetTypeColor(type),
                League = League.Unknown,
                SheetRowNumber = rowNumber,
                Presets = presets
            });
        }

        Log.Info($"[EventSheet] Прочитано карт: {result.Count}");
        return result;
    }

    private static string ResolveLink(string text, Dictionary<string, string> hyperlinks, string cellRef)
    {
        if (hyperlinks.TryGetValue(cellRef, out var url) && !string.IsNullOrWhiteSpace(url))
            return url.Trim();

        if (!string.IsNullOrWhiteSpace(text) &&
            (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
             text.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            return text.Trim();

        return null;
    }

    private static string NormalizeSheetPath(string target)
    {
        target = target.TrimStart('/');
        return target.StartsWith("xl/", StringComparison.OrdinalIgnoreCase) ? target : "xl/" + target;
    }

    private static Dictionary<string, string> LoadHyperlinks(ZipArchive zip, XDocument sheetXml, string sheetPath)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var hyperlinksEl = sheetXml.Root!.Element(Main + "hyperlinks");
        if (hyperlinksEl == null) return result;

        string relsPath = $"{Path.GetDirectoryName(sheetPath)!.Replace('\\', '/')}/_rels/{Path.GetFileName(sheetPath)}.rels";
        var relTargets = zip.GetEntry(relsPath) == null
            ? new Dictionary<string, string>()
            : LoadXml(zip, relsPath).Root!.Elements(PkgRel + "Relationship")
                .ToDictionary(e => e.Attribute("Id")!.Value, e => e.Attribute("Target")!.Value);

        foreach (var link in hyperlinksEl.Elements(Main + "hyperlink"))
        {
            string cellRef = link.Attribute("ref")?.Value;
            string rId = link.Attribute(R + "id")?.Value;
            if (cellRef == null || rId == null) continue;

            if (relTargets.TryGetValue(rId, out var url))
                result[cellRef] = url;
        }

        return result;
    }

    private static List<string> LoadSharedStrings(ZipArchive zip)
    {
        var result = new List<string>();
        if (zip.GetEntry("xl/sharedStrings.xml") == null) return result;

        foreach (var si in LoadXml(zip, "xl/sharedStrings.xml").Root!.Elements(Main + "si"))
        {
            var direct = si.Element(Main + "t");
            result.Add(direct != null
                ? direct.Value
                : string.Concat(si.Elements(Main + "r").Select(r => r.Element(Main + "t")?.Value ?? "")));
        }

        return result;
    }

    private static string GetCellValue(XElement cell, List<string> sharedStrings)
    {
        string type = cell.Attribute("t")?.Value;

        if (type == "s")
        {
            string v = cell.Element(Main + "v")?.Value;
            return v != null && int.TryParse(v, out int idx) && idx >= 0 && idx < sharedStrings.Count
                ? sharedStrings[idx]
                : "";
        }

        if (type == "inlineStr")
            return string.Concat(cell.Element(Main + "is")?.Descendants(Main + "t").Select(t => t.Value) ?? Array.Empty<string>());

        return cell.Element(Main + "v")?.Value ?? "";
    }

    private static XDocument LoadXml(ZipArchive zip, string path)
    {
        var entry = zip.GetEntry(path) ?? throw new FileNotFoundException($"Не найден файл внутри xlsx: {path}");
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }
}
