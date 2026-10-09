using System;
using System.Collections;
using System.Reflection;
using GlobalListAtlas.Install;
using GlobalListAtlas.Logging;
using UnityEngine;
using UnityEngine.UI;

namespace GlobalListAtlas.Integrations;

// Кнопка блокировки Architect внутри панели HKMP в паузе (столбец Connect / Start Hosting / Settings).
// Создаётся родным ButtonComponent HKMP в его группе подключения, поэтому прячется и показывается вместе с панелью.
// Типы UI у HKMP internal, так что всё через рефлексию. Место под кнопкой ищем при каждом открытии паузы:
// берём самый нижний элемент этого столбца (включая кнопки надстроек вроде Rounds/Timer/Oneshot) и встаём под него
internal static class HkmpPauseButton
{
    private const float ColumnX = 1710f;
    private const float ColumnHalfWidth = 150f;
    private const float ButtonHeight = 38f;
    private const float Gap = 8f;
    private const float FallbackY = 508f;

    private const BindingFlags AnyInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags AnyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

    // Текст всегда на английском — это интерфейс HKMP
    internal const string LockLabel = "Lock Architect";
    internal const string UnlockLabel = "Unlock Architect";

    private static readonly Color LockedBgTint = new(1f, 0.62f, 0.62f, 1f);
    private static readonly Color LockedTextColor = new(1f, 0.86f, 0.86f, 1f);
    private static readonly Vector3[] Corners = new Vector3[4];

    public static bool Failed { get; private set; }

    private static object _button;
    private static GameObject _buttonGo;
    private static Text _buttonText;
    private static Image _buttonImage;
    private static object _connectGroup;
    private static IList _groupComponents;
    private static FieldInfo _componentGoField;
    private static MethodInfo _groupIsActive;
    private static MethodInfo _setActive;
    private static MethodInfo _setPosition;
    private static MethodInfo _setText;
    private static RectTransform _canvasRect;

    private static bool _shown;
    private static bool _shownLocked;

    public static void Update()
    {
        if (Failed) return;

        bool paused = GameManager.instance != null && GameManager.instance.isPaused;
        if (!paused && !_shown) return;

        if (_button == null && !TryCreate()) return;

        bool open = false;
        if (paused)
        {
            try { open = _groupIsActive.Invoke(_connectGroup, null) is true; }
            catch { open = false; }
        }

        bool show = open && HkmpArchitectLock.IsHost();
        if (show != _shown)
        {
            _shown = show;
            if (show)
            {
                Relayout();
                RefreshText();
            }
            Invoke(_setActive, show);
        }
        else if (show && _shownLocked != HkmpArchitectLock.HostLocked)
        {
            RefreshText();
        }
    }

    private static bool TryCreate()
    {
        var uiManager = HkmpArchitectLock.HkmpUiManager;
        if (uiManager == null) return false; // аддон HKMP ещё не инициализирован

        try
        {
            var connectInterface = uiManager.GetType().GetProperty("ConnectInterface", AnyInstance)?.GetValue(uiManager)
                ?? throw new MissingMemberException("UiManager.ConnectInterface");
            _connectGroup = connectInterface.GetType().GetField("_connectGroup", AnyInstance)?.GetValue(connectInterface)
                ?? throw new MissingMemberException("ConnectInterface._connectGroup");

            var groupType = _connectGroup.GetType();
            _groupComponents = groupType.GetField("_components", AnyInstance)?.GetValue(_connectGroup) as IList
                ?? throw new MissingMemberException("ComponentGroup._components");
            _groupIsActive = groupType.GetMethod("IsActive", AnyInstance, null, Type.EmptyTypes, null)
                ?? throw new MissingMemberException("ComponentGroup.IsActive");

            var asm = groupType.Assembly;
            _componentGoField = asm.GetType("Hkmp.Ui.Component.Component", true).GetField("GameObject", AnyInstance)
                ?? throw new MissingMemberException("Component.GameObject");
            var canvasGo = asm.GetType("Hkmp.Ui.UiManager", true).GetField("UiGameObject", AnyStatic)?.GetValue(null) as GameObject
                ?? throw new MissingMemberException("UiManager.UiGameObject");
            _canvasRect = canvasGo.transform as RectTransform
                ?? throw new MissingMemberException("UiGameObject.RectTransform");

            var buttonType = asm.GetType("Hkmp.Ui.Component.ButtonComponent", true);
            var ctor = buttonType.GetConstructor(AnyInstance, null, new[] { groupType, typeof(Vector2), typeof(string) }, null)
                ?? throw new MissingMemberException("ButtonComponent(ComponentGroup, Vector2, string)");
            _setActive = buttonType.GetMethod("SetActive", new[] { typeof(bool) }) ?? throw new MissingMemberException("SetActive");
            _setPosition = buttonType.GetMethod("SetPosition", new[] { typeof(Vector2) }) ?? throw new MissingMemberException("SetPosition");
            _setText = buttonType.GetMethod("SetText", new[] { typeof(string) }) ?? throw new MissingMemberException("SetText");
            var setOnPress = buttonType.GetMethod("SetOnPress", new[] { typeof(Action) }) ?? throw new MissingMemberException("SetOnPress");

            _button = ctor.Invoke(new object[] { _connectGroup, new Vector2(ColumnX, FallbackY), LockLabel });
            setOnPress.Invoke(_button, new object[] { (Action)OnPressed });
            Invoke(_setActive, false);

            _buttonGo = GetGameObject(_button);
            _buttonText = _buttonGo != null ? _buttonGo.GetComponentInChildren<Text>(true) : null;
            _buttonImage = _buttonGo != null ? _buttonGo.GetComponent<Image>() : null;
            if (_buttonText != null)
            {
                _buttonText.resizeTextForBestFit = true;
                _buttonText.resizeTextMinSize = 14;
                _buttonText.resizeTextMaxSize = _buttonText.fontSize;
            }

            Log.Info("[HkmpPauseButton] Кнопка блокировки Architect встроена в панель HKMP");
            return true;
        }
        catch (Exception e)
        {
            Failed = true;
            if (_button != null)
            {
                try { _button.GetType().GetMethod("Destroy")?.Invoke(_button, null); } catch { }
                _button = null;
            }
            Log.Warn($"[HkmpPauseButton] Не удалось встроить кнопку в панель HKMP, будет отдельная кнопка: {(e.InnerException ?? e).Message}");
            return false;
        }
    }

