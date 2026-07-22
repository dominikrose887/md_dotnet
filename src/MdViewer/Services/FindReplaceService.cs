using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace MdViewer.Services;

public sealed class FindReplaceService
{
    private readonly TextEditor _editor;
    private readonly MatchHighlightRenderer _renderer;
    private readonly List<TextSegment> _matches = [];
    private int _currentIndex = -1;
    private string _lastQuery = string.Empty;
    private bool _matchCase;
    private bool _useRegex;
    private string? _error;

    public FindReplaceService(TextEditor editor)
    {
        _editor = editor;
        _renderer = new MatchHighlightRenderer();
        _editor.TextArea.TextView.BackgroundRenderers.Add(_renderer);
    }

    public int MatchCount => _matches.Count;
    public int CurrentIndex => _currentIndex;
    public bool HasMatches => _matches.Count > 0;
    public string? Error => _error;

    public void SetMarkerBrush(Brush brush)
    {
        _renderer.MarkerBrush = brush;
        _editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
    }

    public void Clear()
    {
        _matches.Clear();
        _renderer.Segments.Clear();
        _currentIndex = -1;
        _lastQuery = string.Empty;
        _error = null;
        _editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
    }

    public int FindAll(string query, bool matchCase, bool useRegex)
    {
        _matchCase = matchCase;
        _useRegex = useRegex;
        _lastQuery = query ?? string.Empty;
        _matches.Clear();
        _renderer.Segments.Clear();
        _currentIndex = -1;
        _error = null;

        if (string.IsNullOrEmpty(_lastQuery))
        {
            _editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
            return 0;
        }

        try
        {
            if (useRegex)
                FindAllRegex();
            else
                FindAllPlain();
        }
        catch (ArgumentException ex)
        {
            _error = ex.Message;
            _editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
            return 0;
        }

        _editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);

        if (_matches.Count > 0)
        {
            var caret = _editor.CaretOffset;
            _currentIndex = _matches.FindIndex(m => m.StartOffset >= caret);
            if (_currentIndex < 0) _currentIndex = 0;
            SelectCurrent();
        }

        return _matches.Count;
    }

    public void FindNext()
    {
        if (_matches.Count == 0)
        {
            if (!string.IsNullOrEmpty(_lastQuery))
                FindAll(_lastQuery, _matchCase, _useRegex);
            if (_matches.Count == 0) return;
        }

        _currentIndex = (_currentIndex + 1) % _matches.Count;
        SelectCurrent();
    }

    public void FindPrevious()
    {
        if (_matches.Count == 0)
        {
            if (!string.IsNullOrEmpty(_lastQuery))
                FindAll(_lastQuery, _matchCase, _useRegex);
            if (_matches.Count == 0) return;
        }

        _currentIndex = (_currentIndex - 1 + _matches.Count) % _matches.Count;
        SelectCurrent();
    }

    public bool ReplaceCurrent(string replacement)
    {
        if (_matches.Count == 0 || _currentIndex < 0 || _currentIndex >= _matches.Count)
            return false;

        var match = _matches[_currentIndex];
        var offset = match.StartOffset;
        var length = match.Length;
        var current = _editor.Document.GetText(offset, length);
        var replacementText = BuildReplacement(current, replacement ?? string.Empty);

        _editor.Document.Replace(offset, length, replacementText);

        var query = _lastQuery;
        var nextCaret = offset + replacementText.Length;
        FindAll(query, _matchCase, _useRegex);

        if (_matches.Count == 0)
            return true;

        _currentIndex = _matches.FindIndex(m => m.StartOffset >= nextCaret);
        if (_currentIndex < 0) _currentIndex = 0;
        SelectCurrent();
        return true;
    }

    public int ReplaceAll(string replacement)
    {
        if (string.IsNullOrEmpty(_lastQuery))
            return 0;

        // Refresh matches first
        FindAll(_lastQuery, _matchCase, _useRegex);
        if (_matches.Count == 0)
            return 0;

        var count = _matches.Count;
        var replacementTemplate = replacement ?? string.Empty;

        _editor.Document.BeginUpdate();
        try
        {
            for (var i = _matches.Count - 1; i >= 0; i--)
            {
                var match = _matches[i];
                var current = _editor.Document.GetText(match.StartOffset, match.Length);
                var text = BuildReplacement(current, replacementTemplate);
                _editor.Document.Replace(match.StartOffset, match.Length, text);
            }
        }
        finally
        {
            _editor.Document.EndUpdate();
        }

        FindAll(_lastQuery, _matchCase, _useRegex);
        return count;
    }

    private void FindAllPlain()
    {
        var comparison = _matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var text = _editor.Text;
        var start = 0;

        while (start <= text.Length - _lastQuery.Length)
        {
            var index = text.IndexOf(_lastQuery, start, comparison);
            if (index < 0) break;

            AddMatch(index, _lastQuery.Length);
            start = index + Math.Max(1, _lastQuery.Length);
        }
    }

    private void FindAllRegex()
    {
        var options = RegexOptions.Multiline | RegexOptions.CultureInvariant;
        if (!_matchCase) options |= RegexOptions.IgnoreCase;

        var regex = new Regex(_lastQuery, options, TimeSpan.FromSeconds(1));
        foreach (Match match in regex.Matches(_editor.Text))
        {
            if (match.Length == 0) continue;
            AddMatch(match.Index, match.Length);
        }
    }

    private void AddMatch(int offset, int length)
    {
        var segment = new TextSegment { StartOffset = offset, Length = length };
        _matches.Add(segment);
        _renderer.Segments.Add(segment);
    }

    private string BuildReplacement(string matchedText, string template)
    {
        if (!_useRegex)
            return template;

        try
        {
            var options = RegexOptions.Multiline | RegexOptions.CultureInvariant;
            if (!_matchCase) options |= RegexOptions.IgnoreCase;
            var regex = new Regex(_lastQuery, options, TimeSpan.FromSeconds(1));
            return regex.Replace(matchedText, template, 1);
        }
        catch
        {
            return template;
        }
    }

    private void SelectCurrent()
    {
        if (_currentIndex < 0 || _currentIndex >= _matches.Count) return;

        var match = _matches[_currentIndex];
        _editor.Select(match.StartOffset, match.Length);
        _editor.ScrollToLine(_editor.Document.GetLineByOffset(match.StartOffset).LineNumber);
        _editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
    }

    private sealed class MatchHighlightRenderer : IBackgroundRenderer
    {
        public TextSegmentCollection<TextSegment> Segments { get; } = new();
        public Brush MarkerBrush { get; set; } = new SolidColorBrush(Color.FromArgb(120, 255, 213, 65));
        public KnownLayer Layer => KnownLayer.Selection;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (textView.Document is null || Segments.Count == 0 || textView.VisualLines.Count == 0)
                return;

            var viewStart = textView.VisualLines[0].FirstDocumentLine.Offset;
            var lastLine = textView.VisualLines[^1].LastDocumentLine;
            var viewEnd = lastLine.EndOffset;

            foreach (var segment in Segments.FindOverlappingSegments(viewStart, viewEnd - viewStart))
            {
                var geoBuilder = new BackgroundGeometryBuilder
                {
                    AlignToWholePixels = true,
                    CornerRadius = 2
                };
                geoBuilder.AddSegment(textView, segment);
                var geometry = geoBuilder.CreateGeometry();
                if (geometry is not null)
                    drawingContext.DrawGeometry(MarkerBrush, null, geometry);
            }
        }
    }
}
