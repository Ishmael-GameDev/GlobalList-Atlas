using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GlobalListAtlas.Logging;
using Newtonsoft.Json;

namespace GlobalListAtlas.Configuration;

public enum Language
{
    English,
    Russian
}

// Строки интерфейса хранятся в JSON-ресурсах (Resources/Localization/<код>.json)
public static class Localization
{
    private const string FallbackCode = "en";
    private const string ResourceSuffix = ".Localization.{0}.json";
    private const string OverrideFolderName = "Localization";

    public static Language CurrentLanguage { get; private set; } = Language.English;

    // код языка -> (ключ -> строка)
    private static readonly Dictionary<string, Dictionary<string, string>> Tables = new();

    private static string CodeOf(Language lang) => lang switch
    {
        Language.Russian => "ru",
        _ => "en"
    };

    public static void Initialize()
    {
        if (Tables.Count > 0) return;

        foreach (Language lang in Enum.GetValues(typeof(Language)))
        {
            string code = CodeOf(lang);
            var table = LoadEmbedded(code);
            MergeOverride(table, code);
            Tables[code] = table;
        }

        Log.Info($"[Localization] Загружено языков: {Tables.Count}, ключей (en): " +
                 $"{(Tables.TryGetValue(FallbackCode, out var en) ? en.Count : 0)}");
    }

    private static Dictionary<string, string> LoadEmbedded(string code)
    {
        var result = new Dictionary<string, string>();
        var assembly = Assembly.GetExecutingAssembly();
        string suffix = string.Format(ResourceSuffix, code);

        string resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

        if (resourceName == null)
        {
            Log.Warn($"[Localization] Встроенный ресурс для языка '{code}' не найден");
            return result;
        }

        try
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            using var reader = new StreamReader(stream!);
            var parsed = JsonConvert.DeserializeObject<Dictionary<string, string>>(reader.ReadToEnd());
            if (parsed != null)
                foreach (var kvp in parsed)
                    result[kvp.Key] = kvp.Value;
        }
        catch (Exception e)
        {
            Log.Error($"[Localization] Не удалось прочитать ресурс '{resourceName}'", e);
        }

        return result;
    }

    private static void MergeOverride(Dictionary<string, string> table, string code)
    {
        try
        {
            string dllDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (string.IsNullOrEmpty(dllDir)) return;

            string path = Path.Combine(dllDir, OverrideFolderName, code + ".json");
            if (!File.Exists(path)) return;

            var parsed = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path));
            if (parsed == null) return;

            foreach (var kvp in parsed)
                table[kvp.Key] = kvp.Value;

            Log.Info($"[Localization] Применены пользовательские строки из {path} ({parsed.Count} шт.)");
        }
        catch (Exception e)
        {
            Log.Warn($"[Localization] Не удалось применить пользовательский файл для '{code}': {e.Message}");
        }
    }

    public static string Get(string key)
    {
        if (Tables.TryGetValue(CodeOf(CurrentLanguage), out var table) && table.TryGetValue(key, out var text))
            return text;

        if (Tables.TryGetValue(FallbackCode, out var fallback) && fallback.TryGetValue(key, out var en))
            return en;

        // Перевод отсутствует — возвращаем ключ, чтобы пропуск был заметен в UI
        return key;
    }

    public static string Get(string key, params object[] args)
    {
        string template = Get(key);
        try { return string.Format(template, args); }
        catch { return template; }
    }

    public static void SetLanguage(Language lang)
    {
        CurrentLanguage = lang;
    }
}
