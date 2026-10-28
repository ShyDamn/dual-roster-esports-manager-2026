using UnityEngine;
using TeamList = Il2CppSystem.Collections.Generic.List<DataTeam>;
using TeamSet = Il2CppSystem.Collections.Generic.HashSet<string>;

namespace DualRoster;

// Only executed by the isolated test launcher; never changes a real tournament.
internal static class RuntimeChecks
{
    internal static void Run()
    {
        var preview = Environment.GetEnvironmentVariable("DUALROSTER_UI_PREVIEW");
        if (!string.IsNullOrEmpty(preview)) { FeatureChecks.Preview(preview); return; }
        var longRun = Environment.GetEnvironmentVariable("DUALROSTER_LONGRUN");
        if (longRun == "run") { LongRunChecks.Begin(); return; }
        if (longRun == "resume") { LongRunChecks.Resume(); return; }
        if (Environment.GetEnvironmentVariable("DUALROSTER_FEATURE_CHECKS") == "1") { FeatureChecks.Run(); return; }
        if (Environment.GetEnvironmentVariable("DUALROSTER_FINANCE_DIAG") == "1")
        { FinanceChecks.Diagnose(); FinanceChecks.Run(); FinanceChecks.CheckStickerPayments(); return; }
        var main = GlobalValues.DataTeam;
        var academy = main.GetAcademyTeam();
        if (academy == null) throw new InvalidOperationException("The test career needs an academy.");
        Check(MessagePackStorage.PATH.Contains("save-snapshot", StringComparison.OrdinalIgnoreCase), "save isolation");
        var teams = new TeamList();
        teams.Add(main); teams.Add(academy); teams.Add(main);
        var selected = TournamentEngine.PickTeamsWithoutOrgDuplicates(teams, 0, 3);
        Check(selected.Count == 2 && selected[0].Id == main.Id && selected[1].Id == academy.Id,
            "both rosters kept; duplicate roster rejected");
        Check(TournamentEngine.PickTeamsWithoutOrgDuplicates(teams, 0, 1).Count == 1, "participant cap preserved");
        Check(TournamentEngine.PickTeamsWithoutOrgDuplicates(teams, 1, 1)[0].Id == academy.Id, "rank skip preserved");
        var seats = new TeamSet();
        Check(TournamentEngine.TryClaimOrgSeat(main, seats) && TournamentEngine.TryClaimOrgSeat(academy, seats)
            && !TournamentEngine.TryClaimOrgSeat(main, seats), "independent organization seats");
        var t = new DataTournament { Id = "dualroster-test", State = TournamentState.Registration, Tier = TournamentType.Major };
        t.AddParticipant(main.Id); t.AddParticipant(academy.Id);
        Check(t.IsParticipant && t.IsAcademyParticipant, "both registration flags");
        TournamentEngine.Instance.UnregisterPlayer(t, academy);
        Check(t.IsParticipant && !t.IsAcademyParticipant, "cancel academy keeps main");
        t.AddParticipant(academy.Id);
        TournamentEngine.Instance.UnregisterPlayer(t, main);
        Check(!t.IsParticipant && t.IsAcademyParticipant, "cancel main keeps academy");
        t.AddParticipant(main.Id);
        MessagePackStorage.Save("DualRosterTest", t);
        Check(MessagePackStorage.TryLoad<DataTournament>("DualRosterTest", out var roundTrip)
            && roundTrip.IsParticipant && roundTrip.IsAcademyParticipant, "MessagePack save/load retains both rosters");
        Plugin.Logger.LogInfo("RUNTIME CHECKS PASSED");
        if (Environment.GetEnvironmentVariable("DUALROSTER_TEST_FIXTURE") == "1") PrepareFixture(main, academy);
    }

