namespace MdViewer.Services;

public static class KeyboardShortcutsCatalog
{
    public sealed record Shortcut(string Keys, string Action);

    public sealed record ShortcutGroup(string Title, IReadOnlyList<Shortcut> Items);

    public static IReadOnlyList<ShortcutGroup> Groups { get; } =
    [
        new("File",
        [
            new("Ctrl+N", "New tab"),
            new("Ctrl+O", "Open file(s) in tabs"),
            new("Ctrl+Shift+O", "Open folder / workspace"),
            new("Ctrl+S", "Save"),
            new("Ctrl+Shift+S", "Save as"),
            new("Ctrl+W", "Close active tab"),
        ]),
        new("Edit",
        [
            new("Ctrl+F", "Find"),
            new("Ctrl+H", "Find and replace"),
            new("Ctrl+Shift+F", "Format document"),
            new("Esc", "Close find bar"),
        ]),
        new("Find bar",
        [
            new("Enter", "Find next"),
            new("Shift+Enter", "Find previous"),
            new("Enter (Replace)", "Replace current match"),
            new("Esc", "Close find bar"),
        ]),
        new("Formatting",
        [
            new("Ctrl+B", "Bold"),
            new("Ctrl+I", "Italic"),
            new("Ctrl+K", "Insert link"),
            new("Ctrl+1", "Heading 1"),
            new("Ctrl+2", "Heading 2"),
            new("Ctrl+3", "Heading 3"),
            new("Ctrl+Shift+C", "Inline code"),
            new("Ctrl+Shift+X", "Strikethrough"),
        ]),
    ];
}
