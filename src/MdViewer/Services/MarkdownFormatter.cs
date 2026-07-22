using System.Text;
using System.Text.RegularExpressions;

namespace MdViewer.Services;

public static class MarkdownFormatter
{
    private static readonly Regex TrailingWs = new(@"[ \t]+$", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex ExcessBlankLines = new(@"\n{3,}", RegexOptions.Compiled);
    private static readonly Regex HeadingLine = new(@"^(#{1,6})([^\s#])", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex UnorderedList = new(@"^([ \t]*)[*+] ", RegexOptions.Multiline | RegexOptions.Compiled);

    public static string Format(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
            return string.Empty;

        var text = markdown.Replace("\r\n", "\n").Replace('\r', '\n');
        text = TrailingWs.Replace(text, string.Empty);
        text = HeadingLine.Replace(text, "$1 $2");
        text = UnorderedList.Replace(text, "$1- ");
        text = ExcessBlankLines.Replace(text, "\n\n");
        text = EnsureBlankLineAroundHeadings(text);
        text = text.TrimEnd() + "\n";
        return text;
    }

    private static string EnsureBlankLineAroundHeadings(string text)
    {
        var lines = text.Split('\n');
        var sb = new StringBuilder(text.Length + 32);

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var isHeading = line.Length > 0 && line[0] == '#' && Regex.IsMatch(line, @"^#{1,6}\s");

            if (isHeading && sb.Length > 0)
            {
                // Ensure previous content ends with blank line
                var soFar = sb.ToString();
                if (!soFar.EndsWith("\n\n", StringComparison.Ordinal) && !soFar.EndsWith("\n", StringComparison.Ordinal))
                    sb.Append('\n');
                else if (soFar.EndsWith("\n", StringComparison.Ordinal) && !soFar.EndsWith("\n\n", StringComparison.Ordinal))
                {
                    // Check if previous line was non-empty
                    var prev = i > 0 ? lines[i - 1] : string.Empty;
                    if (!string.IsNullOrWhiteSpace(prev))
                        sb.Append('\n');
                }
            }

            sb.Append(line);
            if (i < lines.Length - 1)
                sb.Append('\n');
        }

        return sb.ToString();
    }
}
