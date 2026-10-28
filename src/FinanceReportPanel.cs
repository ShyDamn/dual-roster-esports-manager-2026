using DualRoster.Shared;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DualRoster;

public sealed class FinanceReportPanel : MonoBehaviour
{
    public FinanceReportPanel(IntPtr pointer) : base(pointer) { }
    private static FinanceReportPanel? _instance;
    private static readonly Dictionary<IntPtr, (FinancesStickersSection Section, TMP_Text Label)> Tabs = new();
    private GameObject? _root, _body;
    private FinancesStickersSection? _section;
    private TMP_FontAsset _font = null!;
    private ReportScope _scope;
    private bool _monthOnly, _language;
    private bool _localeInitialized;
    private float _next;
    private string _stamp = "";
    public void Awake() => _instance = this;

    internal static void Bind(FinancesStickersSection section)
    {
        if (section._strip == null || section._strip.transform.Find("DualRosterReportTab") != null) return;
        var button = section.Segment(UiText.T("Турниры клуба", "Club events"),
            (UnityEngine.Events.UnityAction)(Action)(() => Show(section)), out var label);
        button.name = "DualRosterReportTab";
        label.enableAutoSizing = true; label.fontSizeMin = 14; label.fontSizeMax = 21;
        label.enableWordWrapping = true;
        Tabs[section.Pointer] = (section, label);
    }
    internal static void Show(FinancesStickersSection section)
    {
        if (_instance == null) return;
        _instance._section = section;
        _instance._scope = ReportScope.Club;
        _instance._monthOnly = false;
        _instance.Open();
    }
    private void Open()
    {
        if (_root == null) _root = NativeUi.Overlay(transform, "DualRosterFinanceReport", out _font);
        _root.SetActive(true); Render();
    }
    private void Close() { _root?.SetActive(false); _section = null; }
    public void Update()
    {
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + .5f;
        var language = UiText.IsRussian;
        if (!_localeInitialized || language != _language)
        {
            _localeInitialized = true;
            _language = language;
            try
            {
                var folder = System.IO.Path.Combine(BepInEx.Paths.GameRootPath, "DualRoster");
                System.IO.Directory.CreateDirectory(folder);
                System.IO.File.WriteAllText(System.IO.Path.Combine(folder, "language.txt"), language ? "ru" : "en");
            }
            catch (Exception e) { Plugin.Logger.LogWarning($"Launcher language preference not saved: {e.Message}"); }
            foreach (var tab in Tabs.Values.ToArray()) if (tab.Label != null) tab.Label.text = UiText.T("Турниры клуба", "Club events");
            StickerDashboard.RefreshLabels();
            var view = Invitations.CurrentView;
            if (view != null && view.gameObject.activeInHierarchy) view.Refresh();
        }
        if (_root == null || !_root.activeSelf) return;
        if (_section?._strip == null || !_section._strip.activeInHierarchy) { Close(); return; }
        var stamp = $"{language}|{GlobalValues.CurrentDate.Ticks}|{DataFinances.Club?.Transactions?.Count}|{DataFinances.Academy?.Transactions?.Count}";
        if (_stamp != stamp) { _stamp = stamp; Render(); }
    }
    private void Render()
    {
        if (_root == null) return;
        if (_body != null) { _body.SetActive(false); UnityEngine.Object.Destroy(_body); }
        _body = NativeUi.Column(_root.transform, "ReportBody", 0, 12);
        NativeUi.Stretch(_body, 64, 48, 64, 48);
        var heading = NativeUi.Row(_body.transform, "Heading", 48);
        NativeUi.Label(heading.transform, UiText.T("Турнирные финансы", "Tournament finances"), _font, 28);
        var close = NativeUi.Button(heading.transform, UiText.T("Закрыть", "Close"), _font, Close);
        close.GetComponent<LayoutElement>().preferredWidth = 180; close.GetComponent<LayoutElement>().flexibleWidth = 0;
        var tabs = NativeUi.Row(_body.transform, "RosterScope");
        foreach (var scope in new[] { ReportScope.Club, ReportScope.Main, ReportScope.Academy })
        {
            var captured = scope;
            var title = scope switch { ReportScope.Club => UiText.Club, ReportScope.Main => UiText.Main, _ => UiText.Academy };
            NativeUi.Button(tabs.transform, title, _font, () => { _scope = captured; Render(); }, _scope == scope);
        }
        var periods = NativeUi.Row(_body.transform, "Period");
        NativeUi.Button(periods.transform, UiText.T("Вся история операций", "All recorded history"), _font, () => { _monthOnly = false; Render(); }, !_monthOnly);
        NativeUi.Button(periods.transform, UiText.T("Текущий месяц", "This month"), _font, () => { _monthOnly = true; Render(); }, _monthOnly);
        var report = FinanceReport.Build(_scope, _monthOnly);
        NativeUi.Label(_body.transform, UiText.T("Текущий баланс выбранных бюджетов: ", "Current balance of selected wallets: ") + UiText.Money(report.Balance), _font, 24, 44);
        NativeUi.Label(_body.transform, UiText.T("Итог турнирных операций за период: ", "Net tournament transactions for this period: ") + UiText.Money(report.Net), _font, 22, 40);
        NativeUi.Label(_body.transform, UiText.T("Вклад составов сравнивайте по вкладкам. Старые операции без точной связи вынесены отдельно. Прокрутите список для остальных турниров.", "Compare roster contributions using the tabs. Older transactions without an exact event link are listed separately. Scroll to see more events."), _font, 18, 52);
        var scrollObject = NativeUi.Rect("EventsScroll", _body.transform);
        var scrollLayout = scrollObject.AddComponent<LayoutElement>(); scrollLayout.minHeight = 150; scrollLayout.flexibleHeight = 1;
        var scroll = scrollObject.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.scrollSensitivity = 40;
        var viewport = NativeUi.Rect("Viewport", scrollObject.transform); NativeUi.Stretch(viewport);
        viewport.AddComponent<Image>().color = new Color(0, 0, 0, .001f);
        viewport.AddComponent<RectMask2D>();
        var content = NativeUi.Column(viewport.transform, "EventRows", 0, 16);
        var contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0, 1); contentRect.anchorMax = new Vector2(1, 1); contentRect.pivot = new Vector2(.5f, 1);
        contentRect.offsetMin = Vector2.zero; contentRect.offsetMax = Vector2.zero;
        content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport = viewport.GetComponent<RectTransform>(); scroll.content = contentRect;
        var rows = report.Events.Values.OrderBy(x => x.Id == "unlinked").ThenByDescending(x => x.Edition, StringComparer.Ordinal).ThenBy(x => x.Name, StringComparer.CurrentCulture).ToArray();
        if (rows.Length == 0) NativeUi.Label(content.transform, UiText.T("За выбранный период турнирных операций нет.", "No tournament transactions for this period."), _font, 22, 80);
        foreach (var row in rows)
        {
            var card = NativeUi.Column(content.transform, "EventCard", 16, 4);
            NativeUi.Height(card, 152);
            card.AddComponent<Image>().color = new Color(.15f, .18f, .24f, 1);
            NativeUi.Label(card.transform, row.Name + (row.Edition == "" ? "" : " · " + row.Edition), _font, 23, 40);
            var values = NativeUi.Row(card.transform, "Values", 42);
            foreach (var value in new[] {
                (UiText.T("Призовые", "Prizes"), row.Prize), (UiText.T("Наклейки", "Stickers"), row.Stickers),
                (UiText.T("Билеты", "Flights"), row.Tickets), (UiText.T("Отель", "Hotel"), row.Hotel) })
                NativeUi.Label(values.transform, value.Item1 + ": " + UiText.Money(value.Item2), _font, 20, 42);
            NativeUi.Label(card.transform, UiText.T("Итог: ", "Net: ") + UiText.Money(row.Net), _font, 21, 30);
        }
    }
}

[HarmonyPatch(typeof(FinancesStickersSection), nameof(FinancesStickersSection.BuildStrip))]
internal static class FinanceReportTabPatch
{
    static void Postfix(FinancesStickersSection __instance)
    {
        try { FinanceReportPanel.Bind(__instance); }
        catch (Exception e) { Plugin.Logger.LogWarning($"Club finance tab unavailable: {e.Message}"); }
    }
}
