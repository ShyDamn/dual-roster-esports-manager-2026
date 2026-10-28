using DualRoster.Shared;

namespace DualRoster;

internal enum ReportScope { Club, Main, Academy }
internal sealed class TournamentMoney
{
    internal string Id = "", Name = "", Edition = "";
    internal long Prize, Stickers, Tickets, Hotel;
    internal long Net => Prize + Stickers + Tickets + Hotel;
    internal void Add(FinanceType type, long amount)
    {
        switch (type) { case FinanceType.TOURNAMENT: Prize += amount; break; case FinanceType.STICKERS: Stickers += amount; break; case FinanceType.TICKETS: Tickets += amount; break; case FinanceType.HOTEL: Hotel += amount; break; }
    }
}
internal sealed class ClubReport
{
    internal long Balance;
    internal readonly Dictionary<string, TournamentMoney> Events = new(StringComparer.Ordinal);
    internal TournamentMoney Row(string id, string name, string edition = "")
    {
        if (!Events.TryGetValue(id, out var row)) Events[id] = row = new() { Id = id, Name = name };
        if (row.Edition == "" && edition != "") row.Edition = edition;
        return row;
    }
    internal long Net => Events.Values.Sum(x => x.Net);
}

internal static class FinanceReport
{
    internal static ClubReport Build(ReportScope scope, bool monthOnly)
    {
        var result = new ClubReport();
        var main = GlobalValues.DataTeam;
        if (scope != ReportScope.Academy) AddWallet(result, "main", DataFinances.Club, main, monthOnly);
        if (scope != ReportScope.Main) AddWallet(result, "academy", DataFinances.Academy, main?.GetAcademyTeam(), monthOnly);
        return result;
    }
    private static void AddWallet(ClubReport report, string wallet, BudgetLedger? ledger, DataTeam? team, bool monthOnly)
    {
        if (ledger == null) return;
        report.Balance += ledger.TotalBudget;
        var today = GlobalValues.CurrentDate;
        var transactions = ledger.Transactions;
        if (transactions == null) return;
        var stickerMonths = new Dictionary<int, long>();
        for (int i = 0; i < transactions.Count; i++)
        {
            var transaction = transactions[i];
            if (monthOnly && (transaction.Date.Year != today.Year || transaction.Date.Month != today.Month)) continue;
            if (transaction.TypeTransaction == FinanceType.STICKERS)
            {
                var key = transaction.Date.Year * 12 + transaction.Date.Month;
                stickerMonths[key] = stickerMonths.GetValueOrDefault(key) + transaction.Amount;
                continue;
            }
            if (transaction.TypeTransaction is not (FinanceType.TOURNAMENT or FinanceType.TICKETS or FinanceType.HOTEL)) continue;
            var attribution = FinanceJournal.Lookup(wallet, transaction);
            report.Row(attribution?.Id ?? "unlinked", attribution?.Name ?? UiText.T("Без связи с турниром / старые записи", "Unlinked / older records"), attribution?.Edition ?? "").Add(transaction.TypeTransaction, transaction.Amount);
        }
        // Native sticker history has an exact tournament ID. Attribute a month only
        // when its complete payout history reconciles with that wallet's actual credits.
        foreach (var month in stickerMonths)
        {
            var rows = new List<(string Id, string Name, string Edition, int Amount)>();
            var payouts = team?.StickerPayouts;
            if (payouts != null)
                for (int i = 0; i < payouts.Count; i++)
                {
                    var payout = payouts[i];
                    if (payout.History == null || string.IsNullOrEmpty(payout.TournamentId)) continue;
                    var edition = DataTournaments.Get(payout.TournamentId)?.DateEnd.ToString("yyyy-MM") ?? "";
                    for (int j = 0; j < payout.History.Count; j++)
                    {
                        var payment = payout.History[j];
                        if (payment.Date.Year * 12 + payment.Date.Month == month.Key)
                            rows.Add((payout.TournamentId, payout.TournamentName, edition, payment.Amount));
                    }
                }
            if (rows.Count != 0 && rows.Sum(x => (long)x.Amount) == month.Value)
                foreach (var row in rows) report.Row(row.Id, row.Name, row.Edition).Stickers += row.Amount;
            else report.Row("unlinked", UiText.T("Без связи с турниром / старые записи", "Unlinked / older records")).Stickers += month.Value;
        }
    }
}
