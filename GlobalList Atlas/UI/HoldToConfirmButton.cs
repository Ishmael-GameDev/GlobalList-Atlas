using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GlobalListAtlas.UI;

public class HoldToConfirmButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    public float HoldSeconds = 1.2f;
    public Image Fill;
    public Text Label;
    public string IdleLabel;
    public string HoldingLabel;
    public Action OnConfirmed;
    public Action OnLabelChanged;

    private bool _holding;
    private float _heldFor;
    private bool _fired;

    private void Update()
    {
        if (!_holding || _fired) return;

        _heldFor += Time.unscaledDeltaTime;
        float progress = Mathf.Clamp01(_heldFor / Mathf.Max(0.01f, HoldSeconds));
        SetFill(progress);

        if (progress >= 1f)
        {
            _fired = true;
            _holding = false;
            if (Label != null && !string.IsNullOrEmpty(IdleLabel)) { Label.text = IdleLabel; OnLabelChanged?.Invoke(); }
            OnConfirmed?.Invoke();
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (_fired) return;
        _holding = true;
        _heldFor = 0f;
        if (Label != null && !string.IsNullOrEmpty(HoldingLabel)) { Label.text = HoldingLabel; OnLabelChanged?.Invoke(); }
    }

    public void OnPointerUp(PointerEventData eventData) => Cancel();

    public void OnPointerExit(PointerEventData eventData) => Cancel();

    private void Cancel()
    {
        if (_fired) return;
        _holding = false;
        _heldFor = 0f;
        SetFill(0f);
        if (Label != null && !string.IsNullOrEmpty(IdleLabel)) { Label.text = IdleLabel; OnLabelChanged?.Invoke(); }
    }

    private void SetFill(float progress)
    {
        if (Fill == null) return;
        var rect = (RectTransform)Fill.transform;
        rect.anchorMax = new Vector2(progress, 1f);
    }
}
