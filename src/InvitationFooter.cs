using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DualRoster.Shared;

namespace DualRoster;

internal sealed class InvitationFooter
{
    private static readonly Dictionary<IntPtr, InvitationFooter> Views = new();
    private readonly Button _decline;
    private readonly RectState _main, _academy;
    private readonly LayoutGroup? _layout;
    private readonly bool _layoutEnabled;

    private InvitationFooter(EmailView view)
    {
        _main = new RectState(view._confirmBtn.GetComponent<RectTransform>());
        _academy = new RectState(view._cancelBtn.GetComponent<RectTransform>());
        _layout = view._btnContainer.GetComponent<LayoutGroup>();
        _layoutEnabled = _layout != null && _layout.enabled;
        _decline = UnityEngine.Object.Instantiate(view._cancelBtn, view._cancelBtn.transform.parent);
        _decline.gameObject.name = "DualRosterDeclineInvitation";
        // Replacing the event also removes persistent prefab listeners copied by Instantiate.
        _decline.onClick = new Button.ButtonClickedEvent();
        _decline.gameObject.SetActive(false);
    }

    internal static void Restore(EmailView view)
    {
        if (!Views.TryGetValue(view.Pointer, out var footer) || footer._decline == null) return;
        footer._decline.gameObject.SetActive(false);
        footer._main.Restore();
        footer._academy.Restore();
        if (footer._layout != null) footer._layout.enabled = footer._layoutEnabled;
    }

    internal static void Show(EmailView view, DataEmail email)
    {
        if (Invitations.Content(email) == null || !email.IsActive || Invitations.Declined(email)) return;
        try
        {
            if (!Views.TryGetValue(view.Pointer, out var footer) || footer._decline == null)
                Views[view.Pointer] = footer = new InvitationFooter(view);
            if (footer._layout != null) footer._layout.enabled = false;
            Arrange(view._confirmBtn, 0);
            Arrange(view._cancelBtn, 1);
            Arrange(footer._decline, 2);
            footer._decline.GetComponentInChildren<TMP_Text>(true).text = UiText.Decline;
            foreach (var button in new[] { view._confirmBtn, view._cancelBtn, footer._decline })
            {
                var label = button.GetComponentInChildren<TMP_Text>(true);
                label.enableAutoSizing = true; label.fontSizeMin = 14; label.fontSizeMax = 22;
                label.enableWordWrapping = true;
            }
            footer._decline.interactable = true;
            footer._decline.onClick = new Button.ButtonClickedEvent();
            footer._decline.onClick.AddListener((UnityEngine.Events.UnityAction)(Action)(() => Invitations.Decline(email)));
            footer._decline.gameObject.SetActive(true);
        }
        catch (Exception e) { Plugin.Logger.LogError($"Invitation refusal button: {e}"); }
    }

    private static void Arrange(Button button, int column)
    {
        var rect = button.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(column / 3f, 0);
        rect.anchorMax = new Vector2((column + 1) / 3f, 1);
        rect.offsetMin = new Vector2(4, 0);
        rect.offsetMax = new Vector2(-4, 0);
    }

    private sealed class RectState
    {
        private readonly RectTransform _rect;
        private readonly Vector2 _min, _max, _offsetMin, _offsetMax;
        internal RectState(RectTransform rect)
        { _rect = rect; _min = rect.anchorMin; _max = rect.anchorMax; _offsetMin = rect.offsetMin; _offsetMax = rect.offsetMax; }
        internal void Restore()
        { _rect.anchorMin = _min; _rect.anchorMax = _max; _rect.offsetMin = _offsetMin; _rect.offsetMax = _offsetMax; }
    }
}
