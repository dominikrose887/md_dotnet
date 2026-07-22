using System.Text.RegularExpressions;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Rendering;

namespace MdViewer.Services;

/// <summary>
/// Syntax-highlights fenced markdown code blocks with language-aware colors,
/// including Spring/Lombok/React-style annotations and hooks.
/// </summary>
public sealed class CodeBlockHighlightingTransformer : DocumentColorizingTransformer
{
    private static readonly Regex FenceOpen = new(@"^[ \t]*```([\w#+\-.]*)[ \t]*$", RegexOptions.Compiled);
    private static readonly Regex FenceClose = new(@"^[ \t]*```[ \t]*$", RegexOptions.Compiled);

    private readonly List<ColoredSpan> _spans = [];
    private readonly List<CodeBlock> _blocks = [];

    private SolidColorBrush _fenceBrush = Hex("#656D76");
    private SolidColorBrush _keywordBrush = Hex("#CF222E");
    private SolidColorBrush _stringBrush = Hex("#0A3069");
    private SolidColorBrush _commentBrush = Hex("#6E7781");
    private SolidColorBrush _numberBrush = Hex("#0550AE");
    private SolidColorBrush _typeBrush = Hex("#953800");
    private SolidColorBrush _annotationBrush = Hex("#B89A78");
    private SolidColorBrush _plainBrush = Hex("#1F2328");

    public void SetTheme(bool dark)
    {
        if (dark)
        {
            _fenceBrush = Hex("#8E879C");
            _keywordBrush = Hex("#D4B0B8");
            _stringBrush = Hex("#B0C4D8");
            _commentBrush = Hex("#7A7488");
            _numberBrush = Hex("#A8B4D4");
            _typeBrush = Hex("#D4C0A0");
            _annotationBrush = Hex("#E0C8A8");
            _plainBrush = Hex("#C5C0D0");
        }
        else
        {
            _fenceBrush = Hex("#9A93A8");
            _keywordBrush = Hex("#C4A0A8");
            _stringBrush = Hex("#7D92B8");
            _commentBrush = Hex("#B0A8BC");
            _numberBrush = Hex("#8B9DC3");
            _typeBrush = Hex("#A89070");
            _annotationBrush = Hex("#B89A78");
            _plainBrush = Hex("#5A6270");
        }
    }

    public void Rebuild(TextDocument document)
    {
        _blocks.Clear();
        _spans.Clear();
        if (document.LineCount == 0) return;

        string? language = null;
        var startLine = 0;

        for (var i = 1; i <= document.LineCount; i++)
        {
            var line = document.GetLineByNumber(i);
            var text = document.GetText(line);

            if (language is null)
            {
                var open = FenceOpen.Match(text);
                if (open.Success)
                {
                    language = open.Groups[1].Value;
                    startLine = i;
                }
            }
            else if (FenceClose.IsMatch(text) && i > startLine)
            {
                _blocks.Add(new CodeBlock(startLine, i, language));
                language = null;
            }
        }

        foreach (var block in _blocks)
            BuildSpansForBlock(document, block);
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        foreach (var span in _spans)
        {
            if (span.EndOffset <= line.Offset || span.StartOffset >= line.EndOffset)
                continue;

            var start = Math.Max(span.StartOffset, line.Offset);
            var end = Math.Min(span.EndOffset, line.EndOffset);
            if (start >= end) continue;

            var brush = span.Brush;
            ChangeLinePart(start, end, element => element.TextRunProperties.SetForegroundBrush(brush));
        }
    }

    private void BuildSpansForBlock(TextDocument document, CodeBlock block)
    {
        var openLine = document.GetLineByNumber(block.StartLine);
        var closeLine = document.GetLineByNumber(block.EndLine);
        _spans.Add(new ColoredSpan(openLine.Offset, openLine.EndOffset, _fenceBrush));
        _spans.Add(new ColoredSpan(closeLine.Offset, closeLine.EndOffset, _fenceBrush));

        if (block.EndLine - block.StartLine <= 1)
            return;

        var firstContent = document.GetLineByNumber(block.StartLine + 1);
        var lastContent = document.GetLineByNumber(block.EndLine - 1);
        var codeStart = firstContent.Offset;
        var codeLength = lastContent.EndOffset - codeStart;
        if (codeLength <= 0) return;

        var codeText = document.GetText(codeStart, codeLength);
        _spans.Add(new ColoredSpan(codeStart, codeStart + codeLength, _plainBrush));

        _ = TryHighlightWithAvalonEdit(codeText, block.Language, codeStart)
            || TryHighlightWithHeuristics(codeText, block.Language, codeStart);

        ApplyAnnotations(codeText, block.Language, codeStart);
    }

    private bool TryHighlightWithAvalonEdit(string codeText, string language, int absoluteStart)
    {
        var definition = ResolveDefinition(language);
        if (definition is null) return false;

        try
        {
            var temp = new TextDocument(codeText);
            var highlighter = new DocumentHighlighter(temp, definition);
            var any = false;

            for (var lineNumber = 1; lineNumber <= temp.LineCount; lineNumber++)
            {
                var highlighted = highlighter.HighlightLine(lineNumber);
                foreach (var section in highlighted.Sections)
                {
                    var brush = MapAvalonColor(section.Color) ?? ExtractBrush(section.Color);
                    if (brush is null) continue;

                    var start = absoluteStart + section.Offset;
                    var end = start + section.Length;
                    if (end <= start) continue;

                    _spans.Add(new ColoredSpan(start, end, brush));
                    any = true;
                }
            }

            return any;
        }
        catch
        {
            return false;
        }
    }

