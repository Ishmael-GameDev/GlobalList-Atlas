using System;
using System.Collections.Generic;
using UnityEngine;
using GlobalListAtlas.Configuration;

namespace GlobalListAtlas.Maps;

// Одна строка карты из глобаллиста
public class MapRow
{
    public string Name;
    public string DriveUrl;

   
    public List<string> RequiredPublicMods = new();

    public List<MapEditor> Editors = new();
    public string Creator;
    public List<MapTag> Tags = new();
    public int Stars;
    public bool Verified;
    public string VerifierName;
    public DateTime? VerificationDate;
    public Color32 CellColor;
    public League League;
    public int SheetRowNumber;
}