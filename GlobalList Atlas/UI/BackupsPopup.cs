using System;
using System.Collections.Generic;
using GlobalListAtlas.Configuration;
using GlobalListAtlas.Install;
using UnityEngine;
using UnityEngine.UI;

namespace GlobalListAtlas.UI;

public static class BackupsPopup
{
    private const int KeepNewest = 3;

    public static GameObject Show(Transform canvasParent, Action onChanged)
    {
        // Своё полотно поверх обеих панелей, как у плашек фильтра
        UIFactory.CreateRootCanvas("BackupsPopupCanvas", out var popupCanvasGo).sortingOrder = 20000;

        var overlayGo = new GameObject("BackupsOverlay", typeof(RectTransform), typeof(Image));
        overlayGo.transform.SetParent(popupCanvasGo.transform, false);
        var overlayRect = (RectTransform)overlayGo.transform;
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;
        overlayGo.GetComponent<Image>().color = new Color(0, 0, 0, 0.6f);

        var overlayButton = overlayGo.AddComponent<Button>();
        overlayButton.transition = Selectable.Transition.None;
        overlayButton.onClick.AddListener(() => PopupStack.Close(popupCanvasGo));

        var card = UIFactory.CreatePanel(overlayRect, "BackupsCard", new Color(0.1f, 0.1f, 0.13f, 0.98f));
        card.anchorMin = new Vector2(0.5f, 0.5f);
        card.anchorMax = new Vector2(0.5f, 0.5f);
        card.sizeDelta = new Vector2(700, 520);
        card.anchoredPosition = Vector2.zero;

        var title = UIFactory.CreateText(card, "Title", Localization.Get("backups.title"), 20, TextAnchor.MiddleCenter);
        var titleRect = (RectTransform)title.transform;
        titleRect.anchorMin = new Vector2(0, 1);
        titleRect.anchorMax = new Vector2(1, 1);
        titleRect.pivot = new Vector2(0.5f, 1);
        titleRect.sizeDelta = new Vector2(0, 32);
        titleRect.anchoredPosition = new Vector2(0, -6);

        var (content, _) = UIFactory.CreateVerticalScrollList(card, "BackupsScroll");
        var scrollRoot = (RectTransform)content.parent.parent;
        scrollRoot.offsetMax = new Vector2(0, -42);
        scrollRoot.offsetMin = new Vector2(0, 46);

        Rebuild(content, overlayGo, onChanged);

        // Общая очистка: оставить только несколько свежих бекапов на каждый редактор
        var (cleanupGo, cleanupButton, cleanupImage, cleanupText) = UIFactory.CreateButton(
            card, "CleanupButton", Localization.Get("backups.cleanup", KeepNewest), 16);
        var cleanupRect = (RectTransform)cleanupGo.transform;
        cleanupRect.anchorMin = new Vector2(0.5f, 0);
        cleanupRect.anchorMax = new Vector2(0.5f, 0);
        cleanupRect.pivot = new Vector2(0.5f, 0);
        cleanupRect.sizeDelta = new Vector2(UIFactory.MeasureButtonWidth(cleanupText), 32);
        UIFactory.AddOutline(cleanupGo);
        cleanupRect.anchoredPosition = new Vector2(0, 6);
        cleanupText.alignment = TextAnchor.MiddleCenter;
        cleanupButton.onClick.AddListener(() =>
        {
            BackupManager.DeleteOldKeepingNewest(KeepNewest);
            Rebuild(content, overlayGo, onChanged);
            onChanged?.Invoke();
        });

        PopupStack.Register(popupCanvasGo);
        return popupCanvasGo;
    }

    private static void Rebuild(RectTransform content, GameObject overlayGo, Action onChanged)
    {
        foreach (Transform child in content)
            UnityEngine.Object.Destroy(child.gameObject);

        var backups = BackupManager.ListAll();

        if (backups.Count == 0)
        {
            AddLine(content, Localization.Get("backups.empty"), 16, new Color(0.7f, 0.7f, 0.7f), 30);
            return;
        }

        foreach (var backup in backups)
        {
            string created = backup.Created?.ToString("dd.MM.yyyy HH:mm") ?? backup.FolderName;
            string size = FormatBytes(backup.TotalBytes);
            string editorColor = ColorUtility.ToHtmlStringRGB(EditorConfig.GetColor(backup.Editor));
            AddLine(content,
                $"<color=#{editorColor}>{EditorConfig.GetLabel(backup.Editor)}</color>  —  {created}  ({backup.FileCount} / {size})",
                15, Color.white, 24);

            var row = new GameObject("Buttons", typeof(RectTransform));
            row.transform.SetParent(content, false);
            ((RectTransform)row.transform).sizeDelta = new Vector2(0, 28);

            var captured = backup;
            AddRowButton(row, Localization.Get("backups.restore"), 0f, () =>
            {
                BackupManager.Restore(captured);
                Rebuild(content, overlayGo, onChanged);
                onChanged?.Invoke();
            });
            AddRowButton(row, Localization.Get("backups.delete"), 150f, () =>
            {
                BackupManager.Delete(captured);
                Rebuild(content, overlayGo, onChanged);
                onChanged?.Invoke();
            });
        }
    }

    private static void AddRowButton(GameObject row, string label, float x, Action onClick)
    {
        var (go, button, image, text) = UIFactory.CreateButton(row.transform, $"Btn_{label}", label, 14);
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        UIFactory.AddOutline(go);

        var rect = (RectTransform)go.transform;
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.sizeDelta = new Vector2(UIFactory.MeasureButtonWidth(text, min: 110f), 0f);
        rect.anchoredPosition = new Vector2(x, 0f);

        button.onClick.AddListener(() => onClick());
    }

    private static void AddLine(RectTransform parent, string text, int fontSize, Color color, float height)
    {
        var t = UIFactory.CreateText(parent, "Line", text, fontSize, TextAnchor.MiddleLeft);
        t.color = color;
        ((RectTransform)t.transform).sizeDelta = new Vector2(0, height);
    }

    private static string FormatBytes(long bytes)
    {
        double mb = bytes / (1024.0 * 1024.0);
        return mb >= 1 ? $"{mb:F1} МБ" : $"{bytes / 1024.0:F0} КБ";
    }
}
