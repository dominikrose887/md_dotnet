using System.IO;
using System.Net;
using System.Text.RegularExpressions;

namespace MdViewer.Services;

/// <summary>
/// Embeds local relative images as data URIs so standalone HTML exports keep pictures.
/// </summary>
public static class ExportImageService
{
    private static readonly Regex ImgSrcRegex = new(
        @"<img\b([^>]*?)\bsrc\s*=\s*([""'])(?<src>[^""']+)\2([^>]*)>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".svg"
    };

    public static string EmbedLocalImages(string html, string? documentDirectory)
    {
        if (string.IsNullOrWhiteSpace(html) || string.IsNullOrWhiteSpace(documentDirectory))
            return html;

        return ImgSrcRegex.Replace(html, match =>
        {
            var src = WebUtility.HtmlDecode(match.Groups["src"].Value.Trim());
            if (string.IsNullOrWhiteSpace(src) || IsRemoteOrData(src))
                return match.Value;

            var path = ResolveLocalPath(documentDirectory, src);
            if (path is null || !File.Exists(path))
                return match.Value;

            try
            {
                var ext = Path.GetExtension(path);
                if (!ImageExtensions.Contains(ext))
                    return match.Value;

                var mime = MimeFromExtension(ext);
                var bytes = File.ReadAllBytes(path);
                var dataUri = $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
                return $"<img{match.Groups[1].Value}src=\"{dataUri}\"{match.Groups[3].Value}>";
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Failed to embed image '{src}'", ex);
                return match.Value;
            }
        });
    }

    private static bool IsRemoteOrData(string src) =>
        src.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || src.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        || src.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
        || src.StartsWith("//", StringComparison.Ordinal);

    private static string? ResolveLocalPath(string documentDirectory, string src)
    {
        try
        {
            var cleaned = src.Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(cleaned))
                return Path.GetFullPath(cleaned);

            return Path.GetFullPath(Path.Combine(documentDirectory, cleaned));
        }
        catch
        {
            return null;
        }
    }

    private static string MimeFromExtension(string ext) => ext.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        ".svg" => "image/svg+xml",
        _ => "application/octet-stream"
    };
}
