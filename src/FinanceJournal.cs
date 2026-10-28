using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HarmonyLib;

namespace DualRoster;

// Attribution only. Amounts and budgets are always read from the native ledgers.
internal static class FinanceJournal
{
    internal sealed record Event(string Id, string Name, string Edition = "");
    [ThreadStatic] internal static Event? CurrentEvent;
    private static readonly object Sync = new();
    private static string _path = "";
    private static bool _readable = true;
    private static Dictionary<string, Event> _events = new();

    internal static string Key(string wallet, DataTransaction transaction)
    {
        var raw = $"{wallet}|{transaction.Id}|{transaction.Date.Ticks}|{(int)transaction.TypeTransaction}|{transaction.Amount}|{transaction.Description}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }
    private static bool Load()
    {
        var slot = SlotController.CurrentSlot;
        if (string.IsNullOrEmpty(slot) || slot.Length > 64 || slot.Any(c => !char.IsLetterOrDigit(c) && c != '-' && c != '_')) return false;
        var path = Path.Combine(MessagePackStorage.PATH, "DualRoster", "finance-" + slot + ".json");
        if (path == _path) return _readable;
        _path = path; _events = new(); _readable = true;
        try
        {
            if (File.Exists(path))
            {
                if (new FileInfo(path).Length > 16 * 1024 * 1024) throw new InvalidDataException("Attribution journal is too large.");
                _events = JsonSerializer.Deserialize<Dictionary<string, Event>>(File.ReadAllText(path)) ?? new();
            }
        }
        catch (Exception e)
        {
            // Never overwrite an unreadable journal. The report still shows native totals.
            _readable = false;
            Plugin.Logger.LogWarning($"Finance attribution unavailable; original journal kept: {e.Message}");
        }
        return _readable;
    }
    internal static Event? Lookup(string wallet, DataTransaction transaction)
    {
        lock (Sync) return Load() && _events.TryGetValue(Key(wallet, transaction), out var item) ? item : null;
    }
    internal static void Record(BudgetLedger ledger, int first)
    {
        var current = CurrentEvent;
        if (current == null || ledger.Transactions == null) return;
        lock (Sync)
        {
            if (!Load()) return;
            var wallet = ledger.Pointer == DataFinances.Club?.Pointer ? "main" : ledger.Pointer == DataFinances.Academy?.Pointer ? "academy" : "";
            if (wallet == "") return;
            var changed = false;
            for (int i = first; i < ledger.Transactions.Count; i++)
            {
                var transaction = ledger.Transactions[i];
                if (transaction.TypeTransaction is not (FinanceType.TICKETS or FinanceType.HOTEL or FinanceType.TOURNAMENT)) continue;
                var key = Key(wallet, transaction);
                if (!_events.TryGetValue(key, out var old) || old != current) { _events[key] = current; changed = true; }
            }
            if (!changed) return;
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, JsonSerializer.Serialize(_events));
                File.Move(temp, _path, true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
    internal static void ForgetCache() { lock (Sync) { _path = ""; _events.Clear(); } }
}

[HarmonyPatch(typeof(FinancesEventEngine), nameof(FinancesEventEngine.FinanceOperation))]
internal static class FinanceJournalOperationPatch
{
    [HarmonyPriority(Priority.Last)]
    static void Prefix(BudgetLedger __3, out (BudgetLedger? Ledger, int Count) __state)
    {
        var ledger = __3 ?? DataFinances.Club;
        __state = (ledger, ledger?.Transactions?.Count ?? 0);
    }
    static void Postfix((BudgetLedger? Ledger, int Count) __state)
    {
        if (__state.Ledger == null) return;
        try { FinanceJournal.Record(__state.Ledger, __state.Count); }
        catch (Exception e) { Plugin.Logger.LogWarning($"Finance attribution write failed; native payment retained: {e.Message}"); }
    }
}

[HarmonyPatch(typeof(TournamentEngine), nameof(TournamentEngine.CreatePrizeFundFinalList))]
internal static class FinancePrizeContextPatch
{
    static void Prefix(DataTournament __0, out FinanceJournal.Event? __state)
    {
        __state = FinanceJournal.CurrentEvent;
        FinanceJournal.CurrentEvent = new(__0.Id, __0.Name, __0.DateEnd.ToString("yyyy-MM"));
    }
    static void Finalizer(FinanceJournal.Event? __state) => FinanceJournal.CurrentEvent = __state;
}
