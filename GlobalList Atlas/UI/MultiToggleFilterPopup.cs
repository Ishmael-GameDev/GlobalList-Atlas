using System;
using System.Collections.Generic;
using System.Linq;
using GlobalListAtlas.Configuration;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GlobalListAtlas.UI;

// Один переключаемый пункт фильтра
public class FilterOption
{
    public string Label;
    public bool IsOn;
    public object Value;

    public Color32? AccentColor;
}


// Универсальная плашка фильтра
public static class MultiToggleFilterPopup
{
    private const float CheckboxSize = 18f;
    private const float CheckboxLeftPadding = 10f;
    private const float CheckboxToLabelGap = 8f;

    private static readonly Color CheckboxBoxColor = new(1f, 1f, 1f, 0.92f);
    private static readonly Color CheckboxMarkColor = new(0.05f, 0.05f, 0.05f, 1f);

    public static GameObject Show(
    Transform canvasParent,
    RectTransform anchorNear,
    string title,
    List<FilterOption> options,
    bool showAllIsOn,
    Action onShowAllSelected,
    Action<FilterOption> onOptionToggled)
    {
        // Добавляем Canvas и GraphicRaycaster, чтобы вынести попап на самый верхний слой отрисовки
        var overlayGo = new GameObject("FilterPopupOverlay", typeof(RectTransform), typeof(Image), typeof(Canvas), typeof(GraphicRaycaster));
        overlayGo.transform.SetParent(canvasParent, false);

        var popupCanvas = overlayGo.GetComponent<Canvas>();
        popupCanvas.overrideSorting = true;
        popupCanvas.sortingOrder = 12000; // Поверх MapListPanel, MapDetailsPanel и остальных интерфейсов

        var overlayRect = (RectTransform)overlayGo.transform;
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;

        var overlayImage = overlayGo.GetComponent<Image>();
        overlayImage.color = new Color(0, 0, 0, 0.55f);

        var overlayButton = overlayGo.AddComponent<Button>();
        overlayButton.transition = Selectable.Transition.None;
        overlayButton.onClick.AddListener(() => UnityEngine.Object.Destroy(overlayGo));

        var card = UIFactory.CreatePanel(overlayRect, "FilterCard", new Color(0.1f, 0.1f, 0.13f, 0.98f));
        card.sizeDelta = new Vector2(280, 40 + (options.Count + 1) * 44);

        if (anchorNear != null)
        {
            card.pivot = new Vector2(0, 1);
            var corners = new Vector3[4];
            anchorNear.GetWorldCorners(corners);
            card.position = corners[0]; // нижний левый угол кнопки-фильтра
        }
        else
        {
            card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
            card.anchoredPosition = Vector2.zero;
        }

        // Клик по самой карточке не должен закрывать попап — блокируем всплытие через отдельный Image-блокер

        var titleText = UIFactory.CreateText(card, "Title", title, 20, TextAnchor.MiddleCenter);
        var titleRect = (RectTransform)titleText.transform;
        titleRect.anchorMin = new Vector2(0, 1);
        titleRect.anchorMax = new Vector2(1, 1);
        titleRect.pivot = new Vector2(0.5f, 1);
        titleRect.sizeDelta = new Vector2(0, 32);
        titleRect.anchoredPosition = new Vector2(0, -6);

        float y = -40;

        var (showAllGo, showAllButton, showAllImage, showAllTextComp, showAllCheckmark) =
    CreateToggleRow(card, "Option_ShowAll", Localization.Get("filter.show_all"), y, showAllIsOn, null);
        y -= 44;

        var optionRows = new List<(Image image, Text text, GameObject checkmark)>();

        foreach (var option in options)
        {
            var (btnGo, button, image, text, checkmark) =
                CreateToggleRow(card, $"Option_{option.Label}", option.Label, y, option.IsOn, option.AccentColor);
            optionRows.Add((image, text, checkmark));

            button.onClick.AddListener(() =>
            {
                option.IsOn = !option.IsOn;
                ApplyToggleVisual(image, text, checkmark, option.IsOn, option.AccentColor);

                if (option.IsOn)
                {
                    // Включение любого обычного пункта сразу гасит "Показать все".
                    showAllIsOn = false;
                    ApplyToggleVisual(showAllImage, showAllTextComp, showAllCheckmark, false, null);
                }
                else if (options.All(o => !o.IsOn))
                {
                    // Ничего больше не выбрано - возвращаемся к состоянию "Показать все".
                    showAllIsOn = true;
                    ApplyToggleVisual(showAllImage, showAllTextComp, showAllCheckmark, true, null);
                }

                onOptionToggled?.Invoke(option);
            });

            y -= 44;
        }

        showAllButton.onClick.AddListener(() =>
        {
            showAllIsOn = true;
            ApplyToggleVisual(showAllImage, showAllTextComp, showAllCheckmark, true, null);

            for (int i = 0; i < options.Count; i++)
            {
                options[i].IsOn = false;
                ApplyToggleVisual(optionRows[i].image, optionRows[i].text, optionRows[i].checkmark, false, options[i].AccentColor);
            }

            onShowAllSelected?.Invoke();
        });

        return overlayGo;
    }

