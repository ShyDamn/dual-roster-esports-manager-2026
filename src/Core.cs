using System.Security.Cryptography;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;
using DualRoster.Shared;
using TeamList = Il2CppSystem.Collections.Generic.List<DataTeam>;
using TeamSet = Il2CppSystem.Collections.Generic.HashSet<string>;

namespace DualRoster;

[BepInPlugin(Id, "Dual Roster", "0.4.2")]
public sealed class Plugin : BasePlugin
{
    public const string Id = "local.dualroster.em2026";
    public const string SupportedHash = "F915DE7BD84B5C8D52D73789500BFC74E0108920EBE5FBABFCE1FC1DD24D60F9";
    internal static ManualLogSource Logger = null!;
    internal static bool TestMode;

    public override void Load()
    {
        Logger = Log;
        UiText.LanguageProvider = GameLocale.Code;
        using var game = File.OpenRead(Path.Combine(Paths.GameRootPath, "GameAssembly.dll"));
        using var sha = SHA256.Create();
        if (Convert.ToHexString(sha.ComputeHash(game)) != SupportedHash)
            throw new NotSupportedException("Dual Roster 0.4.2 requires Steam build 26012398. No gameplay patches applied.");

        var testRoot = Environment.GetEnvironmentVariable("DUALROSTER_TEST_SAVE");
        if (!string.IsNullOrEmpty(testRoot))
        {
            // Explicit test launcher only. Fail closed before entering the career.
            testRoot = Path.GetFullPath(testRoot);
            if (!File.Exists(Path.Combine(testRoot, ".dualroster-test-copy")))
                Environment.FailFast("Missing Dual Roster test-copy marker.");
            MessagePackStorage.PATH = testRoot;
            JSONStorage.PATH = testRoot;
            TestMode = true;
            Log.LogWarning($"TEST COPY: save storage redirected to {MessagePackStorage.PATH}");
        }

        new Harmony(Id).PatchAll(typeof(Plugin).Assembly);
        AddComponent<RosterPanel>();
        AddComponent<FinanceReportPanel>();
#if DUALROSTER_TESTING
        AddComponent<LongRunChecks>();
        AddComponent<UiPreviewChecks>();
#endif
        Log.LogInfo("Dual Roster 0.4.2 loaded. Main and academy registration enabled for the player's club.");
    }
}

internal static class Rules
{
    internal static bool IsPlayerTeam(DataTeam? team)
    {
        var main = GlobalValues.DataTeam;
        return team != null && main != null &&
            (team.Id == main.Id || (team.IsAcademy && team.OriginTeamId == main.Id));
    }

    internal static string SeatKey(DataTeam? team)
    {
        if (team == null) return "";
        if (IsPlayerTeam(team)) return team.Id ?? "";
        return team.IsAcademy && !string.IsNullOrEmpty(team.OriginTeamId)
            ? team.OriginTeamId : team.Id ?? "";
    }

    internal static bool Registered(DataTournament t, DataTeam? team) =>
        team != null && t.Participants != null && t.Participants.ContainsKey(team.Id);

    internal static string? CannotEnter(DataTournament? t, DataTeam? team)
    {
        if (t == null || t.State != TournamentState.Registration) return UiText.T("Регистрация закрыта", "Registration closed");
        if (team == null || team.IsDisbanded) return UiText.T("Нет активного состава", "No active roster");
        if (Registered(t, team)) return UiText.T("Заявка подана", "Registered");
        var count = team.MainSquad?.Count ?? 0;
        if (count < 5) return UiText.T($"Игроков: {count}/5 — нужно ещё {5 - count}", $"Players: {count}/5 — need {5 - count} more");
        if (!TournamentEngine.CanParticipateInTournament(t, team))
        {
            // These are the exact rank limits in this fingerprinted native build.
            var limit = t.Tier switch { TournamentType.T1 => 24, TournamentType.T2 => 64, TournamentType.Major => 32, TournamentType.T3 => 128, _ => int.MaxValue };
            var rank = DataTeams.GetFullSquadERSPlace(team);
            return limit == int.MaxValue ? UiText.T($"Рейтинг #{rank}: не проходит условия турнира", $"Rank #{rank}: tournament requirements not met")
                : UiText.T($"Рейтинг #{rank}; нужен топ-{limit}", $"Rank #{rank}; top {limit} required");
        }
        if (DataTournaments.TryGetTeamActiveTournament(team.Id, out var active))
            return UiText.T($"Занят: {active.Name} (до {active.DateEnd.ToString("dd.MM.yyyy")})", $"Busy: {active.Name} (until {active.DateEnd.ToString("yyyy-MM-dd")})");
        return null;
    }
}

[HarmonyPatch(typeof(TournamentRegistrationContent), nameof(TournamentRegistrationContent.CanRegisterMainTeam))]
internal static class MainRegistrationPatch
{
    static bool Prefix(TournamentRegistrationContent __instance, ref bool __result)
    {
        __result = Rules.CannotEnter(__instance.TournamentData, GlobalValues.DataTeam) == null;
        return false;
    }
}

[HarmonyPatch(typeof(TournamentRegistrationContent), nameof(TournamentRegistrationContent.CanRegisterAcademyTeam))]
internal static class AcademyRegistrationPatch
{
    static bool Prefix(TournamentRegistrationContent __instance, ref bool __result)
    {
        __result = Rules.CannotEnter(__instance.TournamentData, GlobalValues.DataTeam?.GetAcademyTeam()) == null;
        return false;
    }
}

[HarmonyPatch(typeof(TournamentEngine), nameof(TournamentEngine.GetOrgRegistrationKey))]
internal static class SeatKeyPatch
{
    static bool Prefix(DataTeam team, ref string __result)
    {
        if (!Rules.IsPlayerTeam(team)) return true;
        __result = Rules.SeatKey(team);
        return false;
    }
}

[HarmonyPatch(typeof(TournamentEngine), nameof(TournamentEngine.TryClaimOrgSeat))]
internal static class ClaimSeatPatch
{
    static bool Prefix(DataTeam team, TeamSet takenOrgs, ref bool __result)
    {
        if (!Rules.IsPlayerTeam(team)) return true;
        var key = Rules.SeatKey(team);
        __result = key.Length != 0 && takenOrgs != null && takenOrgs.Add(key);
        return false;
    }
}

// The native compiler inlined GetOrgRegistrationKey here. Patching the helper
// alone does not change this path. Preserve order, skip, and the entry cap.
[HarmonyPatch(typeof(TournamentEngine), nameof(TournamentEngine.PickTeamsWithoutOrgDuplicates))]
internal static class PickTeamsPatch
{
    static bool Prefix(TeamList availableTeams, int skip, int targetCount, ref TeamList __result)
    {
        var result = new TeamList();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (availableTeams != null && targetCount > 0)
            for (var i = Math.Clamp(skip, 0, availableTeams.Count);
                 i < availableTeams.Count && result.Count < targetCount; i++)
            {
                var team = availableTeams[i];
                var key = Rules.SeatKey(team);
                if (key.Length != 0 && seen.Add(key)) result.Add(team);
            }
        __result = result;
        return false;
    }
}

// Vanilla's one-time repair removes all manually registered player academies.
// Do not stamp its completion flag: removing this mod retains vanilla repair.
[HarmonyPatch(typeof(SlotController), nameof(SlotController.MigrateAutoSeatedUserAcademy))]
internal static class AcademyRepairPatch
{
    static bool Prefix() => false;
}
