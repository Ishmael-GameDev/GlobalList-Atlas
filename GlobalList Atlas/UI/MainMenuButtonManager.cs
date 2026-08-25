using System;
using System.IO;
using System.Linq;
using System.Reflection;
using GlobalEnums;
using UnityEngine;
using UnityEngine.UI;

namespace GlobalListAtlas.UI;

// Управляет отображением кнопки мода в главном меню игры
internal static class MainMenuButtonManager
{
    private static GameObject _buttonCanvasGo;
    private static Button _button;
    private static Image _buttonImage;
    private static MonoBehaviour _updateRunner;

    public static void Initialize()
    {
        // Невидимый объект для проверки состояния меню в Update
        var go = new GameObject("GlobalListAtlas_MenuButtonRunner");
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        _updateRunner = go.AddComponent<MenuButtonUpdateRunner>();
    }

    private class MenuButtonUpdateRunner : MonoBehaviour
    {
        private void Update()
        {
            if (UIManager.instance == null) return;

            bool inMainMenu = UIManager.instance.menuState == MainMenuState.MAIN_MENU;

            if (inMainMenu)
            {
                if (_buttonCanvasGo == null)
                {
                    CreateButton();
                }
                _buttonCanvasGo.SetActive(true);
            }
            else
            {
                if (_buttonCanvasGo != null)
                {
                    _buttonCanvasGo.SetActive(false);
                }
            }
        }
    }

    private static void CreateButton()
    {
        // Создаём Canvas для кнопки
        _buttonCanvasGo = new GameObject("GlobalListAtlas_MenuButtonCanvas", typeof(RectTransform));
        UnityEngine.Object.DontDestroyOnLoad(_buttonCanvasGo);

        var canvas = _buttonCanvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 6000;
        _buttonCanvasGo.AddComponent<GraphicRaycaster>();

        var scaler = _buttonCanvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        var buttonGo = new GameObject("GlobalistButton", typeof(RectTransform), typeof(Image), typeof(Button));
        buttonGo.transform.SetParent(_buttonCanvasGo.transform, false);

        var rect = buttonGo.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0, -10f); // Прижата к верхнему краю
        rect.sizeDelta = new Vector2(135, 135); // Квадратная кнопка

        _buttonImage = buttonGo.GetComponent<Image>();
        _buttonImage.color = Color.white;
        _buttonImage.preserveAspect = true;

        _button = buttonGo.GetComponent<Button>();
        _button.transition = Selectable.Transition.None;
        _button.onClick.AddListener(() => {
            if (MapListPanel.Instance != null)
            {
                MapListPanel.Instance.Toggle();
            }
        });

        Sprite sprite = LoadGlobalSprite();
        if (sprite != null)
        {
            _buttonImage.sprite = sprite;
            Modding.Logger.Log("[MainMenuButtonManager] Спрайт Global.png успешно загружен");
        }
        else
        {
            _buttonImage.color = new Color(0.15f, 0.15f, 0.18f, 0.9f);
            Modding.Logger.Log("[MainMenuButtonManager] Спрайт Global.png не найден. Используется цветной фон.");
        }
    }

    private static Sprite LoadGlobalSprite()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(x => x.EndsWith("Global.png", StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrEmpty(resourceName))
            {
                Modding.Logger.Log($"[MainMenuButtonManager] Ресурс Global.png не найден. Доступные ресурсы: {string.Join(", ", assembly.GetManifestResourceNames())}");
                return null;
            }

            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    Modding.Logger.Log("[MainMenuButtonManager] Не удалось получить поток для ресурса Global.png");
                    return null;
                }

                byte[] buffer = new byte[stream.Length];
                stream.Read(buffer, 0, buffer.Length);

                var tex = new Texture2D(1, 1);
                tex.LoadImage(buffer, true);

                return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f), 100f);
            }
        }
        catch (Exception e)
        {
            Modding.Logger.Log($"[MainMenuButtonManager] Ошибка загрузки спрайта: {e.Message}");
            return null;
        }
    }

    public static void SetButtonSprite(Sprite sprite)
    {
        if (_buttonImage != null && sprite != null)
        {
            _buttonImage.sprite = sprite;
            _buttonImage.color = Color.white;
        }
    }
}