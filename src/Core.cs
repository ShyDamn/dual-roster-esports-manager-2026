using System.Security.Cryptography;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;
using TeamList = Il2CppSystem.Collections.Generic.List<DataTeam>;
using TeamSet = Il2CppSystem.Collections.Generic.HashSet<string>;

namespace DualRoster;

[BepInPlugin(Id, "Dual Roster", "0.3.0")]
public sealed class Plugin : BasePlugin
{
    public const string Id = "local.dualroster.em2026";
    public const string SupportedHash = "A4D500C32CC3FB5457ADCE6089ADD87165706732BAB96CDAB4FEC97AE32A78AA";
    internal static ManualLogSource Logger = null!;
    internal static bool TestMode;

    public override void Load()
    {
        Logger = Log;
        using var game = File.OpenRead(Path.Combine(Paths.GameRootPath, "GameAssembly.dll"));
        using var sha = SHA256.Create();
        if (Convert.ToHexString(sha.ComputeHash(game)) != SupportedHash)
            throw new NotSupportedException("Dual Roster 0.3.0 requires Steam build 25869291. No gameplay patches applied.");

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
        Log.LogInfo("Dual Roster 0.3.0 loaded. Main and academy registration enabled for the player's club.");
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
        if (t == null || t.State != TournamentState.Registration) return "Регистрация закрыта";
        if (team == null || team.IsDisbanded) return "Нет состава";
        if (Registered(t, team)) return "Заявка подана";
        if (team.MainSquad == null || team.MainSquad.Count < 5) return "Нужно 5 игроков";
        if (!TournamentEngine.CanParticipateInTournament(t, team)) return "Не проходит по рейтингу";
        if (DataTournaments.IsTeamActiveInAnyTournament(team.Id)) return "Участвует в другом турнире";
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
