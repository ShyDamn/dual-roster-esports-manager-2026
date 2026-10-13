#if DUALROSTER_TESTING
using Il2CppInterop.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DualRoster;

internal static class DeclineChecks
{
    internal static void Run(DataTournament t, List<DataEmail> emails, DataTeam main, DataTeam academy)
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); Plugin.Logger.LogInfo("PASS: " + message); }
        // Exercise the actual third Button event, including cloned persistent-listener removal.
        var root = new GameObject("DualRosterDeclineTest", new Il2CppSystem.Type[] { Il2CppType.Of<RectTransform>() });
        root.SetActive(false);
        var view = root.AddComponent<EmailView>();
        var parent = new GameObject("Buttons", new Il2CppSystem.Type[] { Il2CppType.Of<RectTransform>() });
        parent.transform.SetParent(root.transform, false);
        parent.GetComponent<RectTransform>().sizeDelta = new Vector2(1000, 48);
        var layout = parent.AddComponent<HorizontalLayoutGroup>();
        view._btnContainer = parent;
        Button Make(string name)
        {
            var obj = new GameObject(name, new Il2CppSystem.Type[] { Il2CppType.Of<RectTransform>() });
            obj.transform.SetParent(parent.transform, false);
            var button = obj.AddComponent<Button>();
            var label = new GameObject("Text", new Il2CppSystem.Type[] { Il2CppType.Of<RectTransform>() });
            label.transform.SetParent(obj.transform, false);
            label.AddComponent<TextMeshProUGUI>();
            return button;
        }
        view._confirmBtn = Make("Main"); view._cancelBtn = Make("Academy");
        var original = view._confirmBtn.GetComponent<RectTransform>().anchorMin;
        Invitations.CurrentView = null;
        InvitationFooter.Show(view, emails[0]);
        var third = parent.transform.Find("DualRosterDeclineInvitation")?.GetComponent<Button>();
        Check(third != null && third.gameObject.activeSelf && third.interactable, "third refusal button exists and is enabled");
        Check(third!.GetComponentInChildren<TMP_Text>(true).text == "Отказаться", "refusal button caption");
        Check(!layout.enabled && view._cancelBtn.GetComponent<RectTransform>().anchorMax.x <= third.GetComponent<RectTransform>().anchorMin.x, "three button columns do not overlap");
        third.onClick.Invoke();
        Check(!Rules.Registered(t, main) && !Rules.Registered(t, academy), "refusal registers neither roster");
        Check(emails.All(e => !e.IsActive && Invitations.Declined(e)), "refusal closes both invitation letters");
        Invitations.SyncAll();
        Check(emails.All(e => !e.IsActive), "refresh does not reactivate declined invitations");
        emails[0].Content.Actions.Confirm(); emails[0].Content.Actions.Cancel();
        Check(!Rules.Registered(t, main) && !Rules.Registered(t, academy), "stale registration buttons cannot undo refusal");
        MessagePackStorage.Save("DualRosterDeclinedEmailTest", emails[0]);
        Check(MessagePackStorage.TryLoad<DataEmail>("DualRosterDeclinedEmailTest", out var restored) && Invitations.Declined(restored), "refusal marker survives email serialization");
        restored!.Initialize(); Invitations.Sync(restored);
        Check(!restored.IsActive, "refused email stays inactive after initialization");
        InvitationFooter.Restore(view);
        Check(!third.gameObject.activeSelf && layout.enabled && view._confirmBtn.GetComponent<RectTransform>().anchorMin == original, "other email types regain original footer layout");
        // Refusing the remaining invitation must not withdraw an already registered roster.
        foreach (var email in emails) email.BodyAfterContent = "";
        t.AddParticipant(main.Id); Invitations.SyncAll(); Invitations.Decline(emails[1]);
        Check(Rules.Registered(t, main) && !Rules.Registered(t, academy), "refusal preserves an existing main-team entry");
        var body = emails[1].BodyAfterContent; Invitations.Decline(emails[1]);
        Check(emails[1].BodyAfterContent == body, "repeat refusal is idempotent");
        foreach (var email in emails) email.BodyAfterContent = "";
        t.AddParticipant(academy.Id); Invitations.SyncAll();
        UnityEngine.Object.Destroy(root);
        Plugin.Logger.LogInfo("DECLINE CHECKS PASSED");
    }
}
#endif
