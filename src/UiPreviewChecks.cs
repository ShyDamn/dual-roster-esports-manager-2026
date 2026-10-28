#if DUALROSTER_TESTING
using UnityEngine;

namespace DualRoster;

public sealed class UiPreviewChecks : MonoBehaviour
{
    public UiPreviewChecks(IntPtr pointer) : base(pointer) { }
    private static float _start;
    private bool _captured;
    internal static void Begin() => _start = Time.unscaledTime;
    public void Update()
    {
        if (_start == 0) return;
        var path = Environment.GetEnvironmentVariable("DUALROSTER_UI_SCREENSHOT");
        if (string.IsNullOrEmpty(path)) return;
        if (!_captured && Time.unscaledTime > _start + 3)
        {
            _captured = true; ScreenCapture.CaptureScreenshot(path);
        }
        if (_captured && Time.unscaledTime > _start + 6)
        {
            _start = 0;
            if (File.Exists(path)) Plugin.Logger.LogInfo("UI CAPTURE PASSED: " + path);
            else Plugin.Logger.LogError("UI CAPTURE FAILED: " + path);
            Application.Quit();
        }
    }
}
#endif
