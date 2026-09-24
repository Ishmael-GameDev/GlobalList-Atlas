using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using GlobalListAtlas.Configuration;
using GlobalListAtlas.Logging;
using GlobalListAtlas.Utility;
using UnityEngine;
using UnityEngine.UI;

namespace GlobalListAtlas.UI;

public static class HelpPopup
{
    private const string RepositoryUrl = "https://github.com/Ishmael-GameDev/GlobalList-Atlas";
    private const string ResourceSuffix = "README.md";

    // Заголовки языковых разделов внутри README
    private const string RussianHeading = "# Русский";
    private const string EnglishHeading = "# English";

    public static GameObject Show()
    {
        UIFactory.CreateRootCanvas("HelpPopupCanvas", out var popupCanvasGo).sortingOrder = 20000;

        var overlayGo = new GameObject("HelpOverlay", typeof(RectTransform), typeof(Image));
        overlayGo.transform.SetParent(popupCanvasGo.transform, false);
        var overlayRect = (RectTransform)overlayGo.transform;
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;
        overlayGo.GetComponent<Image>().color = new Color(0, 0, 0, 0.65f);

        var overlayButton = overlayGo.AddComponent<Button>();
        overlayButton.transition = Selectable.Transition.None;
        overlayButton.onClick.AddListener(() => PopupStack.Close(popupCanvasGo));

        var card = UIFactory.CreatePanel(overlayRect, "HelpCard", new Color(0.09f, 0.09f, 0.12f, 0.98f));
        card.anchorMin = new Vector2(0.5f, 0.5f);
        card.anchorMax = new Vector2(0.5f, 0.5f);
        card.sizeDelta = new Vector2(940, 660);
        card.anchoredPosition = Vector2.zero;

        var title = UIFactory.CreateText(card, "Title", Localization.Get("help.title"), 20, TextAnchor.MiddleCenter);
        var titleRect = (RectTransform)title.transform;
        titleRect.anchorMin = new Vector2(0, 1);
        titleRect.anchorMax = new Vector2(1, 1);
        titleRect.pivot = new Vector2(0.5f, 1);
        titleRect.sizeDelta = new Vector2(0, 32);
        titleRect.anchoredPosition = new Vector2(0, -6);

        var (content, _) = UIFactory.CreateVerticalScrollList(card, "HelpScroll");
        var scrollRoot = (RectTransform)content.parent.parent;
        scrollRoot.offsetMax = new Vector2(0, -42);
        scrollRoot.offsetMin = new Vector2(0, 46);

        AddDocumentText(content, ToRichText(GetSectionForCurrentLanguage()));

        var (linkGo, linkButton, linkImage, linkText) = UIFactory.CreateButton(
            card, "OpenRepoButton", Localization.Get("help.open_github"), 15);
        linkText.alignment = TextAnchor.MiddleCenter;
        linkText.horizontalOverflow = HorizontalWrapMode.Overflow;
        // Синеватая заливка: ссылка на репозиторий не должна читаться как кнопка закрытия
        linkImage.color = new Color(0.192f, 0.310f, 0.471f, 1f);
        linkText.color = UIFactory.GetReadableTextColor(linkImage.color);
        UIFactory.AddOutline(linkGo);
        var linkRect = (RectTransform)linkGo.transform;
        linkRect.anchorMin = new Vector2(0, 0);
        linkRect.anchorMax = new Vector2(0, 0);
        linkRect.pivot = new Vector2(0, 0);
        linkRect.sizeDelta = new Vector2(UIFactory.MeasureButtonWidth(linkText, min: 200f), 32);
        linkRect.anchoredPosition = new Vector2(12, 10);
        linkButton.onClick.AddListener(() => SystemUtils.OpenUrl(RepositoryUrl));

        PopupStack.Register(popupCanvasGo);
        return popupCanvasGo;
    }

    private static void AddDocumentText(RectTransform content, string text)
    {
        var label = UIFactory.CreateText(content, "Document", text, 15, TextAnchor.UpperLeft);
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Overflow;

        LayoutRebuilder.ForceRebuildLayoutImmediate(content);

        var layout = content.GetComponent<VerticalLayoutGroup>();
        float padding = layout != null ? layout.padding.left + layout.padding.right : 0f;
        float width = Mathf.Max(content.rect.width - padding, 1f);

        var settings = label.GetGenerationSettings(new Vector2(width, 0f));
        settings.generateOutOfBounds = true;
        float height = new TextGenerator().GetPreferredHeight(text, settings);

        ((RectTransform)label.transform).sizeDelta = new Vector2(0, height + 8f);
    }

    public static string LoadReadme()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            string resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(ResourceSuffix, StringComparison.OrdinalIgnoreCase));

            if (resourceName == null)
            {
                Log.Warn("[Help] README.md не найден среди ресурсов сборки");
                return null;
            }

            using var stream = assembly.GetManifestResourceStream(resourceName);
            using var reader = new StreamReader(stream!);
            return reader.ReadToEnd();
        }
        catch (Exception e)
        {
            Log.Error($"[Help] Не удалось прочитать README.md: {e.Message}");
            return null;
        }
    }

    private static string GetSectionForCurrentLanguage()
    {
        string readme = LoadReadme();
        if (string.IsNullOrEmpty(readme))
            return Localization.Get("help.unavailable");

        bool russian = Localization.CurrentLanguage == Configuration.Language.Russian;
        string heading = russian ? RussianHeading : EnglishHeading;
        string otherHeading = russian ? EnglishHeading : RussianHeading;

        int start = readme.IndexOf(heading, StringComparison.Ordinal);
        if (start < 0) return readme;

        start += heading.Length;
        int end = readme.IndexOf(otherHeading, start, StringComparison.Ordinal);
        string section = end > start ? readme.Substring(start, end - start) : readme.Substring(start);

        return section.Trim('\r', '\n', ' ', '-');
    }

    private static string ToRichText(string markdown)
    {
        var result = new StringBuilder();

        foreach (var rawLine in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            string line = rawLine.TrimEnd();

            if (line.StartsWith("### "))
                line = $"<size=17><b>{line.Substring(4)}</b></size>";
            else if (line.StartsWith("## "))
                line = $"<size=19><b>{line.Substring(3)}</b></size>";
            else if (line.StartsWith("# "))
                line = $"<size=22><b>{line.Substring(2)}</b></size>";
            else if (line.StartsWith("- "))
                line = "   •  " + line.Substring(2);
            else if (line.Trim() == "---")
                line = "<color=#55606E>────────────</color>";

            line = Regex.Replace(line, @"\*\*(.+?)\*\*", "<b>$1</b>");
            line = Regex.Replace(line, @"`(.+?)`", "<color=#C8D2E6>$1</color>");
            line = Regex.Replace(line, @"\[(.+?)\]\((.+?)\)", "$1 ($2)");

            result.AppendLine(line);
        }

        return result.ToString();
    }
}
