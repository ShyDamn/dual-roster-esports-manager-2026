using Il2CppInterop.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DualRoster.Workshop;

public sealed class LoaderStatus : MonoBehaviour
{
    public LoaderStatus(IntPtr pointer) : base(pointer) { }
    private GameObject? _canvas;
    private TextMeshProUGUI? _label;
    private float _next;

    public void Update()
    {
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + .5f;
        if (Loader.CareerStarted)
        {
            _canvas?.SetActive(false);
            return;
        }
        if (_label == null)
        {
            var menu = UnityEngine.Object.FindObjectOfType<MainMenuInitializer>();
            if (menu == null || !menu.IsBootstrapped) return;
            var texts = Resources.FindObjectsOfTypeAll<TextMeshProUGUI>();
            TextMeshProUGUI? template = null;
            foreach (var text in texts) if (text != null && text.font != null) { template = text; break; }
            if (template == null) return;
            _canvas = Rect("DualRosterWorkshopStatus", transform);
            var canvas = _canvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 95;
            var scaler = _canvas.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            var panel = Rect("Status", _canvas.transform);
            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 0);
            rect.anchoredPosition = new Vector2(24, 22);
            rect.sizeDelta = new Vector2(1000, 60);
            var background = panel.AddComponent<Image>();
            background.color = new Color(.04f, .08f, .11f, .92f);
            background.raycastTarget = false;
            var label = Rect("Label", panel.transform);
            var labelRect = label.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(12, 4);
            labelRect.offsetMax = new Vector2(-12, -4);
            _label = label.AddComponent<TextMeshProUGUI>();
            _label.font = template.font;
            _label.fontSharedMaterial = template.fontSharedMaterial;
            _label.fontSize = 19;
            _label.enableAutoSizing = true; _label.fontSizeMin = 14; _label.fontSizeMax = 19;
            _label.enableWordWrapping = true; _label.richText = false;
            _label.color = new Color(.75f, .95f, .9f, 1);
            _label.raycastTarget = false;
        }
        _label.text = "Dual Roster: " + Loader.Status;
    }

    private static GameObject Rect(string name, Transform parent)
    {
        var go = new GameObject(name, new Il2CppSystem.Type[] { Il2CppType.Of<RectTransform>() });
        go.transform.SetParent(parent, false);
        return go;
    }
}
