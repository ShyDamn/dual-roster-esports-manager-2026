#if DUALROSTER_TESTING
using DualRoster.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DualRoster;

internal static class FeatureChecks
{
    internal static void Preview(string language)
    {
        UiText.LanguageProvider = () => language;
        if (int.TryParse(Environment.GetEnvironmentVariable("DUALROSTER_UI_WIDTH"), out var width))
            Screen.SetResolution(width, width == 1280 ? 800 : 1080, false);
        var root = NativeUi.Rect("FinancePreviewHost", null);
        var canvas = root.AddComponent<Canvas>(); canvas.enabled = false;
        var style = NativeUi.Label(root.transform, "font", NativeUi.Font()!);
        var section = FinancesStickersSection.Create(root.transform, style);
        FinanceReportPanel.Show(section);
        UiPreviewChecks.Begin();
        Plugin.Logger.LogInfo("UI PREVIEW READY: " + language);
    }
    internal static void Check([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool ok, string message)
    { if (!ok) throw new InvalidOperationException(message); Plugin.Logger.LogInfo("PASS: " + message); }
    internal static void FreeRosters(DataTeam main, DataTeam academy)
    {
        var active = DataTournaments.ActiveTournaments;
        for (int i = 0; i < active.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<DataTournament>>().Count; i++)
        { var t = active[i]; if (Rules.Registered(t, main)) t.RemoveParticipant(main.Id); if (Rules.Registered(t, academy)) t.RemoveParticipant(academy.Id); }
    }
    internal static long NativeNet(BudgetLedger ledger, bool month)
    {
        long result = 0;
        for (int i = 0; i < ledger.Transactions.Count; i++)
        {
            var row = ledger.Transactions[i];
            if (month && (row.Date.Year != GlobalValues.CurrentDate.Year || row.Date.Month != GlobalValues.CurrentDate.Month)) continue;
            if (row.TypeTransaction is FinanceType.TICKETS or FinanceType.HOTEL or FinanceType.TOURNAMENT or FinanceType.STICKERS) result += row.Amount;
        }
        return result;
    }
    internal static void Run()
    {
        var main = GlobalValues.DataTeam; var academy = main.GetAcademyTeam();
        Check(academy != null, "feature fixture has both rosters");
        FreeRosters(main, academy);
        var provider = UiText.LanguageProvider;
        var t = new DataTournament { Id = "dr-feature-" + Guid.NewGuid().ToString("N"), Name = "Feature report event", Tier = TournamentType.T4, State = TournamentState.Registration, IsLan = true, DateEnd = GlobalValues.CurrentDate.AddDays(5) };
        DataTournaments.Add(t); // Invitation content resolves its event by ID through native storage.
        UiText.LanguageProvider = () => "ru";
        Check(Rules.CannotEnter(t, main) == null && Rules.CannotEnter(t, academy) == null, "eligible rosters stay eligible after detailed reason changes");
        t.AddParticipant(main.Id);
        Check(RosterPanel.SiblingWarning(t, main, academy).Contains("Академия"), "eligible unregistered academy warning");
        t.RemoveParticipant(main.Id); t.AddParticipant(academy.Id);
        UiText.LanguageProvider = () => "en";
        Check(RosterPanel.SiblingWarning(t, main, academy).Contains("main team"), "English warning identifies missing main entry");
        var original = academy.MainSquad;
        try
        {
            academy.MainSquad = new Il2CppSystem.Collections.Generic.List<string>();
            Check(Rules.CannotEnter(t, academy)!.Contains("Registered"), "registered status precedes later lineup changes");
            t.RemoveParticipant(academy.Id);
            Check(Rules.CannotEnter(t, academy)!.Contains("0/5"), "missing player count is explicit");
        }
        finally { academy.MainSquad = original; }
        var mainOriginal = main.MainSquad;
        try
        {
            main.MainSquad = new Il2CppSystem.Collections.Generic.List<string>();
            academy.MainSquad = new Il2CppSystem.Collections.Generic.List<string>();
            var invitation = new DataEmail(new EmailAuthor { Type = EmailAuthorType.Tournament, AuthorId = t.Id, Name = t.Name }, true, new TournamentRegistrationContent(t, false));
            invitation.Initialize(); Invitations.Sync(invitation);
            Check(invitation.IsActive && !Invitations.State(Invitations.Content(invitation), false).Interactable && !Invitations.State(Invitations.Content(invitation), true).Interactable, "ineligible rosters keep an actionable invitation for refusal");
        }
        finally { main.MainSquad = mainOriginal; academy.MainSquad = original; }
        UiText.LanguageProvider = () => "ru";
        t.State = TournamentState.Qualification; t.AddParticipant(main.Id); t.AddParticipant(academy.Id);
        var activeList = DataTournaments.ActiveTournaments.Cast<Il2CppSystem.Collections.Generic.List<DataTournament>>(); activeList.Add(t);
        var another = new DataTournament { Id = "dr-other", Name = "Other event", Tier = TournamentType.T4, State = TournamentState.Registration };
        Check(Rules.CannotEnter(another, main)!.Contains(t.Name), "busy reason names the conflicting tournament");
        var m = DataFinances.Club; var a = DataFinances.Academy;
        long before = FinanceReport.Build(ReportScope.Club, false).Net;
        FinancesEventEngine.Instance.BuyTickets(true, main);
        FinancesEventEngine.Instance.FiveStarHotel(false, main);
        FinanceJournal.CurrentEvent = new(t.Id, t.Name);
        try
        {
            TournamentEngine.PayUserTournamentPrize(new TournamentFinalData { TeamId = main.Id, Money = 1234 }, t.Name);
            TournamentEngine.PayUserTournamentPrize(new TournamentFinalData { TeamId = academy.Id, Money = 567 }, t.Name);
        }
        finally { FinanceJournal.CurrentEvent = null; }
        var report = FinanceReport.Build(ReportScope.Club, false);
        Check(report.Balance == (long)m.TotalBudget + a.TotalBudget, "club balance equals both native wallets");
        Check(report.Net == NativeNet(m, false) + NativeNet(a, false), "club event totals reconcile with native transactions");
        Check(FinanceReport.Build(ReportScope.Main, false).Net + FinanceReport.Build(ReportScope.Academy, false).Net == report.Net, "roster report totals add up to club total");
        Check(FinanceReport.Build(ReportScope.Club, true).Net == NativeNet(m, true) + NativeNet(a, true), "month filter reconciles with native transaction dates");
        Check(report.Events.TryGetValue(t.Id, out var eventRow), "new transactions have an exact event link");
        Check(eventRow!.Prize == 1801, "both roster prizes attributed without double counting");
        Check(eventRow.Tickets == -2L * FinancesEventEngine.Instance._businessTicketsPrice && eventRow.Hotel == -2L * FinancesEventEngine.Instance._fourHotelPrice, "both roster travel costs attributed to the event");
        FinanceJournal.ForgetCache();
        Check(FinanceReport.Build(ReportScope.Club, false).Events[t.Id].Net == eventRow.Net, "event attribution survives journal reload");
        var mb = m.TotalBudget; var ab = a.TotalBudget;
        for (int i = 0; i < 4; i++) foreach (var scope in Enum.GetValues<ReportScope>()) FinanceReport.Build(scope, i % 2 == 0);
        Check(m.TotalBudget == mb && a.TotalBudget == ab, "report viewing never changes balances");
        var root = NativeUi.Rect("FeatureFinanceUiTest", null); root.AddComponent<Canvas>();
        var font = NativeUi.Font() ?? throw new InvalidOperationException("No UI font");
        var style = NativeUi.Label(root.transform, "font", font);
        var section = FinancesStickersSection.Create(root.transform, style);
        Check(section._strip.transform.Find("DualRosterReportTab") != null, "native finance strip contains the new report tab");
        section._strip.transform.Find("DualRosterReportTab").GetComponent<Button>().onClick.Invoke();
        var visible = Resources.FindObjectsOfTypeAll<TextMeshProUGUI>().Where(x => x != null && x.gameObject.activeInHierarchy).Select(x => x.text).ToArray();
        Check(visible.Contains("Турнирные финансы") && visible.Any(x => x.Contains(t.Name)), "finance report opens with real event rows");
        UiText.LanguageProvider = () => "en";
        section._strip.transform.Find("DualRosterReportTab").GetComponent<Button>().onClick.Invoke();
        visible = Resources.FindObjectsOfTypeAll<TextMeshProUGUI>().Where(x => x != null && x.gameObject.activeInHierarchy).Select(x => x.text).ToArray();
        Check(visible.Contains("Tournament finances") && visible.Contains("Club"), "finance report renders English controls");
        UnityEngine.Object.Destroy(root);
        UiText.LanguageProvider = provider;
        activeList.Remove(t);
        Check(report.Net != before, "feature test exercised actual native money operations");
        var journalPath = Path.Combine(MessagePackStorage.PATH, "DualRoster", "finance-" + SlotController.CurrentSlot + ".json");
        var journal = File.ReadAllText(journalPath);
        try
        {
            File.WriteAllText(journalPath, "{corrupt"); FinanceJournal.ForgetCache();
            Check(FinanceReport.Build(ReportScope.Club, false).Net == NativeNet(m, false) + NativeNet(a, false), "unreadable attribution never loses or invents money");
            Check(File.ReadAllText(journalPath) == "{corrupt", "unreadable attribution journal is preserved");
        }
        finally { File.WriteAllText(journalPath, journal); FinanceJournal.ForgetCache(); }
        var payout = academy.StickerPayouts[academy.StickerPayouts.Count - 1];
        string[] Autographs() => Enumerable.Range(0, payout.Stickers.Count).Where(i => !payout.Stickers[i].IsTeamSticker).Select(i => payout.Stickers[i].Nick).ToArray();
        var nicks = Autographs();
        var player = DataPlayers.Get(academy.MainSquad[0]);
        academy.RemoveUser(player); main.AddUser(player);
        Check(main.FullSquad.Contains(player.Id) && !academy.FullSquad.Contains(player.Id), "native roster transfer moves the player between teams");
        Check(Autographs().SequenceEqual(nicks), "existing Major autograph roster survives a player transfer");
        main.RemoveUser(player); academy.AddUser(player);
        var refusal = FinancesEventEngine.Instance.CloseOwnAcademyRefusal();
        if (string.IsNullOrEmpty(refusal))
        {
            Check(FinancesEventEngine.Instance.CloseOwnAcademy(), "native academy closure completes in the isolated fixture");
            Check(main.GetAcademyTeam() == null, "closed academy is no longer selectable");
        }
        else
        {
            Plugin.Logger.LogInfo("Native academy closure is legitimately blocked in this fixture: " + refusal);
            academy.IsDisbanded = true;
        }
        var closed = FinanceReport.Build(ReportScope.Club, false);
        Check(closed.Balance == (long)DataFinances.Club.TotalBudget + (DataFinances.Academy?.TotalBudget ?? 0), "club report remains valid with a disbanded academy");
        Plugin.Logger.LogInfo("FEATURE CHECKS PASSED");
    }
}
#endif