    // Создает одну кнопку на всю ширину карточки + квадрат-чекбокс слева
    private static (GameObject go, Button button, Image image, Text text, GameObject checkmark) CreateToggleRow(
        RectTransform parent, string name, string label, float y, bool isOn, Color32? accentColor)
    {
        var (btnGo, button, image, text) = UIFactory.CreateButton(parent, name, label, 18);
        var btnRect = (RectTransform)btnGo.transform;
        btnRect.anchorMin = new Vector2(0, 1);
        btnRect.anchorMax = new Vector2(1, 1);
        btnRect.pivot = new Vector2(0.5f, 1);
        btnRect.sizeDelta = new Vector2(-16, 36);
        btnRect.anchoredPosition = new Vector2(0, y);

        var checkboxGo = new GameObject("Checkbox", typeof(RectTransform), typeof(Image));
        checkboxGo.transform.SetParent(btnGo.transform, false);
        var checkboxRect = (RectTransform)checkboxGo.transform;
        checkboxRect.anchorMin = new Vector2(0, 0.5f);
        checkboxRect.anchorMax = new Vector2(0, 0.5f);
        checkboxRect.pivot = new Vector2(0, 0.5f);
        checkboxRect.sizeDelta = new Vector2(CheckboxSize, CheckboxSize);
        checkboxRect.anchoredPosition = new Vector2(CheckboxLeftPadding, 0);
        checkboxGo.GetComponent<Image>().color = CheckboxBoxColor;

        var checkmarkText = UIFactory.CreateText(checkboxRect, "Checkmark", "\u2713", 16, TextAnchor.MiddleCenter);
        checkmarkText.color = CheckboxMarkColor;
        var checkmarkRect = (RectTransform)checkmarkText.transform;
        checkmarkRect.anchorMin = Vector2.zero;
        checkmarkRect.anchorMax = Vector2.one;
        checkmarkRect.offsetMin = Vector2.zero;
        checkmarkRect.offsetMax = Vector2.zero;

        var textRect = (RectTransform)text.transform;
        textRect.offsetMin = new Vector2(CheckboxLeftPadding + CheckboxSize + CheckboxToLabelGap, textRect.offsetMin.y);

        ApplyToggleVisual(image, text, checkmarkText.gameObject, isOn, accentColor);

        return (btnGo, button, image, text, checkmarkText.gameObject);
    }

    // Красит фон кнопки
    private static void ApplyToggleVisual(Image image, Text label, GameObject checkmark, bool isOn, Color32? accentColor)
    {
        Color bg = accentColor.HasValue
            ? (isOn ? (Color)accentColor.Value : Darken(accentColor.Value))
            : (isOn ? UIFactory.ToggleOnBg : UIFactory.ToggleOffBg);

        image.color = bg;
        label.color = GetReadableTextColor(bg);
        checkmark.SetActive(isOn);
    }

    private static Color Darken(Color32 c, float factor = 0.5f)
    {
        return new Color(c.r / 255f * factor, c.g / 255f * factor, c.b / 255f * factor, 1f);
    }

    private static Color GetReadableTextColor(Color bg)
    {
        float luminance = 0.299f * bg.r + 0.587f * bg.g + 0.114f * bg.b;
        return luminance > 0.6f ? Color.black : Color.white;
    }
}