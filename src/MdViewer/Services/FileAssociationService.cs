using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace MdViewer.Services;

public static class FileAssociationService
{
    private const string ProgId = "MdViewer.md";
    private const string FileDescription = "Markdown Document";
    private const string AppName = "MdViewer";
    private const string DirectoryShellKey = @"Software\Classes\Directory\shell\MdViewer";
    private const string DirectoryBackgroundShellKey = @"Software\Classes\Directory\Background\shell\MdViewer";
    private const string ContextMenuLabel = "Open with MdViewer";

    public static bool IsRegistered()
    {
        try
        {
            using var progIdKey = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ProgId}\shell\open\command");
            if (progIdKey?.GetValue(null) is not string command) return false;

            var exePath = GetExecutablePath();
            if (!command.Contains(exePath, StringComparison.OrdinalIgnoreCase))
                return false;

            using var folderKey = Registry.CurrentUser.OpenSubKey($@"{DirectoryShellKey}\command");
            return folderKey?.GetValue(null) is string folderCommand
                   && folderCommand.Contains(exePath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static void Register()
    {
        var exePath = GetExecutablePath();
        var openFileCommand = $"\"{exePath}\" \"%1\"";
        var openDirectoryCommand = $"\"{exePath}\" \"%1\"";
        var openBackgroundCommand = $"\"{exePath}\" \"%V\"";

        using (var extKey = Registry.CurrentUser.CreateSubKey(@"Software\Classes\.md"))
        {
            extKey.SetValue(null, ProgId);
            extKey.SetValue("Content Type", "text/markdown");
        }

        using (var markdownExt = Registry.CurrentUser.CreateSubKey(@"Software\Classes\.markdown"))
        {
            markdownExt.SetValue(null, ProgId);
            markdownExt.SetValue("Content Type", "text/markdown");
        }

        using (var progIdKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}"))
        {
            progIdKey.SetValue(null, FileDescription);

            using var commandKey = progIdKey.CreateSubKey(@"shell\open\command");
            commandKey.SetValue(null, openFileCommand);
        }

        using (var appKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\Applications\{Path.GetFileName(exePath)}"))
        {
            appKey.SetValue("FriendlyAppName", AppName);
            using var openCommand = appKey.CreateSubKey(@"shell\open\command");
            openCommand.SetValue(null, openFileCommand);
        }

        // Right-click a folder in Explorer
        RegisterDirectoryVerb(DirectoryShellKey, exePath, openDirectoryCommand);
        // Right-click empty space inside a folder
        RegisterDirectoryVerb(DirectoryBackgroundShellKey, exePath, openBackgroundCommand);

        NativeMethods.SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero);
    }

    public static void Unregister()
    {
        try
        {
            DeleteKeyTree(@"Software\Classes\" + ProgId);
            DeleteKeyTree(DirectoryShellKey);
            DeleteKeyTree(DirectoryBackgroundShellKey);
            // Legacy key from earlier builds (avoid leaving leftovers)
            DeleteKeyTree(@"Software\Classes\Folder\shell\MdViewer");

            foreach (var ext in new[] { ".md", ".markdown" })
            {
                using var extKey = Registry.CurrentUser.OpenSubKey(@"Software\Classes\" + ext, writable: true);
                if (extKey?.GetValue(null) is string value &&
                    value.Equals(ProgId, StringComparison.OrdinalIgnoreCase))
                {
                    extKey.DeleteValue(string.Empty, throwOnMissingValue: false);
                }
            }

            var exeName = Path.GetFileName(GetExecutablePath());
            DeleteKeyTree($@"Software\Classes\Applications\{exeName}");

            NativeMethods.SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero);
        }
        catch
        {
            // Best-effort unregister
        }
    }

    private static void RegisterDirectoryVerb(string shellKeyPath, string exePath, string command)
    {
        using var verbKey = Registry.CurrentUser.CreateSubKey(shellKeyPath);
        verbKey.SetValue(null, ContextMenuLabel);
        verbKey.SetValue("Icon", $"\"{exePath}\",0");

        using var commandKey = verbKey.CreateSubKey("command");
        commandKey.SetValue(null, command);
    }

    private static void DeleteKeyTree(string subKey)
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
        }
        catch
        {
            // Ignore
        }
    }

    private static string GetExecutablePath()
    {
        var path = Environment.ProcessPath
                   ?? Process.GetCurrentProcess().MainModule?.FileName
                   ?? throw new InvalidOperationException("Could not resolve application path.");
        return Path.GetFullPath(path);
    }

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("shell32.dll")]
        public static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
    }
}
