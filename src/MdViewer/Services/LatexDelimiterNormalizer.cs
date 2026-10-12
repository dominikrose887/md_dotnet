using System.Text;

namespace MdViewer.Services;

/// <summary>
/// Markdig's Mathematics extension only recognizes <c>$...$</c> / <c>$$...$$</c>.
/// Many documents (Pandoc, academic notes) use <c>\(...\)</c> / <c>\[...\]</c> instead.
/// Without conversion, <c>\(</c> becomes a plain <c>(</c> and <c>{...}</c> is eaten by
/// GenericAttributes — leaving raw TeX in the preview.
/// </summary>
public static class LatexDelimiterNormalizer
{
    public static string Normalize(string markdown)
    {
        if (string.IsNullOrEmpty(markdown) || markdown.IndexOf('\\') < 0)
            return markdown;

        var sb = new StringBuilder(markdown.Length + 16);
        var i = 0;
        while (i < markdown.Length)
        {
            // Fenced code block: ``` or ~~~
            if (TryCopyFencedCodeBlock(markdown, ref i, sb))
                continue;

            // Inline code: `...` or `` ... ``
            if (TryCopyInlineCode(markdown, ref i, sb))
                continue;

            // Display math \[ ... \]
            if (i + 1 < markdown.Length && markdown[i] == '\\' && markdown[i + 1] == '[')
            {
                var close = IndexOfDelimiter(markdown, i + 2, '\\', ']');
                if (close >= 0)
                {
                    sb.Append("$$");
                    sb.Append(markdown, i + 2, close - (i + 2));
                    sb.Append("$$");
                    i = close + 2;
                    continue;
                }
            }

            // Inline math \( ... \)
            if (i + 1 < markdown.Length && markdown[i] == '\\' && markdown[i + 1] == '(')
            {
                var close = IndexOfDelimiter(markdown, i + 2, '\\', ')');
                if (close >= 0)
                {
                    sb.Append('$');
                    sb.Append(markdown, i + 2, close - (i + 2));
                    sb.Append('$');
                    i = close + 2;
                    continue;
                }
            }

            sb.Append(markdown[i]);
            i++;
        }

        return sb.ToString();
    }

    private static int IndexOfDelimiter(string text, int start, char first, char second)
    {
        for (var i = start; i + 1 < text.Length; i++)
        {
            if (text[i] == first && text[i + 1] == second)
                return i;
        }

        return -1;
    }

    private static bool TryCopyFencedCodeBlock(string text, ref int i, StringBuilder sb)
    {
        if (text[i] is not ('`' or '~'))
            return false;

        // Only treat as fence at line start (after \n or beginning).
        if (i > 0 && text[i - 1] != '\n')
            return false;

        var fenceChar = text[i];
        var fenceStart = i;
        var fenceLen = 0;
        while (i < text.Length && text[i] == fenceChar)
        {
            fenceLen++;
            i++;
        }

        if (fenceLen < 3)
        {
            i = fenceStart;
            return false;
        }

        // Copy opening fence + info string through end of line
        sb.Append(text, fenceStart, i - fenceStart);
        while (i < text.Length && text[i] is not ('\r' or '\n'))
        {
            sb.Append(text[i]);
            i++;
        }

        // Copy until closing fence of same length
        while (i < text.Length)
        {
            var lineStart = i;
            if (text[i] == '\r')
            {
                sb.Append(text[i++]);
                if (i < text.Length && text[i] == '\n')
                    sb.Append(text[i++]);
                lineStart = i;
            }
            else if (text[i] == '\n')
            {
                sb.Append(text[i++]);
                lineStart = i;
            }

            var j = lineStart;
            var count = 0;
            while (j < text.Length && text[j] == fenceChar)
            {
                count++;
                j++;
            }

            if (count >= fenceLen && (j >= text.Length || text[j] is '\r' or '\n'))
            {
                sb.Append(text, lineStart, j - lineStart);
                i = j;
                return true;
            }

            // Copy rest of this line
            while (i < text.Length && text[i] is not ('\r' or '\n'))
            {
                sb.Append(text[i]);
                i++;
            }
        }

        return true;
    }

    private static bool TryCopyInlineCode(string text, ref int i, StringBuilder sb)
    {
        if (text[i] != '`')
            return false;

        var start = i;
        var ticks = 0;
        while (i < text.Length && text[i] == '`')
        {
            ticks++;
            i++;
        }

        // Find closing run of the same length
        var j = i;
        while (j < text.Length)
        {
            if (text[j] == '`')
            {
                var k = j;
                var count = 0;
                while (k < text.Length && text[k] == '`')
                {
                    count++;
                    k++;
                }

                if (count == ticks)
                {
                    sb.Append(text, start, k - start);
                    i = k;
                    return true;
                }

                j = k;
                continue;
            }

            // Inline code shouldn't span paragraphs in CommonMark; stop at blank-ish break
            if (text[j] == '\n')
            {
                // allow single newline inside; abort on no close before end handled below
            }

            j++;
        }

        // No close — treat opening ticks as literal
        i = start;
        return false;
    }
}
