#if DUALROSTER_TESTING
using UnityEngine;

namespace DualRoster.Workshop;

public sealed class LoaderChecks : MonoBehaviour
{
    public LoaderChecks(IntPtr pointer) : base(pointer) { }
    private bool _started, _completed;
    private void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); Loader.Logger.LogInfo("PASS: " + message); }
    public void Update()
    {
        var mode = Environment.GetEnvironmentVariable("DUALROSTER_GATE_TEST");
        if (string.IsNullOrEmpty(mode) || _completed) return;
        try
        {
            if (!_started)
            {
                var menu = UnityEngine.Object.FindObjectOfType<MainMenuInitializer>();
                if (menu == null || !menu.IsBootstrapped || ImportQueue.IsReimporting || WorldHandover.InProgress) return;
                _started = true;
                int calls = 0;
                Check(!Loader.BeforeCareer(() => calls++) && CareerGate.HasPending && !Loader.CareerStarted, "pending download blocks career entry");
                CareerGate.Cancel();
                Check(!CareerGate.HasPending && calls == 0, "back to menu cancels deferred entry");
                menu.LoadSave("86719");
                Check(CareerGate.HasPending && !Loader.CareerStarted && GlobalValues.DataTeam == null, "actual native LoadSave is blocked while downloading");
                if (mode == "vanilla")
                {
                    CareerGate.ConfirmWithoutModForTests();
                    Check(Loader.CareerStarted && !CareerGate.HasPending, "explicit confirmation resumes unmodded entry");
                    Check(Directory.GetDirectories(Path.Combine(BepInEx.Paths.GameRootPath, "DualRosterBackups"), "career-*").Length > 0, "explicit unmodded entry creates a career backup");
                }
                else { Environment.SetEnvironmentVariable("DUALROSTER_WORKSHOP_STATE", "5"); Loader.Poller!.TryLoad(); }
            }
            if (GlobalValues.DataTeam == null || TournamentEngine.Instance == null) return;
            Check(Loader.CareerStarted && !CareerGate.HasPending, "deferred career reached its normal loaded state");
            Check((Loader.Phase == LoaderPhase.Ready) == (mode != "vanilla"), "only the chosen load mode is active");
            _completed = true;
            Loader.Logger.LogInfo("GATE CHECKS PASSED: " + mode); Application.Quit();
        }
        catch (Exception e) { _completed = true; Loader.Logger.LogError("GATE CHECK FAILED: " + e); Application.Quit(); }
    }
}
#endif
