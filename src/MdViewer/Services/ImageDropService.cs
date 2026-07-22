using System.IO;
using System.Windows;
using ICSharpCode.AvalonEdit;

namespace MdViewer.Services;

public static class ImageDropService
{
    private static readonly string[] ImageExtensions =
        [".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".svg"];

    public static bool CanAccept(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return false;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
            return false;
        return files.Any(IsImagePath);
    }

    public static int InsertDroppedImages(TextEditor editor, string? documentPath, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files)
            return 0;

        var images = files.Where(IsImagePath).ToArray();
        if (images.Length == 0) return 0;

        var assetsDir = ResolveAssetsDirectory(documentPath);
        Directory.CreateDirectory(assetsDir);

        var inserted = 0;
        var offset = editor.SelectionLength > 0 ? editor.SelectionStart : editor.CaretOffset;
        var snippets = new List<string>();

        foreach (var source in images)
        {
            try
            {
                var fileName = MakeUniqueFileName(assetsDir, Path.GetFileName(source));
                var dest = Path.Combine(assetsDir, fileName);
                File.Copy(source, dest, overwrite: false);

                var relative = BuildRelativePath(documentPath, dest);
                var alt = Path.GetFileNameWithoutExtension(fileName);
                snippets.Add($"![{alt}]({relative.Replace('\\', '/')})");
                inserted++;
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Image drop failed for {source}", ex);
            }
        }

        if (snippets.Count == 0) return 0;

        var block = string.Join("\n", snippets);
        if (editor.SelectionLength > 0)
            editor.Document.Replace(offset, editor.SelectionLength, block);
        else
            editor.Document.Insert(offset, block);

        editor.CaretOffset = offset + block.Length;
        return inserted;
    }

    private static bool IsImagePath(string path)
    {
        var ext = Path.GetExtension(path);
        return ImageExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
    }

    private static string ResolveAssetsDirectory(string? documentPath)
    {
        var baseDir = !string.IsNullOrEmpty(documentPath)
            ? Path.GetDirectoryName(documentPath) ?? Environment.CurrentDirectory
            : Environment.CurrentDirectory;
        return Path.Combine(baseDir, "assets");
    }

    private static string BuildRelativePath(string? documentPath, string absoluteImagePath)
    {
        if (string.IsNullOrEmpty(documentPath))
            return Path.Combine("assets", Path.GetFileName(absoluteImagePath));

        var docDir = Path.GetDirectoryName(documentPath) ?? string.Empty;
        var relative = Path.GetRelativePath(docDir, absoluteImagePath);
        return relative;
    }

    private static string MakeUniqueFileName(string directory, string fileName)
    {
        var candidate = fileName;
        var name = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        var i = 1;
        while (File.Exists(Path.Combine(directory, candidate)))
        {
            candidate = $"{name}-{i}{ext}";
            i++;
        }

        return candidate;
    }
}
