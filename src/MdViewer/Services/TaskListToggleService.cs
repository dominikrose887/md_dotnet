using System.Text.RegularExpressions;

namespace MdViewer.Services;

/// <summary>
/// Toggles GitHub-style task list markers (<c>[ ]</c> / <c>[x]</c>) in raw markdown by source line.
/// </summary>
public static class TaskListToggleService
{
    // List marker + task checkbox, e.g. "- [ ]", "1. [x]", "  * [X]"
    private static readonly Regex TaskListMarker = new(
        @"^(\s*(?:[-+*]|\d+[.)])\s+)\[([ xX])\]",
        RegexOptions.Compiled);

    /// <summary>
    /// Locates the task-list checkbox character on <paramref name="zeroBasedLine"/>
    /// and returns the absolute offset to replace with <c>x</c> or space.
    /// </summary>
    public static bool TryGetToggle(
        string markdown,
        int zeroBasedLine,
        bool isChecked,
        out int charOffset,
        out char newChar)
    {
        charOffset = -1;
        newChar = isChecked ? 'x' : ' ';

        if (string.IsNullOrEmpty(markdown) || zeroBasedLine < 0)
            return false;

        if (!TryGetLineSpan(markdown, zeroBasedLine, out var start, out var end))
            return false;

        var line = markdown.AsSpan(start, end - start);
        // Regex needs string; small line allocations are fine here.
        var lineText = line.ToString();
        var match = TaskListMarker.Match(lineText);
        if (!match.Success)
            return false;

        charOffset = start + match.Groups[2].Index;
        var current = markdown[charOffset];
        if (isChecked)
        {
            if (current is 'x' or 'X')
                return false; // already checked
        }
        else if (current == ' ')
        {
            return false; // already unchecked
        }

        return true;
    }

    private static bool TryGetLineSpan(string text, int zeroBasedLine, out int start, out int end)
    {
        start = 0;
        end = 0;
        var line = 0;
        var i = 0;

        while (i <= text.Length)
        {
            if (line == zeroBasedLine)
            {
                start = i;
                while (i < text.Length && text[i] is not ('\r' or '\n'))
                    i++;
                end = i;
                return true;
            }

            if (i >= text.Length)
                break;

            if (text[i] == '\r')
            {
                i++;
                if (i < text.Length && text[i] == '\n')
                    i++;
                line++;
                continue;
            }

            if (text[i] == '\n')
            {
                i++;
                line++;
                continue;
            }

            i++;
        }

        return false;
    }
}
