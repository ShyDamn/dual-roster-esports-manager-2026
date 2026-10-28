using Il2CppInterop.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DualRoster.Shared;

namespace DualRoster;

public sealed class RosterPanel : MonoBehaviour
{
    public RosterPanel(IntPtr pointer) : base(pointer) { }
    internal static TournamentOverview? Current;
    private static RosterPanel? _instance;
    private GameObject? _canvas;
    private TextMeshProUGUI _mainStatus = null!, _academyStatus = null!, _mainCaption = null!, _academyCaption = null!;
    private Button _mainButton = null!, _academyButton = null!;
    private TextMeshProUGUI _header = null!, _warning = null!;
#if DUALROSTER_TESTING
    private bool _tested;
    private bool _testLoading;
    private float _testDiagnosticAt;
#endif
    private float _nextRefresh;

    public void Awake() => _instance = this;

    internal static bool Bind(TournamentOverview view)
    {
        if (_instance == null) return false;
        try
        {
            Current = view;
            _instance.EnsurePanel(view);
            _instance.Refresh();
            return true;
        }
        catch (Exception e)
        {
            _instance._canvas?.SetActive(false);
            Plugin.Logger.LogError($"Panel creation failed; vanilla controls retained: {e}");
            return false;
        }
    }

    public void Update()
    {
#if DUALROSTER_TESTING
        if (Plugin.TestMode && !_testLoading)
        {
            var menu = UnityEngine.Object.FindObjectOfType<MainMenuInitializer>();
            if (Time.unscaledTime >= _testDiagnosticAt)
            {
                _testDiagnosticAt = Time.unscaledTime + 10;
                Plugin.Logger.LogInfo($"TEST startup: menu={(menu != null)}, bootstrapped={menu?.IsBootstrapped}, importing={ImportQueue.IsReimporting}, handover={WorldHandover.InProgress}, time={Time.unscaledTime}");
            }
            if (menu != null && menu.IsBootstrapped && !ImportQueue.IsReimporting && !WorldHandover.InProgress)
            {
                _testLoading = true;
                menu.LoadSave("86719");
            }
        }
#endif
        if (_canvas != null && Time.unscaledTime >= _nextRefresh)
        {
            _nextRefresh = Time.unscaledTime + .25f;
            try { Refresh(); }
            catch (Exception e) { _canvas.SetActive(false); Plugin.Logger.LogError(e); }
        }
#if DUALROSTER_TESTING
        if (Plugin.TestMode && !_tested && GlobalValues.DataTeam != null && TournamentEngine.Instance != null)
        {
            _tested = true;
            try { RuntimeChecks.Run(); }
            catch (Exception e) { Plugin.Logger.LogError($"RUNTIME CHECK FAILED: {e}"); }
            if (Environment.GetEnvironmentVariable("DUALROSTER_TEST_QUIT") == "1") Application.Quit();
        }
#endif
    }

