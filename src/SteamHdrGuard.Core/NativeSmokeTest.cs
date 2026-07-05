using System.Runtime.InteropServices;

namespace SteamHdrGuard.Core;

internal static class NativeSmokeTest
{
    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);
}
