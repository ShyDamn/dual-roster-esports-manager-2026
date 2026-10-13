using HarmonyLib;

namespace DualRoster;

internal static class Travel
{
    [ThreadStatic] internal static DataTournament? CurrentTournament;
    [ThreadStatic] internal static bool CheckingEvents;
    [ThreadStatic] internal static DataTeam? PayingTeam;

    internal static List<DataTeam> Travellers(DataTeam? requested)
    {
        var main = GlobalValues.DataTeam;
        var academy = main?.GetAcademyTeam();
        var team = requested ?? main;
        var result = new List<DataTeam>();
        if (team == null) return result;
        result.Add(team);
        if (main == null || academy == null || !Rules.IsPlayerTeam(team)) return result;
        bool Together(DataTournament? t) => t != null && t.IsLan
            && t.State != TournamentState.Registration && t.State != TournamentState.Finished
            && Rules.Registered(t, main) && Rules.Registered(t, academy);
        var together = CheckingEvents && Together(CurrentTournament);
        if (!together)
        {
            var active = DataTournaments.ActiveTournaments;
            if (active != null)
                for (int i = 0; i < active.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<DataTournament>>().Count; i++)
                    if (Together(active[i])) { together = true; break; }
        }
        if (together) result.Add(team.Id == main.Id ? academy : main);
        return result;
    }

    internal static bool Pay(DataTeam? requested, Action<DataTeam> nativePayment)
    {
        if (PayingTeam != null) return true;
        if (GlobalValues.DataTeam == null || (requested != null && !Rules.IsPlayerTeam(requested))) return true;
        foreach (var team in Travellers(requested))
        {
            PayingTeam = team;
            try { nativePayment(team); }
            finally { PayingTeam = null; }
        }
        return false;
    }
}

[HarmonyPatch(typeof(TournamentEngine), nameof(TournamentEngine.CheckEvents))]
internal static class TravelEventContextPatch
{
    static void Prefix() { Travel.CheckingEvents = true; Travel.CurrentTournament = null; }
    static void Finalizer() { Travel.CheckingEvents = false; Travel.CurrentTournament = null; }
}

[HarmonyPatch(typeof(TournamentEngine), nameof(TournamentEngine.TravellingTeam))]
internal static class TravelTournamentPatch
{
    static void Postfix(DataTournament __0) { if (Travel.CheckingEvents) Travel.CurrentTournament = __0; }
}

[HarmonyPatch(typeof(FinancesEventEngine), nameof(FinancesEventEngine.BuyTickets))]
internal static class TicketsPatch
{
    static bool Prefix(FinancesEventEngine __instance, bool __0, DataTeam __1) =>
        Travel.Pay(__1, team => __instance.BuyTickets(__0, team));
}

[HarmonyPatch(typeof(FinancesEventEngine), nameof(FinancesEventEngine.FiveStarHotel))]
internal static class HotelPatch
{
    static bool Prefix(FinancesEventEngine __instance, bool __0, DataTeam __1) =>
        Travel.Pay(__1, team => __instance.FiveStarHotel(__0, team));
}

[HarmonyPatch(typeof(FinancesEventEngine), nameof(FinancesEventEngine.BuyTicketsWithoutEM))]
internal static class UnmanagedTicketsPatch
{
    static bool Prefix(FinancesEventEngine __instance) => Travel.Pay(
        Travel.CheckingEvents && Travel.CurrentTournament != null ? TournamentEngine.TravellingTeam(Travel.CurrentTournament) : null,
        _ => __instance.BuyTicketsWithoutEM());
}

[HarmonyPatch(typeof(FinancesEventEngine), nameof(FinancesEventEngine.BookHotelWithoutEM))]
internal static class UnmanagedHotelPatch
{
    static bool Prefix(FinancesEventEngine __instance) => Travel.Pay(
        Travel.CheckingEvents && Travel.CurrentTournament != null ? TournamentEngine.TravellingTeam(Travel.CurrentTournament) : null,
        _ => __instance.BookHotelWithoutEM());
}

// Vanilla travel passes a null ledger, even for an academy-only trip.
// Scope the correction to the native travel call; wages and other expenses retain their rules.
[HarmonyPatch(typeof(FinancesEventEngine), nameof(FinancesEventEngine.FinanceOperation))]
internal static class TravelLedgerPatch
{
    static void Prefix(FinanceType __1, ref string __2, ref BudgetLedger __3)
    {
        var team = Travel.PayingTeam;
        if (team == null || (__1 != FinanceType.TICKETS && __1 != FinanceType.HOTEL)) return;
        __3 = DataFinances.LedgerOfUserSquad(team);
        __2 = $"{__2} · {team.Name}";
        Plugin.Logger.LogInfo($"Travel expense: {__1}, roster={team.Name}");
    }
}

[HarmonyPatch(typeof(DataEmails), nameof(DataEmails.Add))]
internal static class TravelEmailCaptionPatch
{
    static void Prefix(DataEmail __0)
    {
        if (Travel.PayingTeam != null && __0 != null)
            __0.Subject = $"{__0.Subject} · {Travel.PayingTeam.Name}";
    }
}