    private void EnsurePanel(TournamentOverview view)
    {
        if (_canvas != null) return;
        _canvas = RectObject("DualRosterCanvas", transform);
        var canvas = _canvas.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = _canvas.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = .5f;
        _canvas.AddComponent<GraphicRaycaster>();
        var panel = RectObject("DualRosterPanel", _canvas.transform);
        var rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1, 0);
        rect.anchoredPosition = new Vector2(-30, 65);
        rect.sizeDelta = new Vector2(680, 330);
        panel.AddComponent<Image>().color = new Color(.055f, .1f, .135f, .98f);
        _header = Label(panel.transform, "Header", "", 16, -12, 648, 30, view, 21);
        _mainStatus = Label(panel.transform, "MainStatus", "", 16, -48, 648, 48, view, 20);
        _mainButton = MakeButton(panel.transform, "MainButton", -100, view, false, out _mainCaption);
        _academyStatus = Label(panel.transform, "AcademyStatus", "", 16, -158, 648, 48, view, 20);
        _academyButton = MakeButton(panel.transform, "AcademyButton", -210, view, true, out _academyCaption);
        _warning = Label(panel.transform, "SiblingWarning", "", 16, -266, 648, 48, view, 18);
        _warning.color = new Color(1, .79f, .38f, 1);
        Plugin.Logger.LogInfo("Native registration panel created.");
    }

    private static GameObject RectObject(string name, Transform parent)
    {
        var go = new GameObject(name, new Il2CppSystem.Type[] { Il2CppType.Of<RectTransform>() });
        go.transform.SetParent(parent, false);
        return go;
    }

    private static void Place(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(width, height);
    }

    private static TextMeshProUGUI Label(Transform parent, string id, string text, float x, float y,
        float width, float height, TournamentOverview view, float fontSize)
    {
        var go = RectObject(id, parent);
        Place(go.GetComponent<RectTransform>(), x, y, width, height);
        var label = go.AddComponent<TextMeshProUGUI>();
        label.font = view._name.font;
        label.fontSize = fontSize;
        label.enableAutoSizing = true; label.fontSizeMin = 15; label.fontSizeMax = fontSize;
        label.enableWordWrapping = true; label.richText = false;
        label.color = Color.white;
        label.raycastTarget = false;
        label.text = text;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        return label;
    }

    private Button MakeButton(Transform parent, string id, float y, TournamentOverview view,
        bool academy, out TextMeshProUGUI caption)
    {
        var go = RectObject(id, parent);
        Place(go.GetComponent<RectTransform>(), 16, y, 648, 46);
        var image = go.AddComponent<Image>();
        image.color = new Color(.06f, .42f, .47f, 1);
        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        caption = Label(go.transform, "Caption", "", 12, 0, 624, 46, view, 20);
        caption.alignment = TextAlignmentOptions.Center;
        button.onClick.AddListener((Action)(() => Act(academy)));
        return button;
    }

    private void Refresh()
    {
        var visible = Current != null && Current.gameObject.activeInHierarchy
            && Current._tournament?.State == TournamentState.Registration;
        _canvas!.SetActive(visible);
        if (!visible) return;
        var t = Current!._tournament!;
        _header.text = UiText.T("ЗАЯВКИ СОСТАВОВ · DUAL ROSTER", "ROSTER ENTRIES · DUAL ROSTER");
        var main = GlobalValues.DataTeam; var academy = main?.GetAcademyTeam();
        RefreshRow(t, main, UiText.Main, _mainStatus, _mainCaption, _mainButton);
        RefreshRow(t, academy, UiText.Academy, _academyStatus, _academyCaption, _academyButton);
        _warning.text = SiblingWarning(t, main, academy);
    }

    private static void RefreshRow(DataTournament t, DataTeam? team, string name,
        TextMeshProUGUI status, TextMeshProUGUI caption, Button button)
    {
        var registered = Rules.Registered(t, team);
        var reason = Rules.CannotEnter(t, team);
        status.text = $"{name}: {(registered ? UiText.T("заявка подана", "registered") : reason ?? UiText.T("может участвовать", "eligible"))}";
        caption.text = registered ? UiText.T($"Отменить заявку: {name}", $"Withdraw: {name}") : UiText.T($"Заявить: {name}", $"Register: {name}");
        button.interactable = registered || reason == null;
    }

    internal static string SiblingWarning(DataTournament t, DataTeam? main, DataTeam? academy)
    {
        if (Rules.Registered(t, main) && Rules.CannotEnter(t, academy) == null)
            return UiText.T("Заявлена только основа. Академия тоже может участвовать.", "Only the main team is registered. The academy is also eligible.");
        if (Rules.Registered(t, academy) && Rules.CannotEnter(t, main) == null)
            return UiText.T("Заявлена только академия. Основа тоже может участвовать.", "Only the academy is registered. The main team is also eligible.");
        return "";
    }

    private void Act(bool forAcademy)
    {
        var t = Current?._tournament;
        var team = forAcademy ? GlobalValues.DataTeam?.GetAcademyTeam() : GlobalValues.DataTeam;
        if (t?.State != TournamentState.Registration || team == null) return;
        var registered = Rules.Registered(t, team);
        if (registered)
        {
            TournamentEngine.Instance.UnregisterPlayer(t, team);
            Current?.Renew();
        }
        else if (Rules.CannotEnter(t, team) == null)
            PopupCreator.Instance.RegisterForTournament(t, team, (Action)(() => Current?.Renew()));
        Plugin.Logger.LogInfo($"Registration action: tournament={t.Name}, team={team.Name}, cancel={registered}");
        Refresh();
    }
}
