using System.Text.RegularExpressions;

namespace MdViewer.Services;

public readonly record struct DocumentStats(
    int Characters,
    int CharactersNoSpaces,
    int Words,
    int Paragraphs,
    int Lines,
    double ReadingMinutes)
{
    public string ToStatusText()
    {
        var minutes = ReadingMinutes < 1 ? "< 1" : ReadingMinutes.ToString("0.#");
        return $"{Words} words · {Characters} chars · {Paragraphs} paragraphs · {minutes} min read";
    }
}

public static class DocumentStatsService
{
    private static readonly Regex WordRegex = new(@"\b[\w''-]+\b", RegexOptions.Compiled);
    private static readonly Regex ParagraphRegex = new(@"\S(?:.|\n)*?(?:\n\s*\n|$)", RegexOptions.Compiled);

    public static DocumentStats Calculate(string text)
    {
        if (string.IsNullOrEmpty(text))
            return new DocumentStats(0, 0, 0, 0, 1, 0);

        var characters = text.Length;
        var noSpaces = text.Count(c => !char.IsWhiteSpace(c));
        var words = WordRegex.Matches(text).Count;
        var lines = text.Replace("\r\n", "\n").Split('\n').Length;
        var paragraphs = Math.Max(1, ParagraphRegex.Matches(text).Count);
        var readingMinutes = words / 200.0;

        return new DocumentStats(characters, noSpaces, words, paragraphs, lines, readingMinutes);
    }
}
