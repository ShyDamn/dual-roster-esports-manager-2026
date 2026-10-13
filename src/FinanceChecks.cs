namespace DualRoster;

internal static class FinanceChecks
{
    private static void Check(bool ok, string name)
    {
        if (!ok) throw new InvalidOperationException(name);
        Plugin.Logger.LogInfo($"PASS: {name}");
    }

    internal static void Run()
    {
        var main = GlobalValues.DataTeam;
        var academy = main.GetAcademyTeam();
        var engine = FinancesEventEngine.Instance;
        var mainLedger = DataFinances.LedgerOfUserSquad(main);
        var academyLedger = DataFinances.LedgerOfUserSquad(academy);
        Check(mainLedger.Pointer != academyLedger.Pointer, "main and academy use separate ledgers");
        var t = new DataTournament { Id = "dualroster-finance-test", Name = "Dual Roster finance test", IsLan = true, State = TournamentState.Finished };
        // Pick an active enum value without changing any real tournament.
        t.State = TournamentState.Qualification;
        t.AddParticipant(main.Id); t.AddParticipant(academy.Id);
        var active = DataTournaments.ActiveTournaments.Cast<Il2CppSystem.Collections.Generic.List<DataTournament>>();
        active.Add(t);
        try
        {
            void Expense(Action action, int amount, bool payMain = true, bool payAcademy = true)
            {
                int m = mainLedger.Transactions.Count, a = academyLedger.Transactions.Count;
                int mb = mainLedger.OperationalBudget, ab = academyLedger.OperationalBudget;
                action();
                Check(mainLedger.Transactions.Count == m + (payMain ? 1 : 0)
                    && academyLedger.Transactions.Count == a + (payAcademy ? 1 : 0), "exactly one travel charge per participating roster");
                Check(mainLedger.OperationalBudget == mb - (payMain ? amount : 0)
                    && academyLedger.OperationalBudget == ab - (payAcademy ? amount : 0), $"travel cost {amount} charged to correct budgets");
            }
            Expense(() => engine.BuyTickets(true, main), engine._businessTicketsPrice);
            Expense(() => engine.BuyTickets(false, main), engine._firstTicketsPrice);
            Expense(() => engine.FiveStarHotel(true, main), engine._fiveHotelPrice);
            Expense(() => engine.FiveStarHotel(false, main), engine._fourHotelPrice);
            Travel.CheckingEvents = true; Travel.CurrentTournament = t;
            Expense(engine.BuyTicketsWithoutEM, engine._firstTicketsPrice * 2);
            Expense(engine.BookHotelWithoutEM, engine._fourHotelPrice * 2);
            Travel.CheckingEvents = false; Travel.CurrentTournament = null;
            t.RemoveParticipant(academy.Id);
            Expense(() => engine.BuyTickets(true, main), engine._businessTicketsPrice, true, false);
            t.RemoveParticipant(main.Id); t.AddParticipant(academy.Id);
            Expense(() => engine.FiveStarHotel(true, academy), engine._fiveHotelPrice, false, true);
            var mp = mainLedger.UnlocatedBudget; var ap = academyLedger.UnlocatedBudget;
            TournamentEngine.PayUserTournamentPrize(new TournamentFinalData { TeamId = main.Id, Money = 12345 }, "Dual Roster test");
            TournamentEngine.PayUserTournamentPrize(new TournamentFinalData { TeamId = academy.Id, Money = 6789 }, "Dual Roster test");
            Check(mainLedger.UnlocatedBudget == mp + 12345 && academyLedger.UnlocatedBudget == ap + 6789,
                "native prize awards reach each roster's ledger");
        }
        finally
        {
            active.Remove(t);
            Travel.CheckingEvents = false; Travel.CurrentTournament = null;
        }
        Plugin.Logger.LogInfo("TRAVEL AND PRIZE CHECKS PASSED");
    }

