using System.Windows.Media;

namespace MdViewer.Services;

/// <summary>
/// Soft pastel theme tokens for light and dark UI.
/// </summary>
public static class ThemePalette
{
    public sealed record Swatch(
        string AppBackground,
        string AppForeground,
        string PanelBackground,
        string Border,
        string EditorBackground,
        string EditorForeground,
        string SidebarBackground,
        string StatusBarBackground,
        string ToolbarBackground,
        string MutedForeground,
        string Accent,
        string AccentSoft,
        string Hover,
        string SegmentTrack,
        string Danger,
        string FindHighlight,
        string InputBackground,
        string InputBorder,
        string Selection,
        string FocusDim,
        string PreviewCss);

    public static Swatch Light { get; } = new(
        AppBackground: "#F3F0F7",
        AppForeground: "#5A6270",
        PanelBackground: "#EBE6F2",
        Border: "#D5CEDF",
        EditorBackground: "#FAF8FC",
        EditorForeground: "#5A6270",
        SidebarBackground: "#F7F4FB",
        StatusBarBackground: "#EDE8F4",
        ToolbarBackground: "#F7F4FB",
        MutedForeground: "#9A93A8",
        Accent: "#8B9DC3",
        AccentSoft: "#E4EAF5",
        Hover: "#E8E2F0",
        SegmentTrack: "#E4DEEC",
        Danger: "#D4A5A5",
        FindHighlight: "#F3E6C8",
        InputBackground: "#FAF8FC",
        InputBorder: "#D5CEDF",
        Selection: "#D5E0F0",
        FocusDim: "#B0A8BC",
        PreviewCss: PreviewDocumentCss);

    public static Swatch Dark { get; } = new(
        AppBackground: "#1C1B24",
        AppForeground: "#C5C0D0",
        PanelBackground: "#262433",
        Border: "#3A3748",
        EditorBackground: "#22212C",
        EditorForeground: "#C5C0D0",
        SidebarBackground: "#201E2A",
        StatusBarBackground: "#1C1B24",
        ToolbarBackground: "#201E2A",
        MutedForeground: "#8E879C",
        Accent: "#A8B4D4",
        AccentSoft: "#2F3345",
        Hover: "#2C2A38",
        SegmentTrack: "#2A2836",
        Danger: "#C9A0A0",
        FindHighlight: "#5A4E32",
        InputBackground: "#22212C",
        InputBorder: "#3A3748",
        Selection: "#3A4560",
        FocusDim: "#7A7488",
        PreviewCss: PreviewDocumentCss);

    public static void ApplyBrushes(Swatch swatch, Action<string, string> setBrush)
    {
        setBrush("AppBackgroundBrush", swatch.AppBackground);
        setBrush("AppForegroundBrush", swatch.AppForeground);
        setBrush("PanelBackgroundBrush", swatch.PanelBackground);
        setBrush("BorderBrushKey", swatch.Border);
        setBrush("SplitterBrush", swatch.Border);
        setBrush("EditorBackgroundBrush", swatch.EditorBackground);
        setBrush("EditorForegroundBrush", swatch.EditorForeground);
        setBrush("SidebarBackgroundBrush", swatch.SidebarBackground);
        setBrush("StatusBarBackgroundBrush", swatch.StatusBarBackground);
        setBrush("ToolbarBackgroundBrush", swatch.ToolbarBackground);
        setBrush("MutedForegroundBrush", swatch.MutedForeground);
        setBrush("SelectionBrush", swatch.Accent);
        setBrush("AccentBrush", swatch.Accent);
        setBrush("AccentSoftBrush", swatch.AccentSoft);
        setBrush("HoverBrush", swatch.Hover);
        setBrush("SegmentTrackBrush", swatch.SegmentTrack);
        setBrush("DangerBrush", swatch.Danger);
        setBrush("FindHighlightBrush", swatch.FindHighlight);
        setBrush("InputBackgroundBrush", swatch.InputBackground);
        setBrush("InputBorderBrush", swatch.InputBorder);
    }

