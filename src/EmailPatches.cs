using HarmonyLib;

namespace DualRoster;

internal static class Invitations
{
    internal static EmailView? CurrentView;
    private const string DeclinedMarker = "[Dual Roster] Приглашение отклонено.";

    internal static bool Declined(DataEmail? email) => email?.BodyAfterContent?.Contains(DeclinedMarker) == true;

    internal static TournamentRegistrationContent? Content(DataEmail? email) =>
        email?.Content?.TryCast<TournamentRegistrationContent>();

    // Registration letters describe a tournament, not a single roster. Derive their
    // activity from both rosters; vanilla deactivates every letter after one entry.
    internal static void Sync(DataEmail? email)
    {
        var content = Content(email);
        if (content == null || email == null || !email.IsIncoming) return;
        var t = content.TournamentData;
        email.IsActive = !Declined(email) && t?.State == TournamentState.Registration &&
            (Rules.CannotEnter(t, GlobalValues.DataTeam) == null ||
             Rules.CannotEnter(t, GlobalValues.DataTeam?.GetAcademyTeam()) == null);
    }

    internal static void SyncAll()
    {
        var emails = DataEmails.Emails;
        if (emails == null) return;
        for (var i = 0; i < emails.Count; i++) Sync(emails[i]);
    }

    internal static EmailActionState State(TournamentRegistrationContent? content, bool academy)
    {
        var team = academy ? GlobalValues.DataTeam?.GetAcademyTeam() : GlobalValues.DataTeam;
        var t = content?.TournamentData;
        var registered = t != null && Rules.Registered(t, team);
        return new EmailActionState
        {
            Visible = !Declined(content?.Email) && t?.State == TournamentState.Registration,
            Interactable = !Declined(content?.Email) && Rules.CannotEnter(t, team) == null,
            Text = registered ? (academy ? "Академия заявлена" : "Основа заявлена")
                : (academy ? "Заявить академию" : "Заявить основу")
        };
    }

    internal static void Register(TournamentRegistrationContent? content, bool academy)
    {
        var t = content?.TournamentData;
        var team = academy ? GlobalValues.DataTeam?.GetAcademyTeam() : GlobalValues.DataTeam;
        // Recheck at click time, including when a stale letter is open.
        if (Declined(content?.Email) || Rules.CannotEnter(t, team) != null) return;
        Plugin.Logger.LogInfo($"Email registration: tournament={t!.Name}, team={team!.Name}");
        PopupCreator.Instance.RegisterForTournament(t, team, (Action)(() =>
        {
            SyncAll();
            var view = CurrentView;
            if (view != null && view.gameObject.activeInHierarchy) view.Refresh();
        }));
    }

    // Dismiss both invitations for this event without registering or withdrawing a roster.
    // The visible marker is part of the normal serialized email, so refusal survives reload.
    internal static void Decline(DataEmail email)
    {
        var t = Content(email)?.TournamentData;
        if (t?.State != TournamentState.Registration || Declined(email)) return;
        var matches = new List<DataEmail> { email };
        var emails = DataEmails.Emails;
        if (emails != null)
            for (int i = 0; i < emails.Count; i++)
                if (emails[i].Pointer != email.Pointer && emails[i].IsIncoming && Content(emails[i])?.TournamentData?.Id == t.Id)
                    matches.Add(emails[i]);
        // Mark every matching letter before notifying the UI through native Deactivate.
        foreach (var item in matches)
            if (!Declined(item)) item.BodyAfterContent = (item.BodyAfterContent ?? "") + "\n\n" + DeclinedMarker;
        foreach (var item in matches) DataEmails.Deactivate(item);
        Plugin.Logger.LogInfo($"Invitation declined: {t.Name}; existing registrations preserved.");
        var view = CurrentView;
        if (view != null && view.gameObject.activeInHierarchy) view.Refresh();
    }

    internal static UserMessage Message(TournamentRegistrationContent content)
    {
        var t = content.TournamentData;
        return new UserMessage
        {
            Tone = MessageTone.Neutral,
            Title = "Dual Roster · основа + академия",
            Message = $"Основа: {Rules.CannotEnter(t, GlobalValues.DataTeam) ?? "может участвовать"}\n" +
                $"Академия: {Rules.CannotEnter(t, GlobalValues.DataTeam?.GetAcademyTeam()) ?? "может участвовать"}"
        };
    }
}

[HarmonyPatch(typeof(TournamentEmailActions), nameof(TournamentEmailActions.GetConfirmState))]
internal static class EmailMainStatePatch
{
    static bool Prefix(TournamentEmailActions __instance, ref EmailActionState __result)
    { __result = Invitations.State(__instance.Content, false); return false; }
}

[HarmonyPatch(typeof(TournamentEmailActions), nameof(TournamentEmailActions.GetCancelState))]
internal static class EmailAcademyStatePatch
{
    static bool Prefix(TournamentEmailActions __instance, ref EmailActionState __result)
    { __result = Invitations.State(__instance.Content, true); return false; }
}

[HarmonyPatch(typeof(TournamentEmailActions), nameof(TournamentEmailActions.Confirm))]
internal static class EmailMainRegisterPatch
{
    static bool Prefix(TournamentEmailActions __instance)
    { Invitations.Register(__instance.Content, false); return false; }
}

// The existing second UI button normally dismisses an invitation. For this one
// content type it submits the academy instead; other email actions are untouched.
[HarmonyPatch(typeof(TournamentEmailActions), nameof(TournamentEmailActions.Cancel))]
internal static class EmailAcademyRegisterPatch
{
    static bool Prefix(TournamentEmailActions __instance)
    { Invitations.Register(__instance.Content, true); return false; }
}

[HarmonyPatch(typeof(TournamentRegistrationContent), nameof(TournamentRegistrationContent.CheckForMessage))]
internal static class EmailMessagePatch
{
    static bool Prefix(TournamentRegistrationContent __instance, ref UserMessage __result)
    { __result = Invitations.Message(__instance); return false; }
}

[HarmonyPatch(typeof(EmailView), nameof(EmailView.SetupFooter))]
internal static class EmailFooterPatch
{
    static void Prefix(EmailView __instance, DataEmail email)
    {
        InvitationFooter.Restore(__instance);
        Invitations.CurrentView = __instance;
        Invitations.Sync(email);
    }

    static void Postfix(EmailView __instance, DataEmail email) => InvitationFooter.Show(__instance, email);
}

[HarmonyPatch(typeof(DataEmails), nameof(DataEmails.DeactivateAllByAuthor))]
internal static class EmailActivityPatch
{
    static void Postfix() => Invitations.SyncAll();
}

[HarmonyPatch(typeof(TournamentEngine), nameof(TournamentEngine.UnregisterPlayer))]
internal static class EmailUnregisterPatch
{
    static void Postfix() => Invitations.SyncAll();
}

[HarmonyPatch(typeof(TournamentRegistrationContent), nameof(TournamentRegistrationContent.Initialize))]
internal static class EmailLoadPatch
{
    static void Postfix(TournamentRegistrationContent __instance) => Invitations.Sync(__instance.Email);
}