    internal static void CheckStickerPayments()
    {
        var main = GlobalValues.DataTeam;
        var academy = main.GetAcademyTeam();
        var m = DataFinances.LedgerOfUserSquad(main);
        var a = DataFinances.LedgerOfUserSquad(academy);
        var engine = FinancesEventEngine.Instance;
        var date = new Il2CppSystem.DateTime(GlobalValues.CurrentDate.Year, GlobalValues.CurrentDate.Month, 1).AddMonths(1);
        var mb = m.TotalBudget; var ab = a.TotalBudget;
        engine.PayStickerMoney(date);
        Check(m.TotalBudget > mb && a.TotalBudget > ab, "monthly stickers credit both roster ledgers");
        mb = m.TotalBudget; ab = a.TotalBudget;
        engine.PayStickerMoney(date);
        Check(m.TotalBudget == mb && a.TotalBudget == ab, "replaying sticker month cannot pay twice");
        foreach (var team in new[] { main, academy })
        {
            var payout = team.StickerPayouts[team.StickerPayouts.Count - 1];
            var email = new DataEmail(new EmailAuthor { Type = EmailAuthorType.Team, AuthorId = team.Id, Name = team.Name }, true,
                new StickerMoneyContent(payout, StickerLetterReason.Launch));
            email.Subject = "Наклейки Мажора";
            StickerLabels.Apply(email); var subject = email.Subject; StickerLabels.Apply(email);
            Check(email.Subject == subject && email.Subject.Contains(team.Name), "sticker subject identifies roster without duplicate suffix");
            email.Initialize();
            Check(email.BodyAfterContent.Contains(team.Name), "sticker summary identifies payment roster");
        }
        Plugin.Logger.LogInfo("STICKER CHECKS PASSED");
        DashboardChecks.Run();
    }
    internal static void Diagnose()
    {
        var main = GlobalValues.DataTeam;
        var academy = main.GetAcademyTeam();
        Plugin.Logger.LogInfo($"FINANCE DIAGNOSTIC: {GlobalValues.CurrentDate}, main={main.Id}, academy={academy?.Id}");
        var tournaments = DataTournaments.GetByDateRange(GlobalValues.CurrentDate.AddYears(-2), GlobalValues.CurrentDate.AddDays(1), null);
        for (int i = 0; i < tournaments.Count; i++)
        {
            var t = tournaments[i];
            if (t.Tier != TournamentType.Major || (!Rules.Registered(t, main) && !Rules.Registered(t, academy))) continue;
            Plugin.Logger.LogInfo($"MAJOR: {t.Id}; {t.Name}; {t.State}; {t.DateStart} - {t.DateEnd}; main={Rules.Registered(t, main)} academy={Rules.Registered(t, academy)}");
            var matches = MatchStorage.GetByDateRange(t.DateStart.AddDays(-30), t.DateEnd.AddDays(1), null);
            foreach (var team in new[] { main, academy })
            {
                if (team == null) continue;
                int played = 0, viewed = 0; long viewers = 0;
                for (int j = 0; j < matches.Count; j++)
                {
                    var match = matches[j];
                    if (match.TournamentId != t.Id || (match.Team1Id != team.Id && match.Team2Id != team.Id) || !match.IsMatchOver) continue;
                    played++; if (match.HasViewers) viewed++; viewers += match.ViewersTotal;
                }
                Plugin.Logger.LogInfo($"MATCHES {team.Name}: played={played}, withViewers={viewed}, viewers={viewers}");
            }
        }
        foreach (var team in new[] { main, academy })
        {
            if (team == null) continue;
            var payouts = team.StickerPayouts;
            Plugin.Logger.LogInfo($"STICKERS {team.Name}: {payouts?.Count}");
            if (payouts != null) for (int i = 0; i < payouts.Count; i++)
            {
                var p = payouts[i];
                Plugin.Logger.LogInfo($"PAYOUT: {p.TournamentName}; paid={p.PaidDate}; amount={p.Amount}; earned={p.Earned}; remaining={p.Remaining}; matches={p.Matches?.Count}; history={p.History?.Count}");
            }
            var transactions = DataFinances.LedgerOfUserSquad(team).Transactions;
            if (transactions != null) for (int i = 0; i < transactions.Count; i++)
            {
                var tx = transactions[i];
                if (tx.TypeTransaction == FinanceType.STICKERS || tx.TypeTransaction == FinanceType.TOURNAMENT)
                    Plugin.Logger.LogInfo($"MONEY {team.Name}: {tx.Date}; {tx.TypeTransaction}; {tx.Amount}; {tx.Description}");
            }
        }
    }
}
