#if DUALROSTER_TESTING
using Il2CppInterop.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DualRoster;

internal static class DashboardChecks
{
    internal static void Run()
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); Plugin.Logger.LogInfo("PASS: " + message); }
        var main = GlobalValues.DataTeam; var academy = main.GetAcademyTeam();
        var mainBudget = DataFinances.LedgerOfUserSquad(main).TotalBudget;
        var academyBudget = DataFinances.LedgerOfUserSquad(academy).TotalBudget;
        TMP_Text? style = null;
        foreach (var label in Resources.FindObjectsOfTypeAll<TextMeshProUGUI>())
            if (label.font != null) { style = label; break; }
        Check(style != null, "dashboard has native font template");
        var root = new GameObject("DualRosterDashboardTest", new Il2CppSystem.Type[] { Il2CppType.Of<RectTransform>() });
        root.AddComponent<Canvas>();
        var section = root.AddComponent<FinancesStickersSection>();
        section._style = style;
        var body = new GameObject("Content", new Il2CppSystem.Type[] { Il2CppType.Of<RectTransform>() });
        body.transform.SetParent(root.transform, false);
        body.GetComponent<RectTransform>().sizeDelta = new Vector2(1800, 1400);
        body.AddComponent<VerticalLayoutGroup>();
        section._content = body.transform;
        string[] Texts() => body.GetComponentsInChildren<TextMeshProUGUI>(true).Select(t => t.text).ToArray();
        section.Fill();
        Check(StickerDashboard.Team(section)?.Id == main.Id, "main is the default sticker roster");
        var selector = body.transform.Find("DualRosterStickerSelector");
        Check(selector != null && selector.childCount == 2, "native sticker page has two roster buttons");
        selector!.Find("Academy").GetComponent<Button>().onClick.Invoke();
        Check(StickerDashboard.Team(section)?.Id == academy.Id, "academy button changes selected roster");
        var texts = Texts();
        long earned = 0;
        for (int i=0; i<academy.StickerPayouts.Count; i++) earned += academy.StickerPayouts[i].Earned;
        Check(texts.Contains(FinancesStickersSection.Money(earned)), "academy cumulative earnings rendered");
        var payout = academy.StickerPayouts[academy.StickerPayouts.Count-1];
        var nicks = new List<string>();
        for(int i=0;i<payout.Stickers.Count;i++) if(!payout.Stickers[i].IsTeamSticker) nicks.Add(payout.Stickers[i].Nick);
        Check(nicks.Count >= 5 && nicks.All(n => texts.Contains(n)), "academy autograph nicknames rendered");
        Check(texts.Contains(FinancesStickersSection.Money(payout.Remaining)), "academy remaining sticker sales rendered");
        Check(StickerDashboard.Context == null && GlobalValues.DataTeam.Id == main.Id, "sticker render restores context without changing the club");
        Check(FinancesStickersSection.SetsOf(main).Count == main.StickerPayouts.Count, "other native calls still read their requested roster");
        var uniqueNick=nicks.FirstOrDefault(n => !Enumerable.Range(0,main.StickerPayouts.Count).Any(i => Enumerable.Range(0,main.StickerPayouts[i].Stickers.Count).Any(j=>main.StickerPayouts[i].Stickers[j].Nick==n)));
        body.transform.Find("DualRosterStickerSelector").Find("Main").GetComponent<Button>().onClick.Invoke();
        Check(StickerDashboard.Team(section)?.Id == main.Id && (uniqueNick == null || !Texts().Contains(uniqueNick)), "switching back restores main-team autograph data");
        Check(DataFinances.LedgerOfUserSquad(main).TotalBudget == mainBudget && DataFinances.LedgerOfUserSquad(academy).TotalBudget == academyBudget, "view switching never changes either budget");
        UnityEngine.Object.Destroy(root);
        Plugin.Logger.LogInfo("STICKER DASHBOARD CHECKS PASSED");
    }
}
#endif
