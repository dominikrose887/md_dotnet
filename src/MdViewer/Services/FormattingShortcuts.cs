using ICSharpCode.AvalonEdit;

namespace MdViewer.Services;

public static class FormattingShortcuts
{
    public static void Bold(TextEditor editor) => WrapOrUnwrap(editor, "**", "**");
    public static void Italic(TextEditor editor) => WrapOrUnwrap(editor, "*", "*");
    public static void Strikethrough(TextEditor editor) => WrapOrUnwrap(editor, "~~", "~~");
    public static void InlineCode(TextEditor editor) => WrapOrUnwrap(editor, "`", "`");

    public static void Link(TextEditor editor)
    {
        var selected = editor.SelectedText;
        if (string.IsNullOrEmpty(selected))
        {
            Insert(editor, "[text](url)");
            editor.Select(editor.CaretOffset - 10, 4); // select "text"
            return;
        }

        var replacement = $"[{selected}](url)";
        var offset = editor.SelectionStart;
        editor.Document.Replace(offset, editor.SelectionLength, replacement);
        editor.Select(offset + selected.Length + 3, 3); // select "url"
    }

    public static void Heading(TextEditor editor, int level)
    {
        level = Math.Clamp(level, 1, 6);
        var prefix = new string('#', level) + " ";
        var line = editor.Document.GetLineByOffset(editor.CaretOffset);
        var text = editor.Document.GetText(line);
        var trimmed = text.TrimStart();
        var leading = text.Length - trimmed.Length;

        // Remove existing heading markers
        var body = System.Text.RegularExpressions.Regex.Replace(trimmed, @"^#{1,6}\s+", "");
        var updated = text[..leading] + prefix + body;
        editor.Document.Replace(line.Offset, line.Length, updated);
        editor.CaretOffset = line.Offset + updated.Length;
    }

    private static void WrapOrUnwrap(TextEditor editor, string left, string right)
    {
        var start = editor.SelectionStart;
        var length = editor.SelectionLength;
        var selected = editor.SelectedText;

        if (length > 0 &&
            selected.StartsWith(left, StringComparison.Ordinal) &&
            selected.EndsWith(right, StringComparison.Ordinal) &&
            selected.Length >= left.Length + right.Length)
        {
            var inner = selected[left.Length..^right.Length];
            editor.Document.Replace(start, length, inner);
            editor.Select(start, inner.Length);
            return;
        }

        if (length == 0)
        {
            var placeholder = "text";
            var insert = left + placeholder + right;
            editor.Document.Insert(start, insert);
            editor.Select(start + left.Length, placeholder.Length);
            return;
        }

        editor.Document.Replace(start, length, left + selected + right);
        editor.Select(start + left.Length, length);
    }

    private static void Insert(TextEditor editor, string text)
    {
        var offset = editor.SelectionLength > 0 ? editor.SelectionStart : editor.CaretOffset;
        if (editor.SelectionLength > 0)
            editor.Document.Replace(offset, editor.SelectionLength, text);
        else
            editor.Document.Insert(offset, text);
        editor.CaretOffset = offset + text.Length;
    }
}
