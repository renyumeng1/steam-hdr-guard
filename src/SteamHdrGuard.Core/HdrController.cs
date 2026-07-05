using System.Runtime.InteropServices;

namespace SteamHdrGuard.Core;

public sealed class HdrController
{
    public IReadOnlyList<DisplayInfo> GetDisplays() => Array.Empty<DisplayInfo>();
    public bool IsAnyHdrEnabled() => false;
    public int SetHdrForAllSupportedDisplays(bool enabled) => throw new NotSupportedException("HDR native controller is not implemented yet.");
}
