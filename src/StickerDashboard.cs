using HarmonyLib;
using Il2CppInterop.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DualRoster.Shared;

namespace DualRoster;

internal static class StickerDashboard
{
    [ThreadStatic] internal static DataTeam? Context;
    private static readonly Dictionary<IntPtr, (string Club, bool Academy)> Choices = new();
    private static readonly Dictionary<IntPtr, (FinancesStickersSection Section, TMP_Text Main, TMP_Text Academy)> Labels = new();

    internal static void RefreshLabels()
    {
        foreach (var entry in Labels.Values.ToArray())
        {
            if (entry.Main == null || entry.Academy == null) continue;
            var main = GlobalValues.DataTeam; var academy = main?.GetAcademyTeam();
            entry.Main.text = UiText.Main + " · " + main?.Name;
            entry.Academy.text = UiText.Academy + " · " + (academy?.Name ?? UiText.T("нет состава", "no roster"));
        }
    }

    internal static DataTeam? Team(FinancesStickersSection section)
    {
        var main = GlobalValues.DataTeam;
        return main != null && Choices.TryGetValue(section.Pointer, out var choice) && choice.Club == main.Id && choice.Academy
            ? main.GetAcademyTeam() ?? main : main;
    }

    internal static void Select(FinancesStickersSection section, bool academy)
    {
        var main = GlobalValues.DataTeam;
        if (main == null || (academy && main.GetAcademyTeam() == null)) return;
        Choices[section.Pointer] = (main.Id, academy);
        section._built = false;
        section.Fill();
        Plugin.Logger.LogInfo($"Sticker dashboard roster: {Team(section)?.Name}");
    }

    internal static void AddSelector(FinancesStickersSection section)
    {
        if (section._content == null || section._style == null || section._content.Find("DualRosterStickerSelector") != null) return;
        var main = GlobalValues.DataTeam;
        if (main == null) return;
        var academy = main.GetAcademyTeam();
        var selected = Team(section);
        var row = Rect("DualRosterStickerSelector", section._content);
        row.transform.SetAsFirstSibling();
        var height = row.AddComponent<LayoutElement>();
        height.minHeight = height.preferredHeight = 44;
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 12;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        var mainLabel = MakeButton(row.transform, section, false, UiText.Main + " · " + main.Name, true, selected?.Id == main.Id);
        var academyLabel = MakeButton(row.transform, section, true, UiText.Academy + " · " + (academy?.Name ?? UiText.T("нет состава", "no roster")), academy != null, selected?.Id == academy?.Id);
        Labels[section.Pointer] = (section, mainLabel, academyLabel);
        LayoutRebuilder.ForceRebuildLayoutImmediate(section._content.Cast<RectTransform>());
    }

    private static TMP_Text MakeButton(Transform parent, FinancesStickersSection section, bool academy, string title, bool enabled, bool selected)
    {
        var obj = Rect(academy ? "Academy" : "Main", parent);
        var height = obj.AddComponent<LayoutElement>();
        height.minHeight = height.preferredHeight = 44;
        height.flexibleWidth = 1;
        var image = obj.AddComponent<Image>();
        image.color = selected ? new Color(.05f, .39f, .36f, 1) : new Color(.16f, .20f, .27f, 1);
        var button = obj.AddComponent<Button>();
        button.targetGraphic = image;
        button.interactable = enabled;
        button.onClick.AddListener((UnityEngine.Events.UnityAction)(Action)(() => Select(section, academy)));
        var text = Rect("Caption", obj.transform);
        var rect = text.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(12, 3); rect.offsetMax = new Vector2(-12, -3);
        var label = text.AddComponent<TextMeshProUGUI>();
        label.font = section._style.font;
        label.fontSharedMaterial = section._style.fontSharedMaterial;
        label.fontSize = 20;
        label.enableAutoSizing = true; label.fontSizeMin = 14; label.fontSizeMax = 20;
        label.enableWordWrapping = true; label.richText = false;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        label.text = title;
        return label;
    }

    private static GameObject Rect(string name, Transform parent)
    {
        var obj = new GameObject(name, new Il2CppSystem.Type[] { Il2CppType.Of<RectTransform>() });
        obj.transform.SetParent(parent, false);
        return obj;
    }
}

[HarmonyPatch(typeof(FinancesStickersSection), nameof(FinancesStickersSection.Fill))]
internal static class StickerDashboardFillPatch
{
    static void Prefix(FinancesStickersSection __instance, out DataTeam? __state)
    { __state = StickerDashboard.Context; StickerDashboard.Context = StickerDashboard.Team(__instance); }
    static void Postfix(FinancesStickersSection __instance)
    {
        try { StickerDashboard.AddSelector(__instance); }
        catch (Exception e) { Plugin.Logger.LogError($"Sticker roster selector: {e}"); }
    }
    static void Finalizer(DataTeam? __state) => StickerDashboard.Context = __state;
}

// This helper is called by native Fill (not inlined). Scope the replacement to
// that synchronous render; global club state and payment calculations are untouched.
[HarmonyPatch(typeof(FinancesStickersSection), nameof(FinancesStickersSection.SetsOf))]
internal static class StickerDashboardTeamPatch
{
    static void Prefix(ref DataTeam __0)
    { if (StickerDashboard.Context != null) __0 = StickerDashboard.Context; }
}
