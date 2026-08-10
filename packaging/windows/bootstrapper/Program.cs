using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Catnip.WindowsBootstrapper;

internal static class Program
{
    private const string PayloadResourceName = "Catnip.Payload.zip";
    private const string ProductVersion = "0.0.0";

    [STAThread]
    private static int Main()
    {
        try
        {
            using Stream payload = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResourceName)
                ?? throw new InvalidDataException("安装包中缺少内嵌的 Catnip 套件。");

            string payloadHash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant()[..12];
            payload.Position = 0;
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string installRoot = Path.Combine(localAppData, "Catnip", $"app-{ProductVersion}-{payloadHash}");
            string desktopPath = Path.Combine(installRoot, "Catnip.Desktop.exe");

            if (!File.Exists(desktopPath))
            {
                ExtractPayload(payload, installRoot);
            }

            if (!File.Exists(desktopPath))
            {
                throw new FileNotFoundException("解包完成后仍未找到 Windows Desktop。", desktopPath);
            }

            Process.Start(
                new ProcessStartInfo(desktopPath)
                {
                    UseShellExecute = true,
                    WorkingDirectory = installRoot,
                });
            return 0;
        }
        catch (Exception exception)
        {
            ShowMessage(
                IntPtr.Zero,
                $"Catnip 无法启动。\n\n{exception.Message}",
                "Catnip 0.0",
                0x10);
            return 1;
        }
    }

    private static void ExtractPayload(Stream payload, string installRoot)
    {
        string stagingRoot = $"{installRoot}.staging-{Guid.NewGuid():N}";
        Directory.CreateDirectory(stagingRoot);
        try
        {
            string safeRoot = Path.GetFullPath(stagingRoot) + Path.DirectorySeparatorChar;
            using var archive = new ZipArchive(payload, ZipArchiveMode.Read, leaveOpen: true);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string destination = Path.GetFullPath(Path.Combine(stagingRoot, entry.FullName));
                if (!destination.StartsWith(safeRoot, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("安装包包含不安全的文件路径。");
                }

                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(destination);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, overwrite: true);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(installRoot)!);
            if (Directory.Exists(installRoot))
            {
                throw new IOException("目标版本目录已存在但内容不完整，请重新下载发布文件。");
            }

            Directory.Move(stagingRoot, installRoot);
        }
        finally
        {
            if (Directory.Exists(stagingRoot))
            {
                Directory.Delete(stagingRoot, recursive: true);
            }
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int ShowMessage(IntPtr window, string text, string caption, uint type);
}
