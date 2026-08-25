using UnityEngine;
using UnityEngine.UI;

namespace GlobalListAtlas.UI;


// Хелперы для рантайм-UI поверх кастомного Canvas
internal static class UIFactory
{
    public static readonly Color PanelBg = new(0.08f, 0.08f, 0.1f, 0.92f);
    public static readonly Color ButtonBg = new(0.2f, 0.2f, 0.24f, 1f);
    public static readonly Color ButtonSelectedBg = new(0.11f, 0.11f, 0.14f, 1f);
    public static readonly Color TextColor = Color.white;

    public static readonly Color ToggleOnBg = new(0.58f, 0.58f, 0.66f, 1f);

    public static readonly Color ToggleOffBg = new(0.12f, 0.12f, 0.15f, 1f);

    private const float ScrollbarWidth = 14f;
    private const float ScrollbarToViewportGap = 6f;

    public static Font DefaultFont => Resources.GetBuiltinResource<Font>("Arial.ttf");

    public static Canvas CreateRootCanvas(string name, out GameObject go)
    {
        go = new GameObject(name, typeof(RectTransform));
        Object.DontDestroyOnLoad(go);

        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10000;

        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        go.AddComponent<GraphicRaycaster>();
        return canvas;
    }

    public static RectTransform CreatePanel(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = color;
        return (RectTransform)go.transform;
    }

    public static Text CreateText(Transform parent, string name, string text, int fontSize, TextAnchor anchor)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>();
        t.font = DefaultFont;
        t.fontSize = fontSize;
        t.color = TextColor;
        t.alignment = anchor;
        t.text = text;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Truncate;
        return t;
    }

    public static (GameObject go, Button button, Image image, Text text) CreateButton(
        Transform parent, string name, string label, int fontSize = 22)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);

        var image = go.GetComponent<Image>();
        image.color = ButtonBg;

        var button = go.GetComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.None;

        var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
        textGo.transform.SetParent(go.transform, false);
        var text = textGo.GetComponent<Text>();
        text.font = DefaultFont;
        text.fontSize = fontSize;
        text.color = TextColor;
        text.alignment = TextAnchor.MiddleLeft;
        text.text = label;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;

        var textRect = (RectTransform)textGo.transform;
        textRect.anchorMin = new Vector2(0, 0);
        textRect.anchorMax = new Vector2(1, 1);
        textRect.offsetMin = new Vector2(12, 2);
        textRect.offsetMax = new Vector2(-12, -2);

        return (go, button, image, text);
    }

    // Скролл-контейнер: слева вертикальный Scrollbar
    public static (RectTransform content, ScrollRect scrollRect) CreateVerticalScrollList(Transform parent, string name)
    {
        var rootGo = new GameObject(name, typeof(RectTransform));
        rootGo.transform.SetParent(parent, false);
        var root = (RectTransform)rootGo.transform;
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = Vector2.zero;
        root.offsetMax = Vector2.zero;

        var scrollRect = rootGo.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 30f;

        // --- Скроллбар слева ---
        var scrollbarGo = new GameObject("Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
        scrollbarGo.transform.SetParent(root, false);
        var scrollbarRect = (RectTransform)scrollbarGo.transform;
        scrollbarRect.anchorMin = new Vector2(0, 0);
        scrollbarRect.anchorMax = new Vector2(0, 1);
        scrollbarRect.pivot = new Vector2(0, 0.5f);
        scrollbarRect.sizeDelta = new Vector2(ScrollbarWidth, 0);
        scrollbarRect.anchoredPosition = Vector2.zero;

        var scrollbarBg = scrollbarGo.GetComponent<Image>();
        scrollbarBg.color = ButtonBg;
        scrollbarBg.raycastTarget = true;

        var slidingAreaGo = new GameObject("SlidingArea", typeof(RectTransform));
        slidingAreaGo.transform.SetParent(scrollbarRect, false);
        var slidingAreaRect = (RectTransform)slidingAreaGo.transform;
        slidingAreaRect.anchorMin = Vector2.zero;
        slidingAreaRect.anchorMax = Vector2.one;
        slidingAreaRect.offsetMin = new Vector2(2, 2);
        slidingAreaRect.offsetMax = new Vector2(-2, -2);

        var handleGo = new GameObject("Handle", typeof(RectTransform), typeof(Image));
        handleGo.transform.SetParent(slidingAreaRect, false);
        var handleImage = handleGo.GetComponent<Image>();
        handleImage.color = new Color(0.62f, 0.62f, 0.68f, 1f);
        var handleRect = (RectTransform)handleGo.transform;
        handleRect.anchorMin = new Vector2(0, 0);
        handleRect.anchorMax = new Vector2(1, 0.2f);
        handleRect.sizeDelta = Vector2.zero;

        var scrollbar = scrollbarGo.GetComponent<Scrollbar>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scrollbar.handleRect = handleRect;
        scrollbar.targetGraphic = handleImage;

        var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        viewportGo.transform.SetParent(root, false);
        var viewportRect = (RectTransform)viewportGo.transform;
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = new Vector2(ScrollbarWidth + ScrollbarToViewportGap, 0);
        viewportRect.offsetMax = Vector2.zero;

        var catcherGo = new GameObject("ScrollCatcher", typeof(RectTransform), typeof(Image));
        catcherGo.transform.SetParent(viewportRect, false);
        var catcherRect = (RectTransform)catcherGo.transform;
        catcherRect.anchorMin = Vector2.zero;
        catcherRect.anchorMax = Vector2.one;
        catcherRect.offsetMin = Vector2.zero;
        catcherRect.offsetMax = Vector2.zero;
        var catcherImage = catcherGo.GetComponent<Image>();
        catcherImage.color = new Color(0, 0, 0, 0);

        var contentGo = new GameObject("Content", typeof(RectTransform));
        contentGo.transform.SetParent(viewportRect, false);
        var content = (RectTransform)contentGo.transform;
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = new Vector2(1, 1);
        content.pivot = new Vector2(0.5f, 1);

        var layout = contentGo.AddComponent<VerticalLayoutGroup>();
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.childControlHeight = false;
        layout.childControlWidth = true;
        layout.spacing = 6;
        layout.padding = new RectOffset(54, 50, 4, 4);

        var fitter = contentGo.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect.viewport = viewportRect;
        scrollRect.content = content;
        scrollRect.verticalScrollbar = scrollbar;
        scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

        return (content, scrollRect);
    }
    public static Color GetReadableTextColor(Color32 bg)
    {
        float luminance = (0.299f * bg.r + 0.587f * bg.g + 0.114f * bg.b) / 255f;
        return luminance > 0.6f ? Color.black : Color.white;
    }
    // Прогресс-бар: темная подложка + заполняющаяся полоска + текст поверх
    public static (GameObject go, Image fillImage, Text text) CreateProgressBar(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = ButtonBg;

        var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fillGo.transform.SetParent(go.transform, false);
        var fillImage = fillGo.GetComponent<Image>();
        fillImage.color = ToggleOnBg;
        var fillRect = (RectTransform)fillGo.transform;
        fillRect.anchorMin = new Vector2(0, 0);
        fillRect.anchorMax = new Vector2(0f, 1);
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;

        var text = CreateText(go.transform, "Text", "", 16, TextAnchor.MiddleCenter);
        var textRect = (RectTransform)text.transform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        return (go, fillImage, text);
    }
}