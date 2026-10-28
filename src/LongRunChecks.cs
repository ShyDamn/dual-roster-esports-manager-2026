#if DUALROSTER_TESTING
using System.Text.Json;
using UnityEngine;
using BepInEx.Unity.IL2CPP.Utils;

namespace DualRoster;

public sealed class LongRunChecks : MonoBehaviour
{
    public LongRunChecks(IntPtr pointer) : base(pointer) { }
    private static bool _begin;
    private bool _initialized, _done, _paidTravel, _checkedMajor, _registered, _checkedFinal;
    private int _days;
    private Il2CppSystem.DateTime _start;
    private string _eventId = "";
    private bool _pendingDay, _dayCompleted;
    private float _dayStartedAt;
    internal static void Begin() => _begin = true;
    internal sealed class Expected
    {
        public string EventId { get; set; } = "";
        public string TeamId { get; set; } = "";
        public string AcademyId { get; set; } = "";
        public long DateTicks { get; set; }
        public int MainBalance { get; set; }
        public int AcademyBalance { get; set; }
        public int MainTransactions { get; set; }
        public int AcademyTransactions { get; set; }
        public long Net { get; set; }
        public int Days { get; set; }
    }
    private static string ExpectedFile => Path.Combine(MessagePackStorage.PATH, "longrun-expected.json");
    internal static void Resume()
    {
        var expected = JsonSerializer.Deserialize<Expected>(File.ReadAllText(ExpectedFile))!;
        FeatureChecks.Check(GlobalValues.DataTeam.Id == expected.TeamId && GlobalValues.DataTeam.GetAcademyTeam()?.Id == expected.AcademyId, "year-long career reload restores both roster identities");
        FeatureChecks.Check(GlobalValues.CurrentDate.Ticks == expected.DateTicks && expected.Days >= 366, "year-long calendar progress survives a fresh game process");
        FeatureChecks.Check(DataFinances.Club.TotalBudget == expected.MainBalance && DataFinances.Academy.TotalBudget == expected.AcademyBalance, "both native wallet balances survive a fresh game process");
        FeatureChecks.Check(DataFinances.Club.Transactions.Count == expected.MainTransactions && DataFinances.Academy.Transactions.Count == expected.AcademyTransactions, "both native transaction histories survive reload");
        FeatureChecks.Check(FinanceReport.Build(ReportScope.Club, false).Net == expected.Net, "club report reconciles after a year-long save reload");
        var tournament = DataTournaments.Get(expected.EventId);
        FeatureChecks.Check(tournament != null && tournament.State == TournamentState.Finished && tournament.FinalList.Count >= 8, "completed dual-roster Major survives native save reload");
        Plugin.Logger.LogInfo("LONG RELOAD CHECKS PASSED");
    }
    public void Update()
    {
        if (!_begin || _done) return;
        try
        {
            if (!_initialized)
            {
                _initialized = true; _start = GlobalValues.CurrentDate;
                StabilizeFixture();
                _eventId = PrepareMajor();
                GlobalValues.ManagerUndismissable = true;
                foreach (var wallet in new[] { DataFinances.Club, DataFinances.Academy })
                    wallet.WageBudget = wallet.UnlocatedBudget = wallet.OperationalBudget = wallet.TransferBudget = wallet.MarketingBudget = 50000000;
                PopupCreator.SuppressMatchPopups = true;
                Plugin.Logger.LogInfo("LONG RUN START: " + _start.ToString("yyyy-MM-dd"));
            }
            var tournament = DataTournaments.Get(_eventId);
            if (!_registered)
            {
                foreach (var team in new[] { GlobalValues.DataTeam, GlobalValues.DataTeam.GetAcademyTeam() })
                {
                    if (Rules.Registered(tournament, team)) continue;
                    var reason = Rules.CannotEnter(tournament, team);
                    if (reason == null) TournamentEngine.Instance.RegisterPlayer(tournament, team);
                    else if (_days % 30 == 0) Plugin.Logger.LogInfo($"LONG entry waiting: {team.Name}: {reason}");
                }
                _registered = Rules.Registered(tournament, GlobalValues.DataTeam) && Rules.Registered(tournament, GlobalValues.DataTeam.GetAcademyTeam());
                if (_registered)
                {
                    FeatureChecks.Check(true, "both eligible native registrations enter the real scheduled Major");
                    SlotController.AutoSave(); SaveWriter.WaitForPendingSave();
                }
                else if (tournament.State is TournamentState.InProgress or TournamentState.Finished or TournamentState.Qualification) throw new InvalidOperationException("The fixture did not qualify both rosters before the real Major deadline.");
            }
            if (!_paidTravel && tournament.State is TournamentState.Qualification or TournamentState.InProgress)
            {
                Travel.CheckingEvents = true; Travel.CurrentTournament = tournament;
                try { FinancesEventEngine.Instance.BuyTickets(false, GlobalValues.DataTeam); FinancesEventEngine.Instance.FiveStarHotel(false, GlobalValues.DataTeam); }
                finally { Travel.CheckingEvents = false; Travel.CurrentTournament = null; }
                tournament.IsHaveTickets = tournament.IsHaveReservation = true; _paidTravel = true;
            }
            var calendar = CalendarController.Instance;
            if (!_pendingDay)
            {
                calendar.NextDayDate(); CalendarController.BeginDay();
                _pendingDay = true; _dayStartedAt = Time.realtimeSinceStartup;
                this.StartCoroutine(RunNativeDay(calendar));
                return;
            }
            if (!_dayCompleted)
            {
                if (Time.realtimeSinceStartup - _dayStartedAt > 120) throw new TimeoutException("Native day coroutine did not complete; inspect the test player log.");
                return;
            }
            _pendingDay = false; _dayCompleted = false;
            var errors = calendar.LastDayErrors;
            var errorCount = errors?.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<string>>().Count ?? 0;
            if (errorCount != 0) throw new InvalidOperationException("Native day engine reported errors: " + string.Join("; ", Enumerable.Range(0, errorCount).Select(i => errors![i])));
            var matches = GlobalValues.TodayMatches;
            if (matches != null)
                for (int i = 0; i < matches.Count; i++)
                {
                    var match = matches[i];
                    if (match.IsMatchOver) continue;
                    // ProccesAIMatch deliberately skips the player's team. Use
                    // the real player-result path for its unattended matches.
                    if (MatchesEngine.IsOurMatch(match))
                        MatchesEngine.Instance.EndMatch(match, MatchOddsCalculator.Resolve(MatchOddsCalculator.BuildPlan(match, null), match.SeriesType));
                    else MatchesEngine.Instance.ProccesAIMatch(match, !string.IsNullOrEmpty(match.TournamentId));
                }
            GlobalValues.CurrentMatch = null; GlobalValues.WatchingMatch = false;
            _days++;
            if (!_checkedFinal && tournament.State == TournamentState.Finished)
            {
                Plugin.Logger.LogInfo($"LONG immediate final: entries={tournament.FinalList.Count}, standings={DataTournaments.GetHistoryRow(tournament.Id)?.Places?.Count}");
                _checkedFinal = true;
            }
            if (!_checkedMajor && tournament.State == TournamentState.Finished && GlobalValues.CurrentDate.Ticks > tournament.DateEnd.AddMonths(1).Ticks)
            { CheckMajor(tournament); _checkedMajor = true; }
            if (_days % 30 == 0) Plugin.Logger.LogInfo($"LONG RUN PROGRESS: {_days}/366 days, {GlobalValues.CurrentDate.ToString("yyyy-MM-dd")}, Major={tournament.State}");
            if (_days < 366) return;
            FeatureChecks.Check(_checkedMajor, "real scheduled Major completed and paid during the native year-long run");
            FeatureChecks.Check(GlobalValues.CurrentDate.Year > _start.Year, "full 366-day native engine run crosses the year boundary");
            FeatureChecks.Check(GlobalValues.DataTeam.GetAcademyTeam() != null, "academy survives the year-long test career");
            var report = FinanceReport.Build(ReportScope.Club, false);
            FeatureChecks.Check(report.Net == FeatureChecks.NativeNet(DataFinances.Club, false) + FeatureChecks.NativeNet(DataFinances.Academy, false), "year-long report still reconciles with both native ledgers");
            var expected = new Expected {
                EventId = _eventId, TeamId = GlobalValues.DataTeam.Id, AcademyId = GlobalValues.DataTeam.GetAcademyTeam().Id,
                DateTicks = GlobalValues.CurrentDate.Ticks, MainBalance = DataFinances.Club.TotalBudget, AcademyBalance = DataFinances.Academy.TotalBudget,
                MainTransactions = DataFinances.Club.Transactions.Count, AcademyTransactions = DataFinances.Academy.Transactions.Count, Net = report.Net, Days = _days
            };
            SlotController.AutoSave(); SaveWriter.WaitForPendingSave();
            File.WriteAllText(ExpectedFile, JsonSerializer.Serialize(expected));
            _done = true; Plugin.Logger.LogInfo("LONG RUN CHECKS PASSED"); Application.Quit();
        }
        catch (Exception e) { _done = true; Plugin.Logger.LogError("LONG RUN CHECK FAILED: " + e); Application.Quit(); }
    }
    [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]
    private System.Collections.IEnumerator RunNativeDay(CalendarController calendar)
    {
        // Unity must execute nested iterators and waits; advancing only the outer
        // iterator skips AI simulations and cannot validate a real calendar day.
        yield return calendar.StartEventsSequentially(false);
        _dayCompleted = true;
    }
    private static void StabilizeFixture()
    {
        // Controlled integration fixture: prevent unattended contract expiry from
        // removing the rosters whose participation and payouts this test measures.
        foreach (var team in new[] { GlobalValues.DataTeam, GlobalValues.DataTeam.GetAcademyTeam() })
            foreach (var member in team.GetAllMembers())
                if (member.Contract != null)
                {
                    member.Contract.EndDate = GlobalValues.CurrentDate.AddYears(3);
                    if (!string.IsNullOrEmpty(member.Contract.LoanParentTeamId)) member.Contract.LoanParentMonthsRemaining = 36;
                }
        GlobalValues.DataTeam.ERS = 10000;
        GlobalValues.DataTeam.GetAcademyTeam().ERS = 9000;
        DataTeams.InvalidateMaxErsCache();
    }
    private static string PrepareMajor()
    {
        var main = GlobalValues.DataTeam; var academy = main.GetAcademyTeam();
        DataTournament? chosen = null;
        var events = DataTournaments.GetByDateRange(GlobalValues.CurrentDate.AddDays(-30), GlobalValues.CurrentDate.AddDays(365), null);
        Plugin.Logger.LogInfo($"LONG fixture date={GlobalValues.CurrentDate.ToString("yyyy-MM-dd")}, scheduled events={events.Count}");
        for (int i = 0; i < events.Count; i++)
        {
            var candidate = events[i];
            if (candidate.Tier == TournamentType.Major) Plugin.Logger.LogInfo($"LONG Major candidate: {candidate.Id}, {candidate.Name}, {candidate.State}, {candidate.DateStart} - {candidate.DateEnd}");
            if (candidate.Tier == TournamentType.Major && candidate.State != TournamentState.Finished &&
                (chosen == null || candidate.DateStart.Ticks < chosen.DateStart.Ticks)) chosen = candidate;
        }
        FeatureChecks.Check(chosen != null, "long-run fixture has a real scheduled Major");
        for (int i = 0; i < events.Count; i++)
        {
            var other = events[i];
            if (other.Id == chosen.Id || other.State != TournamentState.Registration) continue;
            if (Rules.Registered(other, main)) other.RemoveParticipant(main.Id);
            if (Rules.Registered(other, academy)) other.RemoveParticipant(academy.Id);
        }
        return chosen.Id;
    }
    private static void CheckMajor(DataTournament tournament)
    {
        var main = GlobalValues.DataTeam; var academy = main.GetAcademyTeam();
        var own = Enumerable.Range(0, tournament.FinalList.Count).Select(i => tournament.FinalList[i]).Where(x => x.TeamId == main.Id || x.TeamId == academy.Id).ToArray();
        foreach (var team in new[] { main, academy })
        {
            var entry = tournament.Participants.ContainsKey(team.Id) ? tournament.Participants[team.Id] : null;
            Plugin.Logger.LogInfo($"LONG Major result: team={team.Id}, registered={entry != null}, state={entry?.EntryState}, active={entry?.IsActive}, rank={entry?.RankBefore}, final={own.Count(x => x.TeamId == team.Id)}, squad={team.MainSquad.Count}");
        }
        Plugin.Logger.LogInfo("LONG final standings: " + string.Join(", ", Enumerable.Range(0, tournament.FinalList.Count).Select(i => tournament.FinalList[i].TeamId)));
        var standings = DataTournaments.GetHistoryRow(tournament.Id)?.Places;
        foreach (var team in new[] { main, academy })
        {
            Plugin.Logger.LogInfo($"LONG full standing: team={team.Name}, place={(standings != null && standings.ContainsKey(team.Id) ? standings[team.Id] : -1)}, stickerPayouts={team.StickerPayouts.Count}");
        }
        FeatureChecks.Check(standings != null && standings.ContainsKey(main.Id) && standings.ContainsKey(academy.Id), "both rosters have a final place in the actual Major history");
        var matches = MatchStorage.GetByDateRange(tournament.DateStart.AddDays(-30), tournament.DateEnd.AddDays(2), null);
        FeatureChecks.Check(Enumerable.Range(0, matches.Count).Any(i => matches[i].TournamentId == tournament.Id && matches[i].IsMatchOver && (matches[i].Team1Id == main.Id || matches[i].Team2Id == main.Id)), "main team played native simulated Major matches");
        FeatureChecks.Check(Enumerable.Range(0, matches.Count).Any(i => matches[i].TournamentId == tournament.Id && matches[i].IsMatchOver && (matches[i].Team1Id == academy.Id || matches[i].Team2Id == academy.Id)), "academy played native simulated Major matches");
        var report = FinanceReport.Build(ReportScope.Club, false);
        FeatureChecks.Check(report.Events.TryGetValue(tournament.Id, out var money) && money.Prize == own.Sum(x => (long)x.Money), "completed Major prize report matches the native final standings");
        FeatureChecks.Check(money.Tickets < 0 && money.Hotel < 0, "Major report contains the recorded travel expenses");
        foreach (var team in new[] { main, academy })
            FeatureChecks.Check(Enumerable.Range(0, team.StickerPayouts.Count).Any(i => team.StickerPayouts[i].TournamentId == tournament.Id && team.StickerPayouts[i].Earned > 0), "Major sticker income reaches roster " + team.Name);
        long before = (long)DataFinances.Club.TotalBudget + DataFinances.Academy.TotalBudget;
        TournamentEngine.Instance.NextDay(); FinancesEventEngine.Instance.PayStickerMoney(GlobalValues.CurrentDate);
        FeatureChecks.Check((long)DataFinances.Club.TotalBudget + DataFinances.Academy.TotalBudget == before, "reprocessing the same tournament day and sticker month cannot pay twice");
    }
}
#endif
