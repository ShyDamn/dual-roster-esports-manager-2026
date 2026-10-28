namespace DualRoster.Shared;

internal static class GameLocale
{
    internal static string? Code()
    {
        try { return UnityEngine.Localization.Settings.LocalizationSettings.SelectedLocale?.Identifier.Code; }
        catch { return null; }
    }
}