    public static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    /// <summary>Single stylesheet with light/dark via html[data-theme] — toggles instantly without reload.</summary>
    public const string PreviewDocumentCss = """
                    :root {
                        color-scheme: light;
                        --md-fg: #5a6270;
                        --md-bg: #faf8fc;
                        --md-heading: #6b7382;
                        --md-border: #d5cedf;
                        --md-link: #7d92b8;
                        --md-link-hover: #6a80a8;
                        --md-code-bg: #ebe6f2;
                        --md-code-fg: #7a6e8a;
                        --md-pre-bg: #f0ecf6;
                        --md-quote: #9a93a8;
                        --md-quote-border: #c4b8d4;
                        --md-th-bg: #ebe6f2;
                    }
                    html[data-theme="dark"] {
                        color-scheme: dark;
                        --md-fg: #c5c0d0;
                        --md-bg: #22212c;
                        --md-heading: #d2cce0;
                        --md-border: #3a3748;
                        --md-link: #b0bdd8;
                        --md-link-hover: #c4cee6;
                        --md-code-bg: #2c2a38;
                        --md-code-fg: #cbb8d9;
                        --md-pre-bg: #262433;
                        --md-quote: #8e879c;
                        --md-quote-border: #5a5470;
                        --md-th-bg: #2c2a38;
                    }
                    html, body {
                        background: var(--md-bg);
                        color: var(--md-fg);
                    }
                    body {
                        font-family: "Segoe UI Variable Text", "Segoe UI", system-ui, sans-serif;
                        line-height: 1.7;
                        padding: 28px 36px 48px;
                        max-width: 820px;
                        margin: 0 auto;
                        font-size: 15px;
                    }
                    h1, h2, h3, h4, h5, h6 {
                        margin-top: 1.6em; margin-bottom: 0.55em; font-weight: 600;
                        letter-spacing: -0.01em; line-height: 1.3; color: var(--md-heading);
                        scroll-margin-top: 16px;
                    }
                    h1 { font-size: 1.85em; border-bottom: 1px solid var(--md-border); padding-bottom: 0.35em; }
                    h2 { font-size: 1.4em; border-bottom: 1px solid var(--md-border); padding-bottom: 0.3em; }
                    a { color: var(--md-link); text-decoration: none; font-weight: 500; }
                    a:hover { text-decoration: underline; color: var(--md-link-hover); }
                    code { font-family: "Cascadia Code", Consolas, monospace; background: var(--md-code-bg); color: var(--md-code-fg); padding: 0.15em 0.4em; border-radius: 6px; font-size: 0.9em; }
                    pre, pre.code-highlight { background: var(--md-pre-bg); border: 1px solid var(--md-border); padding: 0; overflow-x: auto; border-radius: 8px; }
                    pre code, pre code.hljs {
                        background: transparent !important;
                        padding: 16px 18px !important;
                        display: block; overflow-x: auto; font-size: 13px; line-height: 1.55;
                        color: var(--md-fg);
                        white-space: pre;
                        tab-size: 4;
                        font-variant-ligatures: none;
                    }
                    pre code span { font-family: inherit; }
                    blockquote { border-left: 3px solid var(--md-quote-border); margin: 1.2em 0; padding: 0.2em 0 0.2em 16px; color: var(--md-quote); }
                    table { border-collapse: collapse; width: 100%; margin: 1.2em 0; }
                    th, td { border: 1px solid var(--md-border); padding: 10px 12px; text-align: left; }
                    th { background: var(--md-th-bg); font-weight: 600; color: var(--md-heading); }
                    img { max-width: 100%; height: auto; border-radius: 8px; display: inline-block; }
                    img[alt]:not([src]), img[src=""] { color: var(--md-quote); font-style: italic; }
                    hr { border: none; border-top: 1px solid var(--md-border); margin: 2em 0; }
                    ul, ol { padding-left: 1.4em; }
                    li + li { margin-top: 0.25em; }
                    .math { font-size: 1.05em; }
                    div.math { margin: 1.2em 0; overflow-x: auto; }
                    .mermaid { margin: 1.2em 0; text-align: center; }
        """;
}