    private static void OnPressed()
    {
        HkmpArchitectLock.SetHostLocked(!HkmpArchitectLock.HostLocked);
        RefreshText();
    }

    private static void RefreshText()
    {
        bool locked = HkmpArchitectLock.HostLocked;
        _shownLocked = locked;
        Invoke(_setText, locked ? UnlockLabel : LockLabel);
        if (_buttonText != null)
            _buttonText.color = locked ? LockedTextColor : Color.white;
        // Спрайты hover/active HKMP меняет сам, цвет Image умножается на них — оттенок держится во всех состояниях
        if (_buttonImage != null)
            _buttonImage.color = locked ? LockedBgTint : Color.white;
    }

    private static void Relayout()
    {
        float lowest = float.MaxValue;

        // Элементы панели подключения, включая скрытые (текст статуса появляется после Start Hosting)
        foreach (var component in _groupComponents)
        {
            var go = GetGameObject(component);
            if (go != null && go != _buttonGo)
                Consider(go.transform as RectTransform, ref lowest);
        }

        // Видимые элементы надстроек, положенные прямо в канвас HKMP
        foreach (Transform child in _canvasRect)
        {
            if (child.gameObject != _buttonGo && child.gameObject.activeInHierarchy)
                Consider(child as RectTransform, ref lowest);
        }

        float y = lowest == float.MaxValue ? FallbackY : lowest - Gap - ButtonHeight / 2f;
        Invoke(_setPosition, new Vector2(ColumnX, y));
    }

    // Нижняя граница элемента в координатах HKMP (1920x1080), если он стоит в нашем столбце
    private static void Consider(RectTransform rect, ref float lowest)
    {
        if (rect == null) return;

        var canvas = _canvasRect.rect;
        if (canvas.width <= 0 || canvas.height <= 0) return;

        rect.GetWorldCorners(Corners);
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue;
        foreach (var corner in Corners)
        {
            var local = _canvasRect.InverseTransformPoint(corner);
            float x = (local.x - canvas.xMin) / canvas.width * 1920f;
            float y = (local.y - canvas.yMin) / canvas.height * 1080f;
            minX = Mathf.Min(minX, x);
            maxX = Mathf.Max(maxX, x);
            minY = Mathf.Min(minY, y);
        }

        if (Mathf.Abs((minX + maxX) / 2f - ColumnX) > ColumnHalfWidth) return;
        if (minY < lowest) lowest = minY;
    }

    private static GameObject GetGameObject(object component)
    {
        if (component == null || !_componentGoField.DeclaringType.IsInstanceOfType(component)) return null;
        return _componentGoField.GetValue(component) as GameObject;
    }

    private static void Invoke(MethodInfo method, object arg)
    {
        try { method.Invoke(_button, new[] { arg }); }
        catch (Exception e) { Log.Warn($"[HkmpPauseButton] {method.Name}: {(e.InnerException ?? e).Message}"); }
    }
}
