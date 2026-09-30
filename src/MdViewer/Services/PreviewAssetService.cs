using System.IO;
using System.Text.RegularExpressions;

namespace MdViewer.Services;

/// <summary>
/// Plans WebView2 virtual-host mapping so relative preview assets work,
/// including paths that leave the document folder (e.g. <c>../.attachments/img.png</c>).
/// </summary>
public static class PreviewAssetService
{
    // ![alt](url) or [text](url) — captures the raw URL target.
    private static readonly Regex MarkdownUrlRegex = new(
        @"!\[[^\]]*\]\(\s*(?<url><[^>\r\n]+>|[^)\s]+)"
        + @"|"
        + @"(?<!!)\[[^\]]*\]\(\s*(?<url><[^>\r\n]+>|[^)\s]+)",
        RegexOptions.Compiled);

    private static readonly Regex HtmlSrcHrefRegex = new(
        @"\b(?:src|href)\s*=\s*([""'])(?<url>[^""']+)\1",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public sealed record ResourcePlan(string? RootFolder, string? BaseHref);

    public static ResourcePlan Plan(
        string? documentPath,
        string? workspacePath,
        string markdown,
        string virtualHostName)
    {
        var docDir = TryGetDocumentDirectory(documentPath);
        if (docDir is null)
        {
            if (!string.IsNullOrEmpty(workspacePath) && Directory.Exists(workspacePath))
            {
                var ws = Path.GetFullPath(workspacePath);
                return new ResourcePlan(ws, $"https://{virtualHostName}/");
            }

            return new ResourcePlan(null, null);
        }

        if (!string.IsNullOrEmpty(workspacePath) && Directory.Exists(workspacePath))
        {
            var ws = Path.GetFullPath(workspacePath);
            if (IsUnderOrEqual(docDir, ws))
                return new ResourcePlan(ws, BuildBaseHref(ws, docDir, virtualHostName));
        }

        var roots = new List<string> { docDir };
        foreach (var relative in EnumerateLocalRelativePaths(markdown))
        {
            var resolved = TryResolveExistingFile(docDir, relative);
            if (resolved is null) continue;

            var assetDir = Path.GetDirectoryName(resolved);
            if (!string.IsNullOrEmpty(assetDir))
                roots.Add(Path.GetFullPath(assetDir));
        }

        var common = GetCommonDirectory(roots) ?? docDir;
        if (IsDriveRoot(common))
            common = docDir;

        return new ResourcePlan(common, BuildBaseHref(common, docDir, virtualHostName));
    }

    public static string BuildBaseHref(string rootFolder, string documentDirectory, string virtualHostName)
    {
        rootFolder = Path.GetFullPath(rootFolder);
        documentDirectory = Path.GetFullPath(documentDirectory);

        if (string.Equals(rootFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                documentDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
            return $"https://{virtualHostName}/";

        var relative = Path.GetRelativePath(rootFolder, documentDirectory)
            .Replace('\\', '/')
            .Trim('/');

        if (string.IsNullOrEmpty(relative) || relative is ".")
            return $"https://{virtualHostName}/";

        return $"https://{virtualHostName}/{relative}/";
    }

    private static string? TryGetDocumentDirectory(string? documentPath)
    {
        if (string.IsNullOrWhiteSpace(documentPath)) return null;
        try
        {
            var full = Path.GetFullPath(documentPath);
            var dir = Path.GetDirectoryName(full);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;
            return Path.GetFullPath(dir);
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<string> EnumerateLocalRelativePaths(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
            yield break;

        foreach (Match match in MarkdownUrlRegex.Matches(markdown))
        {
            if (TryNormalizeLocalRelative(match.Groups["url"].Value) is { } url)
                yield return url;
        }

        foreach (Match match in HtmlSrcHrefRegex.Matches(markdown))
        {
            if (TryNormalizeLocalRelative(match.Groups["url"].Value) is { } url)
                yield return url;
        }
    }

    private static string? TryNormalizeLocalRelative(string raw)
    {
        var url = raw.Trim();
        if (url.Length == 0) return null;

        if (url.StartsWith('<') && url.EndsWith('>') && url.Length > 2)
            url = url[1..^1].Trim();

        // Drop optional title fragments that slipped through: url "title"
        var space = url.IndexOfAny([' ', '\t']);
        if (space > 0)
            url = url[..space];

        if (url.Length == 0 || url.StartsWith('#'))
            return null;

        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("//", StringComparison.Ordinal))
            return null;

        // Absolute Windows / POSIX paths are not relative asset refs for mapping.
        if (Path.IsPathRooted(url.Replace('/', Path.DirectorySeparatorChar)))
            return null;

        return url;
    }

    private static string? TryResolveExistingFile(string documentDirectory, string relativeUrl)
    {
        try
        {
            var cleaned = Uri.UnescapeDataString(relativeUrl)
                .Replace('/', Path.DirectorySeparatorChar);
            var full = Path.GetFullPath(Path.Combine(documentDirectory, cleaned));
            return File.Exists(full) ? full : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? GetCommonDirectory(IReadOnlyList<string> directories)
    {
        if (directories.Count == 0) return null;

        var normalized = directories
            .Select(d => Path.GetFullPath(d)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalized.Count == 1)
            return normalized[0];

        var split = normalized
            .Select(p => p.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .ToList();

        var minLen = split.Min(p => p.Length);
        var common = new List<string>();
        for (var i = 0; i < minLen; i++)
        {
            var segment = split[0][i];
            if (!split.All(p => string.Equals(p[i], segment, StringComparison.OrdinalIgnoreCase)))
                break;
            common.Add(segment);
        }

        return common.Count == 0 ? null : string.Join(Path.DirectorySeparatorChar, common);
    }

    private static bool IsUnderOrEqual(string path, string parent)
    {
        var p = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var r = Path.GetFullPath(parent)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return p.StartsWith(r, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDriveRoot(string path)
    {
        try
        {
            var full = Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var root = Path.GetPathRoot(full)
                ?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return !string.IsNullOrEmpty(root)
                   && string.Equals(full, root, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
