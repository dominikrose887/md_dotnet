using System.Globalization;
using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;

namespace MdViewer.Services;

/// <summary>
/// Tags every rendered block with <c>data-line</c> / <c>code-line</c> so preview↔editor
/// scroll sync can map by source line (VS Code style), not by scroll ratio.
/// </summary>
public sealed class SourceLineMapExtension : IMarkdownExtension
{
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        pipeline.DocumentProcessed -= OnDocumentProcessed;
        pipeline.DocumentProcessed += OnDocumentProcessed;
    }

    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
    }

    private static void OnDocumentProcessed(MarkdownDocument document) =>
        AddLineAttributes(document);

    private static void AddLineAttributes(Block block)
    {
        if (block is not MarkdownDocument
            and not BlankLineBlock
            and not LinkReferenceDefinition
            and not LinkReferenceDefinitionGroup)
        {
            var attrs = block.GetAttributes();
            attrs.AddPropertyIfNotExist("data-line", block.Line.ToString(CultureInfo.InvariantCulture));
            attrs.AddClass("code-line");
        }

        if (block is ContainerBlock container)
        {
            foreach (var child in container)
                AddLineAttributes(child);
        }
    }
}
