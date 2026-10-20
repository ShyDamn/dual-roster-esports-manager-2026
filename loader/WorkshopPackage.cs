using System.Security.Cryptography;
using System.Text.Json;

namespace DualRoster.Workshop;

// Steam's EItemState values. Kept independent of Unity so the download gate can be tested.
internal enum PackageState { Unsubscribed, WaitingForDownload, Ready }

internal sealed class WorkshopPackage
{
    internal const ulong ItemId = 3819071177;
    internal const string GameHash = "F915DE7BD84B5C8D52D73789500BFC74E0108920EBE5FBABFCE1FC1DD24D60F9";
    internal const string Compatibility = "em2026-26012398-interop107-v1";
    public int Schema { get; set; }
    public int LoaderApi { get; set; }
    public ulong WorkshopId { get; set; }
    public string CompatibilityId { get; set; } = "";
    public string Version { get; set; } = "";
    public string Sha256 { get; set; } = "";

    internal static PackageState State(uint flags)
    {
        if ((flags & 1) == 0) return PackageState.Unsubscribed;
        // Installed=4, NeedsUpdate=8, Downloading=16, DownloadPending=32.
        return (flags & 4) != 0 && (flags & (8 | 16 | 32)) == 0
            ? PackageState.Ready : PackageState.WaitingForDownload;
    }

    internal static WorkshopPackage Read(string directory)
    {
        var path = Path.Combine(directory, "runtime", "workshop-loader.json");
        if (new FileInfo(path).Length > 16_384) throw new InvalidDataException("Invalid Workshop manifest size.");
        var package = JsonSerializer.Deserialize<WorkshopPackage>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Missing Workshop manifest.");
        if (package.Schema != 1 || package.LoaderApi != 1 || package.WorkshopId != ItemId ||
            package.CompatibilityId != Compatibility || !System.Version.TryParse(package.Version, out _) ||
            package.Sha256.Length != 64 || !package.Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("Incompatible Workshop package. Check the item's installation notes.");
        return package;
    }

    // Load a verified snapshot so Steam can replace its files while the game is running.
    // The cache is never used as an offline fallback when Steam reports an unsubscribe.
    internal string Snapshot(string directory, string cacheRoot)
    {
        var bytes = File.ReadAllBytes(Path.Combine(directory, "runtime", "DualRoster.dll"));
        if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Workshop DLL checksum mismatch. Wait for Steam to finish downloading.");
        var cache = Path.Combine(cacheRoot, Sha256.ToUpperInvariant());
        Directory.CreateDirectory(cache);
        var target = Path.Combine(cache, "DualRoster.dll");
        // Always write the verified bytes, including when a previous cache copy was damaged.
        var temp = Path.Combine(cache, Guid.NewGuid().ToString("N") + ".tmp");
        try { File.WriteAllBytes(temp, bytes); File.Move(temp, target, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        return cache;
    }
}
