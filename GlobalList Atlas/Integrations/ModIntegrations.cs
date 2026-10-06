using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GlobalListAtlas.Install;
using Modding;
using UnityEngine;

namespace GlobalListAtlas.Integrations;

public class MapPresetRef
{
    public string IntegrationId;
    public string Url;

    public string FileName;
}

public enum IntegrationAvailability
{
    Loaded,
    Installed,
    NotInstalled
}

public interface IModIntegration
{
    string Id { get; }
    string DisplayName { get; }

    string ModLinksName { get; }

    string PresetExtension { get; }

    string ActiveFolder { get; }

    string DisabledFolder { get; }

    bool IsExclusive { get; }

    bool IsAutoManaged { get; }

    IntegrationAvailability GetAvailability();
}

public class TeleportMasterIntegration : IModIntegration
{
    public const string IntegrationId = "TeleportMaster";

    public string Id => IntegrationId;
    public string DisplayName => "Teleport Master";
    public string ModLinksName => "Teleport Master";
    public string PresetExtension => ".pallet.json";

    public bool IsExclusive => true;
    public bool IsAutoManaged => true;

    public string ActiveFolder => Path.Combine(Application.persistentDataPath, "TeleportMaster Palettes");
    public string DisabledFolder => Path.Combine(ActiveFolder, "disabled");

    private static readonly string[] LoadedNames = { "TeleportMaster", "Teleport Master" };

    public IntegrationAvailability GetAvailability()
    {
        foreach (var name in LoadedNames)
        {
            try
            {
                if (ModHooks.GetMod(name, onlyEnabled: false, allowLoadError: false) != null)
                    return IntegrationAvailability.Loaded;
            }
            catch {  }
        }

        return RequiredModsChecker.IsModInstalled(ModLinksName)
            ? IntegrationAvailability.Installed
            : IntegrationAvailability.NotInstalled;
    }
}

public static class IntegrationRegistry
{
    public static readonly IReadOnlyList<IModIntegration> All = new List<IModIntegration>
    {
        new TeleportMasterIntegration()
    };

    public static IModIntegration Find(string id) =>
        All.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
}
