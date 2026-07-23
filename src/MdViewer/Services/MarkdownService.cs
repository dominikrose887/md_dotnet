using Markdig;
using Markdig.Extensions.AutoIdentifiers;

namespace MdViewer.Services;

public static class MarkdownService
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
        .UseYamlFrontMatter()
        .Build();

    public static string ConvertToHtml(string markdown, bool darkTheme = false)
    {
        var html = Markdown.ToHtml(markdown, Pipeline);
        return PreviewCodeEnhancer.Enhance(html, darkTheme);
    }

    public static string WrapInDocument(
        string bodyHtml,
        string title = "Document",
        bool darkTheme = false,
        string? baseHref = null)
    {
        var theme = darkTheme ? "dark" : "light";
        var mermaidTheme = darkTheme ? "dark" : "default";
        var colorScheme = darkTheme ? "dark" : "light";
        var baseTag = string.IsNullOrWhiteSpace(baseHref)
            ? string.Empty
            : $"<base href=\"{System.Net.WebUtility.HtmlEncode(baseHref)}\">";

        return $$"""
            <!DOCTYPE html>
            <html lang="en" data-theme="{{theme}}">
            <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <meta name="color-scheme" content="{{colorScheme}}">
                <title>{{System.Net.WebUtility.HtmlEncode(title)}}</title>
                {{baseTag}}
                <link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/katex@0.16.21/dist/katex.min.css">
                <style>
            {{ThemePalette.PreviewDocumentCss}}
                </style>
            </head>
            <body>
            <div id="md-content">
            {{bodyHtml}}
            </div>
            <script src="https://cdn.jsdelivr.net/npm/katex@0.16.21/dist/katex.min.js"></script>
            <script src="https://cdn.jsdelivr.net/npm/mermaid@11.4.1/dist/mermaid.min.js"></script>
            <script>
                (function () {
                    window.__mdMermaidTheme = '{{mermaidTheme}}';

                    window.__mdSetTheme = function (dark) {
                        var theme = dark ? 'dark' : 'light';
                        document.documentElement.setAttribute('data-theme', theme);
                        document.documentElement.style.colorScheme = theme;
                        var meta = document.querySelector('meta[name="color-scheme"]');
                        if (meta) meta.setAttribute('content', theme);
                        window.__mdMermaidTheme = dark ? 'dark' : 'default';
                    };

                    window.__mdSetBaseHref = function (href) {
                        var base = document.querySelector('base');
                        if (!href) {
                            if (base) base.remove();
                            return;
                        }
                        if (!base) {
                            base = document.createElement('base');
                            document.head.insertBefore(base, document.head.firstChild);
                        }
                        base.href = href;
                    };

                    function renderMath() {
                        if (!window.katex) return;
                        document.querySelectorAll('#md-content span.math, #md-content div.math').forEach(function (el) {
                            if (el.getAttribute('data-rendered') === '1') return;
                            var tex = el.textContent || '';
                            try {
                                katex.render(tex, el, {
                                    throwOnError: false,
                                    displayMode: el.tagName.toLowerCase() === 'div'
                                });
                                el.setAttribute('data-rendered', '1');
                            } catch (e) { }
                        });
                    }

                    function renderMermaid() {
                        if (!window.mermaid) return;
                        mermaid.initialize({
                            startOnLoad: false,
                            theme: window.__mdMermaidTheme || 'default',
                            securityLevel: 'strict'
                        });
                        document.querySelectorAll('#md-content code.language-mermaid').forEach(function (code) {
                            var pre = code.parentElement;
                            if (!pre || pre.getAttribute('data-rendered') === '1') return;
                            var host = document.createElement('div');
                            host.className = 'mermaid';
                            host.textContent = code.textContent || '';
                            pre.replaceWith(host);
                        });
                        mermaid.run({ querySelector: '#md-content .mermaid' });
                    }

                    function enableAnchorNavigation() {
                        if (window.__mdAnchorsBound) return;
                        window.__mdAnchorsBound = true;
                        document.addEventListener('click', function (e) {
                            var a = e.target.closest('a');
                            if (!a) return;
                            var href = a.getAttribute('href');
                            if (href && href.charAt(0) === '#') {
                                e.preventDefault();
                                scrollToHash(href);
                            }
                        }, true);
                    }

                    function scrollToHash(hash) {
                        if (!hash || hash === '#') return;
                        var id = decodeURIComponent(hash.replace(/^#/, ''));
                        if (!id) return;
                        var el = document.getElementById(id) || document.getElementById(id.toLowerCase());
                        if (!el) {
                            var headings = document.querySelectorAll('h1,h2,h3,h4,h5,h6');
                            for (var i = 0; i < headings.length; i++) {
                                var h = headings[i];
                                if ((h.id || '') === id || slugify(h.textContent || '') === id) { el = h; break; }
                            }
                        }
                        if (!el) return;
                        el.scrollIntoView({ behavior: 'smooth', block: 'start' });
                    }

                    function slugify(text) {
                        return (text || '').toLowerCase().trim()
                            .replace(/[^\w\u00C0-\u024f\s-]/g, '')
                            .replace(/\s+/g, '-');
                    }

                    window.__mdRender = function () {
                        renderMath();
                        renderMermaid();
                    };

                    enableAnchorNavigation();
                    window.__mdRender();
                    if (location.hash) setTimeout(function () { scrollToHash(location.hash); }, 50);
                })();
            </script>
            </body>
            </html>
            """;
    }

    public static string ToExportHtml(
        string markdown,
        string title = "Document",
        bool darkTheme = false,
        string? baseHref = null) =>
        WrapInDocument(ConvertToHtml(markdown, darkTheme), title, darkTheme, baseHref);

    public static string ToPreviewHtml(string markdown, bool darkTheme = false, string? baseHref = null) =>
        WrapInDocument(ConvertToHtml(markdown, darkTheme), darkTheme: darkTheme, baseHref: baseHref) + ScrollSyncScript;

    private const string ScrollSyncScript = """
        <script>
            (function () {
                var lastPosted = -1;
                var ticking = false;
                function postRatio() {
                    if (window.__mdSyncSuppress) return;
                    var maxScroll = document.documentElement.scrollHeight - window.innerHeight;
                    var ratio = maxScroll > 0 ? window.scrollY / maxScroll : 0;
                    if (Math.abs(ratio - lastPosted) < 0.008) return;
                    lastPosted = ratio;
                    if (window.chrome && window.chrome.webview) {
                        window.chrome.webview.postMessage(JSON.stringify({ type: 'scroll', ratio: ratio }));
                    }
                }
                window.addEventListener('scroll', function () {
                    if (window.__mdSyncSuppress || ticking) return;
                    ticking = true;
                    requestAnimationFrame(function () {
                        ticking = false;
                        postRatio();
                    });
                }, { passive: true });
            })();
        </script>
        """;
}
