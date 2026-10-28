using System.Globalization;

namespace DualRoster.Shared;

internal static class UiText
{
    internal static Func<string?>? LanguageProvider;
    internal static bool IsRussian
    {
        get { try { return (LanguageProvider?.Invoke() ?? CultureInfo.CurrentUICulture.Name).StartsWith("ru", StringComparison.OrdinalIgnoreCase); } catch { return false; } }
    }
    internal static CultureInfo Culture => CultureInfo.GetCultureInfo(IsRussian ? "ru-RU" : "en-US");
    internal static string T(string ru, string en) => IsRussian ? ru : en;
    internal static string Money(long amount) => (amount < 0 ? "-$" : "$") + Math.Abs((decimal)amount).ToString("N0", Culture);
    internal static string Main => T("Основа", "Main team");
    internal static string Academy => T("Академия", "Academy");
    internal static string Club => T("Клуб", "Club");
    internal static string Decline => T("Отказаться", "Decline");
    internal static string Register(bool academy) => academy ? T("Заявить академию", "Register academy") : T("Заявить основу", "Register main");
    internal static string Registered(bool academy) => academy ? T("Академия заявлена", "Academy registered") : T("Основа заявлена", "Main registered");
    internal static string GateTitle => T("Мод ещё не готов", "The mod is not ready");
    internal static string GateWarning => T("Загрузка карьеры без Dual Roster может снять совместную заявку академии. Дождитесь мода или явно подтвердите запуск без него.", "Loading a career without Dual Roster may remove the academy's shared tournament entry. Wait for the mod or explicitly confirm loading without it.");
    internal static string Waiting => T("Ожидание Steam", "Waiting for Steam");
    internal static string Downloading => T("Загрузка Workshop…", "Downloading Workshop…");
    internal static string Unsubscribed => T("Нет подписки на Dual Roster", "Not subscribed to Dual Roster");
    internal static string Failed => T("Мод не загружен. Подробности: BepInEx/LogOutput.log", "Mod failed to load. Details: BepInEx/LogOutput.log");
    internal static string Active(string version) => T($"{version} включён", $"{version} active");
}