    private bool TryHighlightWithHeuristics(string codeText, string language, int absoluteStart)
    {
        var key = LanguageProfiles.Normalize(language);
        var keywords = LanguageProfiles.GetKeywords(key);
        if (keywords.Count == 0 && key is not ("json" or "xml" or "html" or "css" or "sql" or "bash" or "docker" or "yaml" or "properties"))
            return false;

        ApplyRegex(codeText, absoluteStart, @"//.*?$|/\*.*?\*/|#.*?$|<!--.*?-->", _commentBrush, RegexOptions.Singleline | RegexOptions.Multiline);
        ApplyRegex(codeText, absoluteStart, @"(""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*'|`(?:\\.|[^`\\])*`)", _stringBrush);
        ApplyRegex(codeText, absoluteStart, @"\b\d+(\.\d+)?\b", _numberBrush);

        if (keywords.Count > 0)
        {
            var ignoreCase = key is "sql" or "docker" or "yaml" or "properties" or "bash" or "powershell"
                ? RegexOptions.IgnoreCase
                : RegexOptions.None;
            var pattern = $@"\b(?:{string.Join("|", keywords.Select(Regex.Escape))})\b";
            ApplyRegex(codeText, absoluteStart, pattern, _keywordBrush, ignoreCase);
        }

        if (key is "csharp" or "java" or "kotlin" or "groovy" or "typescript" or "javascript" or "jsx" or "tsx")
            ApplyRegex(codeText, absoluteStart, @"\b[A-Z][A-Za-z0-9_]+\b", _typeBrush);

        return true;
    }

    private void ApplyAnnotations(string codeText, string language, int absoluteStart)
    {
        var key = LanguageProfiles.Normalize(language);
        var known = LanguageProfiles.GetAnnotations(key);

        if (LanguageProfiles.SupportsAnnotations(key))
        {
            if (key is "csharp")
            {
                foreach (Match match in Regex.Matches(codeText, @"\[[A-Za-z_][\w]*(?:Attribute)?(?:\s*\([^)\]]*\))?\]"))
                {
                    var inner = match.Value.Trim('[', ']');
                    var name = inner.Split('(', 2)[0].Trim();
                    if (name.EndsWith("Attribute", StringComparison.Ordinal))
                        name = name[..^"Attribute".Length];
                    if (known.Count > 0 && !known.Contains(name))
                        continue;
                    _spans.Add(new ColoredSpan(
                        absoluteStart + match.Index,
                        absoluteStart + match.Index + match.Length,
                        _annotationBrush));
                }
            }
            else
            {
                foreach (Match match in Regex.Matches(codeText, @"@[A-Za-z_][\w]*(?:\.[A-Za-z_][\w]*)*"))
                {
                    if (match.Length == 0) continue;
                    _spans.Add(new ColoredSpan(
                        absoluteStart + match.Index,
                        absoluteStart + match.Index + match.Length,
                        _annotationBrush));
                }
            }
        }

        if ((key is "javascript" or "typescript" or "jsx" or "tsx") && known.Count > 0)
        {
            var pattern = $@"\b(?:{string.Join("|", known.Select(Regex.Escape))})\b";
            ApplyRegex(codeText, absoluteStart, pattern, _annotationBrush);
        }
    }

    private void ApplyRegex(string text, int absoluteStart, string pattern, SolidColorBrush brush, RegexOptions options = RegexOptions.None)
    {
        foreach (Match match in Regex.Matches(text, pattern, options | RegexOptions.Compiled))
        {
            if (match.Length == 0) continue;
            _spans.Add(new ColoredSpan(absoluteStart + match.Index, absoluteStart + match.Index + match.Length, brush));
        }
    }

    private SolidColorBrush? MapAvalonColor(HighlightingColor? color)
    {
        if (color?.Name is null) return null;
        var name = color.Name.ToLowerInvariant();

        if (name.Contains("comment")) return _commentBrush;
        if (name.Contains("string") || name.Contains("char")) return _stringBrush;
        if (name.Contains("number") || name.Contains("digit")) return _numberBrush;
        if (name.Contains("keyword") || name.Contains("methodvisibility") || name.Contains("modifiers"))
            return _keywordBrush;
        if (name.Contains("type") || name.Contains("class") || name.Contains("value type"))
            return _typeBrush;

        return null;
    }

    private static SolidColorBrush? ExtractBrush(HighlightingColor? color)
    {
        try
        {
            var brush = color?.Foreground?.GetBrush(null);
            return brush as SolidColorBrush;
        }
        catch
        {
            return null;
        }
    }

    private static IHighlightingDefinition? ResolveDefinition(string language)
    {
        var key = LanguageProfiles.Normalize(language);
        if (string.IsNullOrEmpty(key)) return null;

        var name = key switch
        {
            "csharp" => "C#",
            "javascript" or "typescript" or "jsx" or "tsx" or "json" => "JavaScript",
            "xml" or "html" or "xaml" or "svg" => "XML",
            "java" or "kotlin" or "groovy" => "Java",
            "cpp" or "c" => "C++",
            "php" => "PHP",
            "vb" => "VB",
            _ => null
        };

        if (name is not null)
        {
            var def = HighlightingManager.Instance.GetDefinition(name);
            if (def is not null) return def;
        }

        return HighlightingManager.Instance.GetDefinitionByExtension("." + key)
               ?? HighlightingManager.Instance.GetDefinition(language);
    }

    private static SolidColorBrush Hex(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }

    private sealed record CodeBlock(int StartLine, int EndLine, string Language);
    private sealed record ColoredSpan(int StartOffset, int EndOffset, SolidColorBrush Brush);
}
