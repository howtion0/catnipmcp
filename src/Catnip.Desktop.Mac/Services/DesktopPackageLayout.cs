using System.IO;

namespace Catnip.Desktop.Mac.Services;

public static class DesktopPackageLayout
{
    public static string GetDemoApiPath(string baseDirectory, bool windows)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        string root = GetPackageResourceRoot(baseDirectory, windows);
        return Path.Combine(root, "DemoApi", windows ? "Catnip.DemoApi.exe" : "Catnip.DemoApi");
    }

    public static string GetRuntimePath(string baseDirectory, bool windows)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        string root = GetPackageResourceRoot(baseDirectory, windows);
        return Path.Combine(root, "Runtime", windows ? "Catnip.Runtime.exe" : "Catnip.Runtime");
    }

    public static string GetWorkBuddyBridgePath(string baseDirectory, bool windows)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        string root = GetPackageResourceRoot(baseDirectory, windows);
        return Path.Combine(
            root,
            "WorkBuddyBridge",
            windows ? "Catnip.WorkBuddyBridge.exe" : "Catnip.WorkBuddyBridge");
    }

    private static string GetPackageResourceRoot(string baseDirectory, bool windows)
    {
        string normalizedBase = Path.GetFullPath(baseDirectory);
        return windows
            ? normalizedBase
            : Path.GetFullPath(Path.Combine(normalizedBase, "..", "Resources"));
    }
}
