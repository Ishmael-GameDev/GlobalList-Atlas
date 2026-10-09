using GlobalListAtlas.Install;
using GlobalListAtlas.Integrations;
using UnityEngine;
using UnityEngine.UI;

namespace GlobalListAtlas.UI;

// Драйвер блокировки Architect: каждый кадр держит блокировку и кнопку в панели HKMP.
// Отдельная кнопка в углу экрана — запасной вариант, если встроиться в панель HKMP не вышло
public class HkmpLockPanel : MonoBehaviour
{
    private static HkmpLockPanel _instance;

    private GameObject _canvasGo;
    private GameObject _buttonGo;
    private Button _button;
    private Image _buttonImage;
    private Text _buttonText;

    private static readonly Color OffColor = UIFactory.ButtonBg;
    private static readonly Color OnColor = new(0.55f, 0.22f, 0.24f, 1f);
    private bool _shownLocked;

    private const float Width = 260f;
    private const float Height = 40f;

    public static void EnsureCreated()
    {
        if (_instance != null) return;
        var go = new GameObject("GlobalListAtlas_HkmpLockPanel");
        Object.DontDestroyOnLoad(go);
        _instance = go.AddComponent<HkmpLockPanel>();
    }

    private void Awake()
    {
        UIFactory.CreateRootCanvas("HkmpLockPanelCanvas", out _canvasGo);

        var (go, button, image, text) = UIFactory.CreateButton(_canvasGo.transform, "HkmpArchitectLockButton", "", 16);
        _buttonGo = go;
        _button = button;
        _buttonImage = image;
        _buttonText = text;
        text.alignment = TextAnchor.MiddleCenter;

        var rect = (RectTransform)go.transform;
        rect.anchorMin = new Vector2(1, 1);
        rect.anchorMax = new Vector2(1, 1);
        rect.pivot = new Vector2(1, 1);
        rect.sizeDelta = new Vector2(Width, Height);
        rect.anchoredPosition = new Vector2(-16f, -16f);

        UIFactory.AddOutline(go);
        button.onClick.AddListener(OnClicked);

        _buttonGo.SetActive(false);
    }

    private void OnClicked()
    {
        HkmpArchitectLock.SetHostLocked(!HkmpArchitectLock.HostLocked);
        RefreshLabel();
    }

    private void Update()
    {
        // Вне HKMP-сессии почти ничего не делает
        HkmpArchitectLock.Tick();

        HkmpPauseButton.Update();

        bool show = HkmpPauseButton.Failed
            && IsGameplayScene()
            && GameManager.instance.isPaused
            && HkmpArchitectLock.IsHost();

        if (show != _buttonGo.activeSelf)
        {
            _buttonGo.SetActive(show);
            if (show) RefreshLabel();
        }
        else if (show && _shownLocked != HkmpArchitectLock.HostLocked)
        {
            RefreshLabel();
        }
    }

    private void RefreshLabel()
    {
        bool locked = HkmpArchitectLock.HostLocked;
        _shownLocked = locked;
        _buttonText.text = locked ? HkmpPauseButton.UnlockLabel : HkmpPauseButton.LockLabel;
        _buttonImage.color = locked ? OnColor : OffColor;
    }

    private static bool IsGameplayScene()
    {
        try { return GameManager.instance != null && GameManager.instance.IsGameplayScene(); }
        catch { return false; }
    }
}
