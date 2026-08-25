using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using UnityEngine;
using GlobalListAtlas.Configuration;
using GlobalListAtlas.Install;
using GlobalListAtlas.Logging;
using GlobalListAtlas.Maps;

namespace GlobalListAtlas.Sheets;

public static class MapSheetParser
{
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PkgRel = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static List<MapRow> Parse(byte[] xlsxBytes, long gid, string fingerprint)
    {
        var result = new List<MapRow>();

        using var zip = new ZipArchive(new MemoryStream(xlsxBytes), ZipArchiveMode.Read);

        var workbook = LoadXml(zip, "xl/workbook.xml");
        var workbookRels = LoadXml(zip, "xl/_rels/workbook.xml.rels");

        var relTargets = workbookRels.Root!
            .Elements(PkgRel + "Relationship")
            .ToDictionary(e => e.Attribute("Id")!.Value, e => e.Attribute("Target")!.Value);

        var sheetElements = workbook.Root!.Element(Main + "sheets")!.Elements(Main + "sheet").ToList();
        var sharedStrings = LoadSharedStrings(zip);
        var styleIndexToColor = XlsxStyleReader.LoadStyleIndexToFillColor(zip);

        XElement matchedSheet = null;

        if (!string.IsNullOrWhiteSpace(fingerprint))
        {
            matchedSheet = FindSheetByFingerprint(zip, relTargets, sheetElements, sharedStrings, fingerprint);
        }
        else
        {
            Log.Warn("CSV-отпечаток не получен (пустой ответ?) — поиск листа по содержимому пропущен.");
        }

        if (matchedSheet == null)
        {
            matchedSheet = sheetElements.FirstOrDefault(s => s.Attribute("sheetId")?.Value == gid.ToString());
            if (matchedSheet != null)
                Log.Warn($"Лист определён по совпадению числового sheetId={gid} — это ненадёжный признак, проверьте результат.");
        }

        if (matchedSheet == null)
        {
            Log.Warn($"Не удалось определить лист ни по CSV-отпечатку, ни по sheetId={gid} — беру первый лист книги. " +
                     "Если это не тот лист — проверьте gid и FirstDataRow в SheetConfig.");
            matchedSheet = sheetElements.First();
        }
        else
        {
            Log.Info($"Используется лист '{matchedSheet.Attribute("name")?.Value}' (gid={gid})");
        }

        string rId = matchedSheet.Attribute(R + "id")!.Value;
        string sheetTarget = "xl/" + relTargets[rId];

        var sheetXml = LoadXml(zip, sheetTarget);
        var hyperlinks = LoadHyperlinks(zip, sheetXml, sheetTarget);

        var sheetData = sheetXml.Root!.Element(Main + "sheetData");
        if (sheetData == null)
        {
            Log.Error("В листе не найден sheetData — таблица пустая или повреждён экспорт");
            return result;
        }

        var rowsByNumber = sheetData.Elements(Main + "row")
            .Where(r => int.TryParse(r.Attribute("r")?.Value, out _))
            .ToDictionary(r => int.Parse(r.Attribute("r")!.Value), r => r);

        int rowNumber = SheetConfig.FirstDataRow;
        while (true)
        {
            if (!rowsByNumber.TryGetValue(rowNumber, out var row))
                break;

            var cellsByColumn = new Dictionary<string, string>();
            int? colorStyleIndex = null;

            foreach (var cell in row.Elements(Main + "c"))
            {
                var cellRef = cell.Attribute("r")?.Value;
                if (cellRef == null) continue;

                var column = new string(cellRef.TakeWhile(char.IsLetter).ToArray());
                cellsByColumn[column] = GetCellValue(cell, sharedStrings);

                if (column == LeagueConfig.ColorSourceColumn)
                {
                    colorStyleIndex = int.TryParse(cell.Attribute("s")?.Value, out int s) ? s : 0;
                }
            }

            string name = cellsByColumn.TryGetValue(SheetConfig.NameColumn, out var nameVal) ? nameVal.Trim() : "";
            if (string.IsNullOrWhiteSpace(name))
                break;

            string editorsRaw = cellsByColumn.TryGetValue(SheetConfig.CategoryColumn, out var catVal) ? catVal : "";
            var editors = EditorConfig.ParseEditors(editorsRaw);
            string starsRaw = cellsByColumn.TryGetValue(SheetConfig.StarsColumn, out var starsVal) ? starsVal : "";
            int stars = Math.Min(starsRaw.Count(c => c == '⭐'), 5);

            string linkCellRef = $"{SheetConfig.LinkColumn}{rowNumber}";
            hyperlinks.TryGetValue(linkCellRef, out var cellHyperlinks);

            string driveUrl = ExtractUrlContaining(cellHyperlinks, "drive.google.com");
            string gearLink = ExtractUrlContaining(cellHyperlinks, "lumafly");
            var requiredMods = LumaflyLinkParser.ExtractRequiredMods(gearLink);

            if (requiredMods.Count == 0)
            {
                var manualMods = ManualModRequirementsOverride.TryGetRequiredMods(name);
                if (manualMods != null && manualMods.Count > 0)
                {
                    requiredMods = manualMods;
                    Log.Info($"Строка {rowNumber} ('{name}'): список модов взят из ручного оверрайда " +
                             "(ManualModRequirementsOverride) — ссылка ⚙️ не читается из xlsx из-за коллизии с 📂 в одной ячейке.");
                }
            }

            string linkCellRefUnused = linkCellRef; // (оставлено для читаемости диагностики выше)

            string creator = cellsByColumn.TryGetValue(SheetConfig.CreatorColumn, out var creatorVal) ? creatorVal.Trim() : "";

            string tagsRaw = cellsByColumn.TryGetValue(SheetConfig.TagsColumn, out var tagsVal) ? tagsVal : "";
            var tags = TagConfig.ParseTags(tagsRaw);

            string verifiedRaw = cellsByColumn.TryGetValue(SheetConfig.VerifiedColumn, out var verifiedVal) ? verifiedVal : "";
            bool verified = ParseCheckbox(verifiedRaw);

            string verifierName = cellsByColumn.TryGetValue(SheetConfig.VerifierColumn, out var verifierVal) ? verifierVal.Trim() : "";

            string verificationDateRaw = cellsByColumn.TryGetValue(SheetConfig.VerificationDateColumn, out var dateVal) ? dateVal : "";
            DateTime? verificationDate = ParseSheetDate(verificationDateRaw);

            Color32? cellColor = null;
            if (colorStyleIndex.HasValue && styleIndexToColor.TryGetValue(colorStyleIndex.Value, out var resolved))
                cellColor = resolved;

            result.Add(new MapRow
            {
                Name = name,
                Editors = editors,
                DriveUrl = driveUrl,
                RequiredPublicMods = requiredMods,
                Creator = creator,
                Tags = tags,
                Stars = stars,
                Verified = verified,
                VerifierName = verifierName,
                VerificationDate = verificationDate,
                CellColor = cellColor ?? new Color32(255, 255, 255, 255),
                League = LeagueConfig.ClassifyColor(cellColor),
                SheetRowNumber = rowNumber
            });

            rowNumber++;
        }

        Log.Info($"Прочитано {result.Count} строк карт из таблицы (диапазон {SheetConfig.FirstDataRow}..{rowNumber - 1})");
        return result;
    }

