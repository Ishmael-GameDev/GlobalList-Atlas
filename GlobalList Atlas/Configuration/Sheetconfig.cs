using System.IO;
using UnityEngine;

namespace GlobalListAtlas.Configuration;

public static class SheetConfig
{
    public const string SheetId = "1M51Qyfo3-h2XVSPCQMsZZV39PNF1Kzoc1iua7UmYqtU";
    public const long Gid = 2045446359;

    // Диапазон карт: с этой строки и до первой строки, где столбец A пуст
    public const int FirstDataRow = 3;

    public const string NameColumn = "A";
    public const string CreatorColumn = "D";
    public const string LinkColumn = "E";
    public const string CategoryColumn = "F";

    public const string TagsColumn = "G";

    public const string StarsColumn = "H";

    public const string VerifiedColumn = "I";

    public const string VerifierColumn = "J";

    public const string VerificationDateColumn = "M";

    public const int MaxMapSlots = 10;

    public static string GetMapsRootFolder()
    {
        return Path.Combine(Application.persistentDataPath, "GlobalistMaps");
    }
}