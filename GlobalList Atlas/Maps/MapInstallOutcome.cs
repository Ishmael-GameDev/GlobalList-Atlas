using System.Collections.Generic;
using GlobalListAtlas.Configuration;

namespace GlobalListAtlas.Maps;

//Результат скачивания и установки файлов карты (json), для отображения в MapDetailsPanel
public class MapInstallOutcome
{
    public bool Success;
    public string ErrorMessage;
    public string TargetFolder;

    public List<string> TxtFileNames = new();

    public bool HasReadmeNamedTxt;

    public List<MapEditor> MissingEditors = new();

    public long FileSizeBytes;

    // Певышает 10 МБ
    public bool IsLargeFile => FileSizeBytes > 10 * 1024 * 1024;
}