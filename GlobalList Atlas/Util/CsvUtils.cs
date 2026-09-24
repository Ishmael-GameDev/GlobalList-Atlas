using System.Text;

namespace GlobalListAtlas.Util;

// Минимальный CSV-парсер для точечного чтения одной ячейки
public static class CsvUtils
{
    public static string GetCellAt(string csvText, int oneBasedRow, int zeroBasedColumn)
    {
        if (string.IsNullOrEmpty(csvText))
            return null;

        int row = 1;
        int col = 0;
        var field = new StringBuilder();
        bool inQuotes = false;
        int i = 0;
        int len = csvText.Length;

        while (i < len)
        {
            char c = csvText[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < len && csvText[i + 1] == '"')
                    {
                        field.Append('"');
                        i += 2;
                        continue;
                    }
                    inQuotes = false;
                    i++;
                    continue;
                }
                field.Append(c);
                i++;
                continue;
            }

            if (c == '"')
            {
                inQuotes = true;
                i++;
                continue;
            }

            if (c == ',')
            {
                if (row == oneBasedRow && col == zeroBasedColumn)
                    return field.ToString();
                field.Clear();
                col++;
                i++;
                continue;
            }

            if (c == '\r')
            {
                i++;
                continue;
            }

            if (c == '\n')
            {
                if (row == oneBasedRow && col == zeroBasedColumn)
                    return field.ToString();
                if (row == oneBasedRow)
                    return null; // строка закончилась раньше, чем дошли до нужной колонки

                field.Clear();
                col = 0;
                row++;
                i++;
                continue;
            }

            field.Append(c);
            i++;
        }

        // последняя ячейка без завершающего \n
        if (row == oneBasedRow && col == zeroBasedColumn)
            return field.ToString();

        return null;
    }
}