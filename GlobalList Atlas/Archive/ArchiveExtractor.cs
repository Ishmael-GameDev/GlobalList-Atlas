using System;
using System.IO;
using SharpCompress.Archives;
using SharpCompress.Common;
using GlobalListAtlas.Logging;

namespace GlobalListAtlas.Archive;

// Распаковывает zip/rar/7z в указанную папку
// SharpCompress сам определяет формат архива по сигнатуре файла (ArchiveFactory.Open)
// NuGet-зависимость: SharpCompress (https://www.nuget.org/packages/SharpCompress)
public static class ArchiveExtractor
{
    public static bool ExtractToFolder(byte[] archiveBytes, string targetFolder)
    {
        try
        {
            Directory.CreateDirectory(targetFolder);

            using var memoryStream = new MemoryStream(archiveBytes);
            using var archive = ArchiveFactory.OpenArchive(memoryStream);

            Log.Info($"Формат архива определён как: {archive.Type}");

            int extractedCount = 0;
            foreach (var entry in archive.Entries)
            {
                if (entry.IsDirectory)
                    continue;

                entry.WriteToDirectory(targetFolder, new ExtractionOptions
                {
                    ExtractFullPath = true,
                    Overwrite = true
                });
                extractedCount++;
            }

            Log.Info($"Распаковано {extractedCount} файлов в: {targetFolder}");
            return true;
        }
        catch (Exception e)
        {
            Log.Error($"Ошибка распаковки архива в {targetFolder}: {e.Message}\n{e.StackTrace}");
            return false;
        }
    }
}