    private static void PrepareFixture(DataTeam main, DataTeam academy)
    {
        var marker = Path.Combine(MessagePackStorage.PATH, ".fixture-created");
        if (File.Exists(marker)) return;
        Plugin.Logger.LogInfo($"Test rankings: main {DataTeams.GetFullSquadERSPlace(main)}, academy {DataTeams.GetFullSquadERSPlace(academy)}");
        DataTournament? major = null;
        var tournaments = DataTournaments.GetByDateRange(GlobalValues.CurrentDate.AddDays(-30), GlobalValues.CurrentDate.AddDays(365), null);
        for (int i = 0; i < tournaments.Count; i++)
        {
            var candidate = tournaments[i];
            if (candidate.State != TournamentState.Finished)
            {
                if (Rules.Registered(candidate, main)) candidate.RemoveParticipant(main.Id);
                if (Rules.Registered(candidate, academy)) candidate.RemoveParticipant(academy.Id);
                if (major == null && candidate.Tier == TournamentType.Major) major = candidate;
            }
        }
        if (major == null) throw new InvalidOperationException("No future Major found in the test copy.");
        major.State = TournamentState.Registration;
        major.DateEnd = GlobalValues.CurrentDate.AddDays(20);
        major.Name = "Dual Roster Test Major";
        var content = new TournamentRegistrationContent(major, false);
        Check(content.CanRegisterMainTeam() && content.CanRegisterAcademyTeam(), "both rosters eligible independently");
        var engine = TournamentEngine.Instance;
        engine.RegisterPlayer(major, main);
        Check(content.CanRegisterAcademyTeam(), "main registration does not block academy");
        engine.RegisterPlayer(major, academy);
        Check(major.IsParticipant && major.IsAcademyParticipant, "native RegisterPlayer retains both rosters");
        var sample = new Il2CppSystem.Collections.Generic.List<DataTournament>(); sample.Add(major);
        SlotController.MigrateAutoSeatedUserAcademy(sample.Cast<Il2CppSystem.Collections.Generic.IEnumerable<DataTournament>>());
        Check(major.IsParticipant && major.IsAcademyParticipant, "load migration preserves academy");
        engine.UnregisterPlayer(major, main);
        Check(content.CanRegisterMainTeam(), "academy registration does not block main");
        engine.RegisterPlayer(major, main);
        Check(major.IsParticipant && major.IsAcademyParticipant, "reverse registration order");
        engine.UnregisterPlayer(major, academy);
        engine.UnregisterPlayer(major, main);
        CheckInvitations(major, main, academy);
        SlotController.AutoSave();
        File.WriteAllText(marker, major.Id);
        Plugin.Logger.LogInfo($"TEST FIXTURE READY: {major.Id}; open Dual Roster Test Major. Only the snapshot was changed.");
    }

    private static void CheckInvitations(DataTournament t, DataTeam main, DataTeam academy)
    {
        var contents = new[] { new TournamentRegistrationContent(t, false), new TournamentRegistrationContent(t, true) };
        var emails = new List<DataEmail>();
        foreach (var content in contents)
        {
            var email = new DataEmail(new EmailAuthor { Type = EmailAuthorType.Tournament, AuthorId = t.Id, Name = t.Name }, true, content);
            email.Initialize();
            email.Subject = content.ForAcademy ? "Dual Roster · приглашение академии" : "Dual Roster · приглашение основы";
            DataEmails.Emails.Add(email);
            emails.Add(email);
        }
        for (int letter = 0; letter < 2; letter++)
        for (int reverse = 0; reverse < 2; reverse++)
        {
            t.RemoveParticipant(main.Id); t.RemoveParticipant(academy.Id);
            Invitations.SyncAll();
            var actions = contents[letter].Actions.Cast<TournamentEmailActions>();
            Check(actions.GetConfirmState().Visible && actions.GetConfirmState().Interactable
                && actions.GetCancelState().Visible && actions.GetCancelState().Interactable,
                $"letter {letter}: two enabled roster buttons");
            if (reverse == 0) actions.Confirm(); else actions.Cancel();
            Check(Rules.Registered(t, reverse == 0 ? main : academy)
                && !Rules.Registered(t, reverse == 0 ? academy : main), "email first click registers intended roster only");
            Check(emails.All(e => e.IsActive), "both invitations remain active after first registration");
            Check((reverse == 0 ? actions.GetCancelState() : actions.GetConfirmState()).Interactable,
                "second email button stays enabled");
            Check(!contents[letter].CheckForMessage().Message.Contains("другой состав"), "no sibling-roster rejection message");
            if (reverse == 0) actions.Cancel(); else actions.Confirm();
            Check(t.IsParticipant && t.IsAcademyParticipant, $"letter {letter}, order {reverse}: native email actions register both");
            var count = t.Participants.Count;
            actions.Confirm(); actions.Cancel();
            Check(t.Participants.Count == count && !actions.GetConfirmState().Interactable
                && !actions.GetCancelState().Interactable, "repeat email clicks are inert");
            Check(emails.All(e => !e.IsActive), "invitations complete after both registrations");
        }
        MessagePackStorage.Save("DualRosterEmailTournamentTest", t);
        Check(MessagePackStorage.TryLoad<DataTournament>("DualRosterEmailTournamentTest", out var loaded)
            && loaded.IsParticipant && loaded.IsAcademyParticipant, "email registrations survive save/load");
        t.RemoveParticipant(main.Id); t.RemoveParticipant(academy.Id);
        t.State = TournamentState.Finished;
        contents[0].Actions.Confirm(); contents[1].Actions.Cancel();
        Check(!t.IsParticipant && !t.IsAcademyParticipant, "closed tournament rejects stale email actions");
        t.State = TournamentState.Registration;
        Invitations.SyncAll();
        Check(emails.All(e => e.IsActive), "cancelled rosters restore invitation activity");
        DeclineChecks.Run(t, emails, main, academy);
        Plugin.Logger.LogInfo("EMAIL CHECKS PASSED: both letters, both orders, duplicate clicks, closed tournament, save/load.");
    }

    private static void Check(bool ok, string check)
    {
        if (!ok) throw new InvalidOperationException(check);
        Plugin.Logger.LogInfo($"PASS: {check}");
    }
}
