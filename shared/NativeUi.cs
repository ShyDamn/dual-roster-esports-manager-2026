using Il2CppInterop.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DualRoster.Shared;

internal static class NativeUi
{
    internal static GameObject Rect(string name, Transform? parent)
    {
        var obj = new GameObject(name, new Il2CppSystem.Type[] { Il2CppType.Of<RectTransform>() });
        if (parent != null) obj.transform.SetParent(parent, false);
        return obj;
    }
    internal static void Stretch(GameObject obj, float left = 0, float bottom = 0, float right = 0, float top = 0)
    {
        var rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom); rect.offsetMax = new Vector2(-right, -top);
    }
    internal static TMP_FontAsset? Font()
    {
        foreach (var text in Resources.FindObjectsOfTypeAll<TextMeshProUGUI>())
            if (text != null && text.font != null) return text.font;
        return null;
    }
    internal static TextMeshProUGUI Label(Transform parent, string value, TMP_FontAsset font, float size = 22, float height = 46)
    {
        var obj = Rect("Label", parent);
        var text = obj.AddComponent<TextMeshProUGUI>();
        text.font = font; text.fontSize = size; text.color = Color.white;
        text.richText = false; text.raycastTarget = false;
        text.enableWordWrapping = true; text.enableAutoSizing = true;
        text.fontSizeMin = Mathf.Min(size, 15); text.fontSizeMax = size;
        text.alignment = TextAlignmentOptions.MidlineLeft; text.text = value;
        Height(obj, height);
        return text;
    }
    internal static Button Button(Transform parent, string title, TMP_FontAsset font, Action action, bool primary = false, float height = 48)
    {
        var obj = Rect("Button", parent);
        Height(obj, height);
        var image = obj.AddComponent<Image>();
        image.color = primary ? new Color(.05f, .39f, .36f, 1) : new Color(.16f, .20f, .27f, 1);
        var button = obj.AddComponent<Button>(); button.targetGraphic = image;
        button.onClick.AddListener((UnityEngine.Events.UnityAction)action);
        var label = Label(obj.transform, title, font, 20, height);
        Stretch(label.gameObject, 12, 4, 12, 4);
        label.alignment = TextAlignmentOptions.Center;
        return button;
    }
    internal static void Height(GameObject obj, float height)
    {
        var layout = obj.GetComponent<LayoutElement>() ?? obj.AddComponent<LayoutElement>();
        layout.minHeight = layout.preferredHeight = height;
        layout.flexibleHeight = 0;
        layout.flexibleWidth = 1;
    }
    internal static GameObject Column(Transform parent, string name, int padding = 0, float gap = 12)
    {
        var obj = Rect(name, parent);
        var layout = obj.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(padding, padding, padding, padding); layout.spacing = gap;
        layout.childControlHeight = layout.childControlWidth = true;
        layout.childForceExpandHeight = false; layout.childForceExpandWidth = true;
        return obj;
    }
    internal static GameObject Row(Transform parent, string name, float height = 48)
    {
        var obj = Rect(name, parent); Height(obj, height);
        var layout = obj.AddComponent<HorizontalLayoutGroup>(); layout.spacing = 12;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true; layout.childForceExpandHeight = true;
        return obj;
    }
    internal static GameObject Overlay(Transform parent, string name, out TMP_FontAsset font)
    {
        font = Font() ?? throw new InvalidOperationException("No native font is available yet.");
        var root = Rect(name, parent);
        var canvas = root.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 200;
        var scaler = root.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
        root.AddComponent<GraphicRaycaster>();
        var backdrop = Rect("Backdrop", root.transform); Stretch(backdrop);
        backdrop.AddComponent<Image>().color = new Color(.025f, .04f, .06f, 1);
        return root;
    }
}
