using UnityEngine;

/// <summary>
/// Adjusts RectTransform to respect device safe area (notch, status bar, home indicator).
/// Attach to the SafeArea GameObject directly under Canvas.
/// </summary>
public class SafeAreaHandler : MonoBehaviour
{
    private RectTransform _rectTransform;
    private Rect _lastSafeArea;

    private void Awake()
    {
        _rectTransform = GetComponent<RectTransform>();
#if UNITY_ANDROID && !UNITY_EDITOR
        Screen.fullScreen = true;
#endif
        Apply();
    }

    private void Update()
    {
        if (_lastSafeArea != Screen.safeArea)
            Apply();
    }

    private void Apply()
    {
        var safeArea = Screen.safeArea;
        _lastSafeArea = safeArea;

        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;

        var screenSize = new Vector2(Screen.width, Screen.height);
        var anchorMin = safeArea.position / screenSize;
        var anchorMax = (safeArea.position + safeArea.size) / screenSize;

        _rectTransform.anchorMin = anchorMin;
        _rectTransform.anchorMax = anchorMax;
        _rectTransform.offsetMin = Vector2.zero;
        _rectTransform.offsetMax = Vector2.zero;
    }
}
