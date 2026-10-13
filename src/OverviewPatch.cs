using HarmonyLib;

namespace DualRoster;

[HarmonyPatch(typeof(TournamentOverview), "Renew")]
internal static class OverviewPatch
{
	private static void Postfix(TournamentOverview __instance)
	{
		DataTournament tournament = __instance._tournament;
		if (tournament != null && tournament.State == TournamentState.Registration && RosterPanel.Bind(__instance))
		{
			__instance._registrationYes?.SetActive(value: false);
			__instance._registrationAlready?.SetActive(value: false);
		}
	}
}
