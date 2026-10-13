using DualRoster.Workshop;
using System.Security.Cryptography;
using System.Text.Json;

var root = Path.Combine(Path.GetTempPath(), "DualRosterChecks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(root, "runtime"));
int count = 0;
void Check(bool condition, string label) { if (!condition) throw new Exception(label); Console.WriteLine("PASS " + label); count++; }
void Reject(Action action, string label) { try { action(); } catch (Exception e) when (e is InvalidDataException or IOException or JsonException) { Check(true, label); return; } throw new Exception("Accepted " + label); }
void Write(WorkshopPackage p) => File.WriteAllText(Path.Combine(root, "runtime", "workshop-loader.json"), JsonSerializer.Serialize(p));
WorkshopPackage Manifest(byte[] bytes) => new() { Schema = 1, LoaderApi = 1, WorkshopId = WorkshopPackage.ItemId, CompatibilityId = WorkshopPackage.Compatibility, Version = "0.3.0", Sha256 = Convert.ToHexString(SHA256.HashData(bytes)) };
var dll = Path.Combine(root, "runtime", "DualRoster.dll");
var cache = Path.Combine(root, "cache");

Check(WorkshopPackage.State(0) == PackageState.Unsubscribed, "No subscription");
Check(WorkshopPackage.State(4) == PackageState.Unsubscribed, "Cached files do not override unsubscribe");
Check(WorkshopPackage.State(1) == PackageState.WaitingForDownload, "New subscription waits for installation");
foreach (uint bit in new uint[] { 8, 16, 32 }) Check(WorkshopPackage.State(5 | bit) == PackageState.WaitingForDownload, "Never load while updating: " + bit);
Check(WorkshopPackage.State(5) == PackageState.Ready, "Subscribed installed content is ready");
byte[] first = { 1, 3, 5, 7 };
File.WriteAllBytes(dll, first); Write(Manifest(first));
var package = WorkshopPackage.Read(root);
var snapshot = package.Snapshot(root, cache);
Check(File.ReadAllBytes(Path.Combine(snapshot, "DualRoster.dll")).SequenceEqual(first), "Verified snapshot matches package");
File.WriteAllBytes(Path.Combine(snapshot, "DualRoster.dll"), new byte[] { 0 });
package.Snapshot(root, cache);
Check(File.ReadAllBytes(Path.Combine(snapshot, "DualRoster.dll")).SequenceEqual(first), "Damaged cache is replaced");
byte[] updated = { 2, 4, 6, 8 };
File.WriteAllBytes(dll, updated);
Reject(() => package.Snapshot(root, cache), "Partial update/hash mismatch rejected");
var next = Manifest(updated); next.Version = "0.3.1"; Write(next);
var nextSnapshot = WorkshopPackage.Read(root).Snapshot(root, cache);
Check(snapshot != nextSnapshot, "Update gets a separate snapshot");
Check(File.ReadAllBytes(Path.Combine(snapshot, "DualRoster.dll")).SequenceEqual(first), "Running version remains unchanged");
Check(File.ReadAllBytes(Path.Combine(nextSnapshot, "DualRoster.dll")).SequenceEqual(updated), "Next launch sees updated bytes");
foreach (var kind in new[] { "schema", "api", "item", "compatibility", "version", "hash" })
{
    var invalid = Manifest(updated);
    switch (kind) { case "schema": invalid.Schema = 2; break; case "api": invalid.LoaderApi = 2; break; case "item": invalid.WorkshopId = 1; break; case "compatibility": invalid.CompatibilityId = "other-build"; break; case "version": invalid.Version = "../bad"; break; case "hash": invalid.Sha256 = new string('Z', 64); break; }
    Write(invalid); Reject(() => WorkshopPackage.Read(root), "Incompatible " + kind);
}
Write(Manifest(updated)); File.Delete(dll);
Reject(() => WorkshopPackage.Read(root).Snapshot(root, cache), "Missing DLL rejected without cached fallback");
File.WriteAllText(Path.Combine(root, "runtime", "workshop-loader.json"), "{");
Reject(() => WorkshopPackage.Read(root), "Truncated manifest rejected");
File.WriteAllText(Path.Combine(root, "runtime", "workshop-loader.json"), new string(' ', 20000));
Reject(() => WorkshopPackage.Read(root), "Oversized manifest rejected");
Console.WriteLine($"{count} Workshop checks passed. Temporary fixtures: {root}");
