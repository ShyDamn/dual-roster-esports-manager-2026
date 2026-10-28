using HarmonyLib;
using DualRoster.Shared;

namespace DualRoster;

internal static class StickerLabels
{
    internal static void Apply(DataEmail? email)
    {
        var payout = email?.Content?.TryCast<StickerMoneyContent>()?.Payout;
        if (payout == null || email == null) return;
        var team = DataTeams.Get(payout.TeamId);
        if (!Rules.IsPlayerTeam(team)) return;
        var suffix = $" · {team.Name}";
        if (!(email.Subject ?? "").EndsWith(suffix, StringComparison.Ordinal)) email.Subject += suffix;
        const string marker = "\n\n[Dual Roster] ";
        var body = email.BodyAfterContent ?? "";
        var old = body.IndexOf(marker, StringComparison.Ordinal);
        if (old >= 0) body = body[..old];
        email.BodyAfterContent = body + marker + team.Name + "\n" + UiText.T(
            $"Бюджет: {(team.IsAcademy ? "академия" : "основа")}. Начислено за всё время: {UiText.Money(payout.Earned)}. Осталось к продаже: {UiText.Money(payout.Remaining)}.",
            $"Budget: {(team.IsAcademy ? "academy" : "main team")}. Earned so far: {UiText.Money(payout.Earned)}. Remaining sales: {UiText.Money(payout.Remaining)}.");
    }
}

[HarmonyPatch(typeof(DataEmails), nameof(DataEmails.Add))]
internal static class StickerNewLetterPatch
{
    static void Prefix(DataEmail __0) => StickerLabels.Apply(__0);
}

[HarmonyPatch(typeof(EmailView), nameof(EmailView.SetupHeader))]
internal static class StickerOldLetterPatch
{
    static void Prefix(DataEmail __0) => StickerLabels.Apply(__0);
}

