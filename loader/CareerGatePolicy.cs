namespace DualRoster.Workshop;

internal enum LoaderPhase { WaitingForSteam, Downloading, Unsubscribed, Ready, Failed }
internal static class CareerGatePolicy
{
    internal static bool CanEnter(LoaderPhase phase, bool explicitWithoutMod) => phase == LoaderPhase.Ready || explicitWithoutMod;
}
