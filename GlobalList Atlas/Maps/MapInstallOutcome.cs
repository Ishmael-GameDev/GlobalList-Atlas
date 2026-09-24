using System.Collections.Generic;
using GlobalListAtlas.Configuration;

namespace GlobalListAtlas.Maps;

public class MapInstallOutcome
{
    public bool Success;
    public string ErrorMessage;
    public string TargetFolder;

    public List<string> TxtFileNames = new();

    public bool HasReadmeNamedTxt;

    public List<MapEditor> MissingEditors = new();

    public long FileSizeBytes;

    public bool IsLargeFile => FileSizeBytes > 10 * 1024 * 1024;
}