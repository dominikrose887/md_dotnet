using System.Reflection;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace MdViewer.Services;

public static class HighlightingService
{
    public static IHighlightingDefinition LoadMarkdown(bool darkTheme)
    {
        var resourceName = darkTheme
            ? "MdViewer.Resources.Markdown-Dark.xshd"
            : "MdViewer.Resources.Markdown-Light.xshd";

        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing resource: {resourceName}");
        using var reader = new XmlTextReader(stream);
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }
}
