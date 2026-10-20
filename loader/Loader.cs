using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using System.Security.Cryptography;
using UnityEngine;

namespace DualRoster.Workshop;

[BepInPlugin("local.dualroster.workshop", "Dual Roster Workshop Loader", "0.3.1")]
public sealed class Loader : BasePlugin
{
    internal const string GameplayId = "local.dualroster.em2026";
    internal static ManualLogSource Logger = null!;
    internal static WorkshopPoller? Poller;
    internal static bool CareerStarted;
    internal static string Status = "Waiting for Steam / Ожидание Steam";

    public override void Load()
    {
        Logger = Log;
        using var stream = File.OpenRead(Path.Combine(Paths.GameRootPath, "GameAssembly.dll"));
        using var sha = SHA256.Create();
        if (Convert.ToHexString(sha.ComputeHash(stream)) != WorkshopPackage.GameHash)
            throw new NotSupportedException("Workshop loader requires Steam build 26012398. Update the loader after a game update.");
#if DUALROSTER_TESTING
        Application.runInBackground = true;
        var testSave = Environment.GetEnvironmentVariable("DUALROSTER_TEST_SAVE");
        if (string.IsNullOrEmpty(testSave) || !File.Exists(Path.Combine(testSave, ".dualroster-test-copy")))
            throw new InvalidOperationException("Test loader requires an isolated save copy.");
        MessagePackStorage.PATH = Path.GetFullPath(testSave);
        JSONStorage.PATH = Path.GetFullPath(testSave);
#endif
        new Harmony("local.dualroster.workshop").PatchAll(typeof(Loader).Assembly);
        Poller = AddComponent<WorkshopPoller>();
        AddComponent<LoaderStatus>();
        Log.LogInfo("Workshop loader ready. Only subscribed item 3819071177 is allowed; no mid-career loading.");
    }

    internal static void BeforeCareer()
    {
        // One final synchronous check before the game reads the career and runs its migrations.
        Poller?.TryLoad();
        // The menu can reject early clicks while the updated game imports its database.
        if (ImportQueue.IsReimporting || WorldHandover.InProgress) return;
        CareerStarted = true;
    }
}

public sealed class WorkshopPoller : MonoBehaviour
{
    public WorkshopPoller(IntPtr pointer) : base(pointer) { }
    private float _nextPoll;
    private bool _finished, _downloadRequested;
    private string _previousStatus = "";

    public void Update()
    {
        if (Time.unscaledTime < _nextPoll) return;
        _nextPoll = Time.unscaledTime + .5f;
        TryLoad();
#if DUALROSTER_TESTING
        if (Environment.GetEnvironmentVariable("DUALROSTER_LOADER_SMOKE") == "1" && Time.unscaledTime > 15)
            Application.Quit();
#endif
    }

    internal void TryLoad()
    {
        if (_finished || Loader.CareerStarted) return;
        try
        {
            if (IL2CPPChainloader.Instance.Plugins.ContainsKey(Loader.GameplayId))
            {
                SetStatus("Already loaded / Мод уже загружен");
                _finished = true;
                return;
            }
            uint flags;
            string directory;
#if DUALROSTER_TESTING
            var fixture = Environment.GetEnvironmentVariable("DUALROSTER_WORKSHOP_FIXTURE");
            if (!string.IsNullOrEmpty(fixture))
            {
                flags = uint.Parse(Environment.GetEnvironmentVariable("DUALROSTER_WORKSHOP_STATE") ?? "5");
                directory = fixture;
            }
            else
#endif
            {
                if (!Steamworks.SteamClient.IsValid) return;
                var ugc = Steamworks.SteamUGC.Internal;
                if (ugc == null) return;
                Steamworks.Data.PublishedFileId id = WorkshopPackage.ItemId;
                flags = ugc.GetItemState(id);
                directory = "";
                if (WorkshopPackage.State(flags) == PackageState.Ready)
                {
                    ulong size = 0;
                    uint timestamp = 0;
                    if (!ugc.GetItemInstallInfo(id, ref size, out directory, ref timestamp))
                    { SetStatus("Waiting for Workshop files / Ожидание файлов Workshop"); return; }
                }
                else if (WorkshopPackage.State(flags) == PackageState.WaitingForDownload && !_downloadRequested)
                    _downloadRequested = ugc.DownloadItem(id, false);
            }
            switch (WorkshopPackage.State(flags))
            {
                case PackageState.Unsubscribed:
                    SetStatus("Disabled: not subscribed / Отключён: нет подписки");
                    return;
                case PackageState.WaitingForDownload:
                    SetStatus("Downloading: wait in this menu / Загрузка: дождитесь в этом меню");
                    return;
            }
            var package = WorkshopPackage.Read(directory);
            var snapshot = package.Snapshot(directory, Path.Combine(Paths.CachePath, "DualRosterWorkshop"));
            // Snapshot contains just the verified gameplay DLL, never arbitrary Workshop plugins.
            var loaded = IL2CPPChainloader.Instance.LoadPlugins(snapshot);
            _finished = true; // A partially applied plugin must never be retried in this process.
            if (!loaded.Any(p => p.Metadata.GUID == Loader.GameplayId))
                throw new InvalidOperationException("Gameplay plugin did not load. See BepInEx/LogOutput.log.");
            SetStatus($"{package.Version} active / {package.Version} включён");
            Loader.Logger.LogInfo($"Loaded subscribed Workshop item {WorkshopPackage.ItemId}, version {package.Version}, SHA256 {package.Sha256} from {directory}");
        }
        catch (Exception e)
        {
            _finished = true;
            SetStatus("Not loaded: see BepInEx/LogOutput.log / Не загружен: смотрите журнал");
            Loader.Logger.LogError(e);
        }
    }

    private void SetStatus(string status)
    {
        Loader.Status = status;
        if (_previousStatus == status) return;
        _previousStatus = status;
        Loader.Logger.LogInfo(status);
    }
}

[HarmonyPatch(typeof(MainMenuInitializer), nameof(MainMenuInitializer.LoadSave))]
internal static class BeforeLoad { static void Prefix() => Loader.BeforeCareer(); }
[HarmonyPatch(typeof(MainMenuInitializer), nameof(MainMenuInitializer.StartNewGame))]
internal static class BeforeNewGame { static void Prefix() => Loader.BeforeCareer(); }
[HarmonyPatch(typeof(MainMenuInitializer), nameof(MainMenuInitializer.StartTutorial))]
internal static class BeforeTutorial { static void Prefix() => Loader.BeforeCareer(); }
[HarmonyPatch(typeof(MainMenuInitializer), nameof(MainMenuInitializer.StartRandomTeam))]
internal static class BeforeRandom { static void Prefix() => Loader.BeforeCareer(); }
