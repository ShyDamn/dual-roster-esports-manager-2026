using System;
using System.Text;
using System.Runtime.InteropServices;

// Flat Steamworks exports, matching the game's shipped Steam API / UGC v020.
public static class DualRosterSteam
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr LoadLibrary(string path);
    [DllImport("steam_api64", EntryPoint = "SteamAPI_InitFlat", CallingConvention = CallingConvention.Cdecl)]
    private static extern int InitFlat([Out] byte[] error);
    public static string LastError = "";
    public static bool SteamAPI_Init()
    {
        byte[] error = new byte[1024];
        int result = InitFlat(error);
        int length = Array.IndexOf(error, (byte)0); if (length < 0) length = error.Length;
        LastError = Encoding.UTF8.GetString(error, 0, length);
        return result == 0;
    }
    [DllImport("steam_api64", CallingConvention = CallingConvention.Cdecl)]
    public static extern void SteamAPI_Shutdown();
    [DllImport("steam_api64", CallingConvention = CallingConvention.Cdecl)]
    public static extern void SteamAPI_RunCallbacks();
    [DllImport("steam_api64", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr SteamAPI_SteamUGC_v020();
    [DllImport("steam_api64", CallingConvention = CallingConvention.Cdecl)]
    public static extern uint SteamAPI_ISteamUGC_GetItemState(IntPtr self, ulong item);
    [DllImport("steam_api64", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SteamAPI_ISteamUGC_DownloadItem(IntPtr self, ulong item, [MarshalAs(UnmanagedType.I1)] bool priority);
    [DllImport("steam_api64", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SteamAPI_ISteamUGC_GetItemInstallInfo(IntPtr self, ulong item, out ulong size, [Out] byte[] folder, uint capacity, out uint timestamp);
    public static string Folder(IntPtr self, ulong item)
    {
        byte[] data = new byte[8192]; ulong size; uint timestamp;
        if (!SteamAPI_ISteamUGC_GetItemInstallInfo(self, item, out size, data, (uint)data.Length, out timestamp)) return null;
        int length = Array.IndexOf(data, (byte)0); if (length < 0) length = data.Length;
        return Encoding.UTF8.GetString(data, 0, length);
    }
}
