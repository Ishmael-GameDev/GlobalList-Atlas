using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GlobalListAtlas.Install;

// Ищет текстовые файлы (.txt, .md) во всём дереве распакованного архива карты.
// Файл с именем "readme" (любое расширение из списка) помечается отдельно.
public static class TxtFileDetector
{
    public static readonly string[] TextExtensions = { ".txt", ".md" };

    public class Result
    {
        public List<string> FileNames = new();
        public bool HasReadmeNamed;
    }

    public static Result Scan(string extractedArchiveFolder)
    {
        var result = new Result();
        if (!Directory.Exists(extractedArchiveFolder))
            return result;

        var files = Directory.GetFiles(extractedArchiveFolder, "*", SearchOption.AllDirectories)
            .Where(f => TextExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));

        foreach (var filePath in files)
        {
            result.FileNames.Add(Path.GetFileName(filePath));

            string nameNoExt = Path.GetFileNameWithoutExtension(filePath);
            if (nameNoExt.Equals("readme", StringComparison.OrdinalIgnoreCase) ||
                nameNoExt.Equals("read me", StringComparison.OrdinalIgnoreCase) ||
                nameNoExt.Equals("read_me", StringComparison.OrdinalIgnoreCase))
            {
                result.HasReadmeNamed = true;
            }
        }

        return result;
    }
}
