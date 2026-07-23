using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MdViewer.Cleanup;

internal static class Program
{
    private const string AppFolderName = "MdViewer";

    private static int Main(string[] args)
    {
        var silent = args.Any(a => a.Equals("/silent", StringComparison.OrdinalIgnoreCase)
                                   || a.Equals("--silent", StringComparison.OrdinalIgnoreCase)
                                   || a.Equals("/S", StringComparison.OrdinalIgnoreCase));

        Console.WriteLine("MdViewer full cleanup");
        Console.WriteLine("This removes the app install (if found), settings, logs, WebView2 cache,");
        Console.WriteLine("shortcuts, and Explorer integration for the current Windows user.");
        Console.WriteLine();

        if (!silent)
        {
            Console.Write("Continue? [y/N]: ");
            var answer = Console.ReadLine()?.Trim();
            if (!string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Cancelled.");
                return 1;
            }
        }

        try
        {
            StopMdViewerProcesses();
            RemoveUserDataFolders();
            RemoveInstallFolders();
            RemoveShortcuts();
            RemoveExplorerIntegration();
            NotifyShell();

            Console.WriteLine();
            Console.WriteLine("Cleanup finished.");
            if (!silent)
            {
                Console.WriteLine("Press Enter to exit...");
                Console.ReadLine();
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Cleanup failed: " + ex.Message);
            if (!silent)
            {
                Console.WriteLine("Press Enter to exit...");
                Console.ReadLine();
            }

            return 2;
        }
    }

    private static void StopMdViewerProcesses()
    {
        Console.WriteLine("- Stopping MdViewer processes...");
        foreach (var process in Process.GetProcessesByName("MdViewer"))
        {
            try
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
            catch
            {
                // Ignore processes we cannot stop
            }
        }
    }

    private static void RemoveUserDataFolders()
    {
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolderName),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), AppFolderName),
        };

        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
            DeleteDirectoryForce(root);
    }

    private static void RemoveInstallFolders()
    {
        var candidates = new List<string>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppFolderName),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), AppFolderName),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), AppFolderName),
        };

        // Portable / custom installs next to this cleanup tool
        var sibling = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
        if (File.Exists(Path.Combine(sibling, "MdViewer.exe")))
            candidates.Add(sibling);

        var here = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (File.Exists(Path.Combine(here, "MdViewer.exe")))
            candidates.Add(here);

        // Avoid deleting the folder we're running from.
        var selfDir = Path.GetFullPath(AppContext.BaseDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        foreach (var dir in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(dir)) continue;

            var full = Path.GetFullPath(dir)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (string.Equals(full, selfDir, StringComparison.OrdinalIgnoreCase)
                || selfDir.StartsWith(full + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"- Skipping running folder: {full}");
                continue;
            }

            if (!Path.GetFileName(full).Equals(AppFolderName, StringComparison.OrdinalIgnoreCase)
                && !File.Exists(Path.Combine(full, "MdViewer.exe")))
                continue;

            DeleteDirectoryForce(full);
        }
    }

    private static void RemoveShortcuts()
    {
        Console.WriteLine("- Removing shortcuts...");
        var shortcuts = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "MdViewer.lnk"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "MdViewer.lnk"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "MdViewer"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "MdViewer.lnk"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs", "MdViewer"),
        };

        foreach (var path in shortcuts)
        {
            try
            {
                if (Directory.Exists(path))
                    DeleteDirectoryForce(path);
                else if (File.Exists(path))
                {
                    File.SetAttributes(path, FileAttributes.Normal);
                    File.Delete(path);
                    Console.WriteLine($"  deleted file: {path}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  skip shortcut {path}: {ex.Message}");
            }
        }
    }

    private static void RemoveExplorerIntegration()
    {
        Console.WriteLine("- Removing Explorer integration / registry...");
        DeleteKeyTree(Registry.CurrentUser, @"Software\Classes\MdViewer.md");
        DeleteKeyTree(Registry.CurrentUser, @"Software\Classes\Directory\shell\MdViewer");
        DeleteKeyTree(Registry.CurrentUser, @"Software\Classes\Directory\Background\shell\MdViewer");
        DeleteKeyTree(Registry.CurrentUser, @"Software\Classes\Folder\shell\MdViewer");
        DeleteKeyTree(Registry.CurrentUser, @"Software\Classes\Applications\MdViewer.exe");
        DeleteKeyTree(Registry.CurrentUser, @"Software\MdViewer");

        ClearProgIdIfOwned(@"Software\Classes\.md", "MdViewer.md");
        ClearProgIdIfOwned(@"Software\Classes\.markdown", "MdViewer.md");

        // Inno Setup uninstall leftovers for this product (current user)
        DeleteKeyTree(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{A7C3E8F1-4B2D-4E9A-9C1F-6D8B0A2E5F47}_is1");
    }

    private static void ClearProgIdIfOwned(string extKeyPath, string progId)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(extKeyPath, writable: true);
            if (key?.GetValue(null) is string value &&
                value.Equals(progId, StringComparison.OrdinalIgnoreCase))
            {
                key.DeleteValue("", throwOnMissingValue: false);
                Console.WriteLine($"  cleared default ProgId on {extKeyPath}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  skip {extKeyPath}: {ex.Message}");
        }
    }

    private static void DeleteKeyTree(RegistryKey root, string subKey)
    {
        try
        {
            root.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
            Console.WriteLine($"  deleted registry: {root.Name}\\{subKey}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  skip registry {subKey}: {ex.Message}");
        }
    }

    private static void DeleteDirectoryForce(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return;

        Console.WriteLine($"- Deleting folder: {path}");
        try
        {
            foreach (var info in new DirectoryInfo(path).EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
            {
                try { info.Attributes = FileAttributes.Normal; } catch { /* ignore */ }
            }

            Directory.Delete(path, recursive: true);
            Console.WriteLine("  ok");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  failed: {ex.Message}");
            // Retry best-effort file-by-file
            try
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                        File.Delete(file);
                    }
                    catch { /* ignore */ }
                }

                foreach (var dir in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories)
                             .OrderByDescending(d => d.Length))
                {
                    try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
                }

                Directory.Delete(path, recursive: true);
                Console.WriteLine("  ok (retry)");
            }
            catch (Exception retryEx)
            {
                Console.WriteLine($"  still locked: {retryEx.Message}");
            }
        }
    }

    private static void NotifyShell()
    {
        // SHCNE_ASSOCCHANGED
        SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero);
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
