using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;

namespace MdViewer.Services;

public sealed class FocusModeService
{
    private readonly TextEditor _editor;
    private readonly FocusColorizer _colorizer;
    private bool _enabled;

    public FocusModeService(TextEditor editor)
    {
        _editor = editor;
        _colorizer = new FocusColorizer();
        _editor.TextArea.TextView.LineTransformers.Add(_colorizer);
        _editor.TextArea.Caret.PositionChanged += (_, _) =>
        {
            if (_enabled)
                Refresh();
        };
    }

    public bool IsEnabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            _colorizer.Enabled = value;
            Refresh();
        }
    }

    public void SetDimBrush(Brush brush) => _colorizer.DimBrush = brush;

    public void BringToFront()
    {
        var transformers = _editor.TextArea.TextView.LineTransformers;
        transformers.Remove(_colorizer);
        transformers.Add(_colorizer);
    }

    public void Refresh()
    {
        if (!_enabled)
        {
            _editor.TextArea.TextView.Redraw();
            return;
        }

        var offset = _editor.CaretOffset;
        var line = _editor.Document.GetLineByOffset(offset);
        var (start, end) = GetParagraphRange(line);
        _colorizer.FocusStartOffset = start;
        _colorizer.FocusEndOffset = end;
        _editor.TextArea.TextView.Redraw();
    }

    private (int start, int end) GetParagraphRange(DocumentLine line)
    {
        var startLine = line;
        while (startLine.PreviousLine is not null)
        {
            var prev = startLine.PreviousLine;
            var text = _editor.Document.GetText(prev).Trim();
            if (text.Length == 0) break;
            startLine = prev;
        }

        var endLine = line;
        while (endLine.NextLine is not null)
        {
            var next = endLine.NextLine;
            var text = _editor.Document.GetText(next).Trim();
            if (text.Length == 0) break;
            endLine = next;
        }

        return (startLine.Offset, endLine.EndOffset);
    }

    private sealed class FocusColorizer : DocumentColorizingTransformer
    {
        public bool Enabled { get; set; }
        public int FocusStartOffset { get; set; }
        public int FocusEndOffset { get; set; }
        public Brush DimBrush { get; set; } = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150));

        protected override void ColorizeLine(DocumentLine line)
        {
            if (!Enabled) return;
            if (line.EndOffset < FocusStartOffset || line.Offset > FocusEndOffset)
                ChangeLinePart(line.Offset, line.EndOffset, element => element.TextRunProperties.SetForegroundBrush(DimBrush));
        }
    }
}