    private static string ExtractUrlContaining(List<string> urls, string substring)
    {
        return urls?.FirstOrDefault(u => u.IndexOf(substring, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static XElement FindSheetByFingerprint(
        ZipArchive zip,
        Dictionary<string, string> relTargets,
        List<XElement> sheetElements,
        List<string> sharedStrings,
        string fingerprint)
    {
        string trimmedFingerprint = fingerprint.Trim();

        foreach (var sheet in sheetElements)
        {
            string rId = sheet.Attribute(R + "id")?.Value;
            if (rId == null || !relTargets.TryGetValue(rId, out var target))
                continue;

            string sheetPath = "xl/" + target;
            if (zip.GetEntry(sheetPath) == null)
                continue;

            var sheetXml = LoadXml(zip, sheetPath);
            var sheetData = sheetXml.Root!.Element(Main + "sheetData");
            if (sheetData == null)
                continue;

            string value = GetCellText(sheetData, SheetConfig.FirstDataRow, SheetConfig.NameColumn, sharedStrings)?.Trim();

            if (value != null && value == trimmedFingerprint)
            {
                Log.Info($"Лист определён по CSV-отпечатку строки {SheetConfig.FirstDataRow}: '{sheet.Attribute("name")?.Value}'");
                return sheet;
            }
        }

        Log.Warn($"Ни один лист не содержит в A{SheetConfig.FirstDataRow} значение отпечатка '{trimmedFingerprint}'.");
        return null;
    }

    private static string GetCellText(XElement sheetData, int rowNumber, string column, List<string> sharedStrings)
    {
        var row = sheetData.Elements(Main + "row")
            .FirstOrDefault(r => r.Attribute("r")?.Value == rowNumber.ToString());
        if (row == null) return null;

        string wantedRef = $"{column}{rowNumber}";
        var cell = row.Elements(Main + "c")
            .FirstOrDefault(c => c.Attribute("r")?.Value == wantedRef);
        if (cell == null) return null;

        return GetCellValue(cell, sharedStrings);
    }

    private static List<string> LoadSharedStrings(ZipArchive zip)
    {
        var sharedStrings = new List<string>();
        if (zip.GetEntry("xl/sharedStrings.xml") == null)
            return sharedStrings;

        var sstXml = LoadXml(zip, "xl/sharedStrings.xml");
        foreach (var si in sstXml.Root!.Elements(Main + "si"))
        {
            var tDirect = si.Element(Main + "t");
            if (tDirect != null)
            {
                sharedStrings.Add(tDirect.Value);
                continue;
            }

            var runs = si.Elements(Main + "r").Select(r => r.Element(Main + "t")?.Value ?? "");
            sharedStrings.Add(string.Concat(runs));
        }

        return sharedStrings;
    }

    // Возвращает все гиперссылки на ячейку
    private static Dictionary<string, List<string>> LoadHyperlinks(ZipArchive zip, XDocument sheetXml, string sheetTarget)
    {
        var hyperlinks = new Dictionary<string, List<string>>();

        var hyperlinksElement = sheetXml.Root!.Element(Main + "hyperlinks");
        if (hyperlinksElement == null)
            return hyperlinks;

        string sheetDir = Path.GetDirectoryName(sheetTarget)!.Replace('\\', '/');
        string sheetFileName = Path.GetFileName(sheetTarget);
        string sheetRelsPath = $"{sheetDir}/_rels/{sheetFileName}.rels";

        if (zip.GetEntry(sheetRelsPath) == null)
        {
            Log.Warn($"Не найден файл связей для листа ({sheetRelsPath}) — гиперссылки читать не из чего");
            return hyperlinks;
        }

        var sheetRelsXml = LoadXml(zip, sheetRelsPath);
        var sheetRelTargets = sheetRelsXml.Root!
            .Elements(PkgRel + "Relationship")
            .ToDictionary(e => e.Attribute("Id")!.Value, e => e.Attribute("Target")!.Value);

        int totalUrls = 0;
        foreach (var link in hyperlinksElement.Elements(Main + "hyperlink"))
        {
            var cellRef = link.Attribute("ref")?.Value;
            var linkRId = link.Attribute(R + "id")?.Value;
            if (cellRef == null || linkRId == null) continue;

            if (!sheetRelTargets.TryGetValue(linkRId, out var url))
                continue;

            if (!hyperlinks.TryGetValue(cellRef, out var list))
            {
                list = new List<string>();
                hyperlinks[cellRef] = list;
            }
            list.Add(url);
            totalUrls++;
        }

        Log.Info($"Найдено {totalUrls} гиперссылок на листе ({hyperlinks.Count} уникальных ячеек)");
        return hyperlinks;
    }

    private static bool ParseCheckbox(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        raw = raw.Trim();

        if (raw == "1") return true;
        if (raw == "0") return false;

        if (bool.TryParse(raw, out bool parsed))
            return parsed;

        if (raw.Equals("TRUE", StringComparison.OrdinalIgnoreCase) || raw.Equals("ИСТИНА", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static DateTime? ParseSheetDate(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        raw = raw.Trim();

        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double serial))
        {
            try
            {
                return DateTime.FromOADate(serial);
            }
            catch (Exception)
            {
                Log.Warn($"Не удалось преобразовать дату верификации (serial='{raw}') в DateTime.");
                return null;
            }
        }

        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return dt;

        Log.Warn($"Не удалось распознать дату верификации: '{raw}'.");
        return null;
    }

    private static string GetCellValue(XElement cell, List<string> sharedStrings)
    {
        var type = cell.Attribute("t")?.Value;

        if (type == "s")
        {
            var v = cell.Element(Main + "v")?.Value;
            if (v != null && int.TryParse(v, out int idx) && idx >= 0 && idx < sharedStrings.Count)
                return sharedStrings[idx];
            return "";
        }

        if (type == "inlineStr")
            return cell.Element(Main + "is")?.Element(Main + "t")?.Value ?? "";

        return cell.Element(Main + "v")?.Value ?? "";
    }

    private static XDocument LoadXml(ZipArchive zip, string path)
    {
        var entry = zip.GetEntry(path);
        if (entry == null)
            throw new FileNotFoundException($"Не найден файл внутри xlsx: {path}");

        using var stream = entry.Open();
        return XDocument.Load(stream);
    }
}