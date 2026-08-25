using System;
using System.Collections.Generic;
using System.IO;

namespace GlobalListAtlas.Install;

// Ищет .txt файлы во всем дереве распакованного архива карты
public static class TxtFileDetector
{
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

        foreach (var filePath in Directory.GetFiles(extractedArchiveFolder, "*.txt", SearchOption.AllDirectories))
        {
            string fileName = Path.GetFileName(filePath);
            result.FileNames.Add(fileName);

            string nameNoExt = Path.GetFileNameWithoutExtension(filePath);
            if (nameNoExt.Equals("readme", StringComparison.OrdinalIgnoreCase) ||
                nameNoExt.Equals("read me", StringComparison.OrdinalIgnoreCase))
            {
                result.HasReadmeNamed = true;
            }
        }

        return result;
    }
}