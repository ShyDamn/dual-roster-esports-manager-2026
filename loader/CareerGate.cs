using BepInEx;
using DualRoster.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DualRoster.Workshop;

public sealed class CareerGate : MonoBehaviour
{
    public CareerGate(IntPtr pointer) : base(pointer) { }
    private static CareerGate? _instance;
    private Action? _pending;
    private GameObject? _root, _body;
    private TMP_FontAsset _font = null!;
    private TextMeshProUGUI? _status;
    private bool _confirm, _language;
    private string _error = "";
    internal static bool HasPending => _instance?._pending != null;
    public void Awake() => _instance = this;
    internal static void Show(Action action)
    {
        if (_instance == null) throw new InvalidOperationException("Career gate is unavailable.");
        _instance._pending ??= action;
        _instance._confirm = false;
        _instance.Render();
    }
    internal static void Cancel() => _instance?.Close();
    private void Close() { _pending = null; _confirm = false; _error = ""; _root?.SetActive(false); }
    public void Update()
    {
        if (_pending == null) return;
        if (Loader.Phase == LoaderPhase.Ready)
        {
            var action = _pending; Close(); action(); return;
        }
        if (_root == null || _language != UiText.IsRussian) Render();
        if (_status != null) _status.text = Loader.Status + (_error == "" ? "" : "\n" + _error);
    }
    private void Render()
    {
        try
        {
            _language = UiText.IsRussian;
            if (_root == null) _root = NativeUi.Overlay(transform, "DualRosterCareerGate", out _font);
            _root.SetActive(true);
            if (_body != null) { _body.SetActive(false); UnityEngine.Object.Destroy(_body); }
            _body = NativeUi.Column(_root.transform, "CareerGateBody", 24, 16);
            var rect = _body.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = Vector2.zero; rect.sizeDelta = new Vector2(900, 500);
            _body.AddComponent<Image>().color = new Color(.09f, .13f, .18f, 1);
            NativeUi.Label(_body.transform, _confirm ? UiText.T("Подтвердите запуск без мода", "Confirm loading without the mod") : UiText.GateTitle, _font, 28, 52);
            NativeUi.Label(_body.transform, UiText.GateWarning + (_confirm ? UiText.T("\nПеред запуском будет создана копия карьеры. Мод останется выключен до перезапуска игры.", "\nA career backup will be created before loading. The mod will stay disabled until the game is restarted.") : ""), _font, 23, 130);
            _status = NativeUi.Label(_body.transform, Loader.Status, _font, 20, 76);
            var row = NativeUi.Row(_body.transform, "SafeActions", 54);
            NativeUi.Button(row.transform, UiText.T("Дождаться мода", "Wait for the mod"), _font,
                () => { _confirm = false; Loader.Poller?.Retry(); Render(); }, true, 54);
            NativeUi.Button(row.transform, UiText.T("Вернуться в меню", "Back to menu"), _font, Close, false, 54);
            NativeUi.Button(_body.transform, _confirm ? UiText.T("Создать копию и загрузить без мода", "Back up and load without the mod") : UiText.T("Продолжить без мода…", "Continue without the mod…"), _font,
                () => { if (!_confirm) { _confirm = true; Render(); } else ContinueWithoutMod(); }, false, 54);
        }
        catch (Exception e) { Loader.Logger.LogWarning($"Career gate UI is waiting for a font: {e.Message}"); }
    }
    internal static void ConfirmWithoutModForTests()
    {
#if DUALROSTER_TESTING
        _instance?.ContinueWithoutMod();
#endif
    }
    private void ContinueWithoutMod()
    {
        if (_pending == null) return;
        try
        {
            var backup = CareerBackup.Create(Paths.GameRootPath, MessagePackStorage.PATH);
            Loader.Logger.LogWarning($"Explicit loading without Dual Roster. Career backup: {backup}");
            var action = _pending; Close(); Loader.ExplicitWithoutMod = true; action();
        }
        catch (Exception e)
        {
            _error = UiText.T("Копия не создана; карьера не открыта. ", "Backup failed; the career was not opened. ") + e.Message;
            Loader.Logger.LogError(e);
        }
    }
}

internal static class CareerBackup
{
    internal static string Create(string gameRoot, string saveRoot)
    {
        var source = Path.GetFullPath(saveRoot);
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException(source);
        var backup = Path.Combine(gameRoot, "DualRosterBackups", "career-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"), "Save");
        if ((Path.GetFullPath(backup) + Path.DirectorySeparatorChar).StartsWith(source.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Career backup must be outside the original save directory.");
        foreach (var file in Directory.EnumerateFiles(source, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false }))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(backup, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, false);
        }
        Directory.CreateDirectory(backup);
        return backup;
    }
}
