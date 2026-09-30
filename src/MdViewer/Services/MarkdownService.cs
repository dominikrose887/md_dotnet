using Markdig;
using Markdig.Extensions.AutoIdentifiers;

namespace MdViewer.Services;

public static class MarkdownService
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
        .UseYamlFrontMatter()
        .Use(new SourceLineMapExtension())
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
                            host.className = 'mermaid code-line';
                            var line = pre.getAttribute('data-line');
                            if (line !== null) host.setAttribute('data-line', line);
                            host.textContent = code.textContent || '';
                            pre.replaceWith(host);
                        });
                        mermaid.run({ querySelector: '#md-content .mermaid' });
                    }

                    function enableAnchorNavigation() {
                        if (window.__mdAnchorsBound) return;
                        window.__mdAnchorsBound = true;
                        document.addEventListener('click', function (e) {
                            var a = e.target.closest ? e.target.closest('a') : null;
                            if (!a) return;
                            var href = a.getAttribute('href');
                            if (!href) return;
                            if (href.charAt(0) === '#') {
                                e.preventDefault();
                                e.stopPropagation();
                                scrollToHash(href);
                            }
                        }, true);
                    }

                    function findHeadingByHash(id) {
                        if (!id) return null;
                        var el = document.getElementById(id) || document.getElementById(id.toLowerCase());
                        if (el) return el;
                        var headings = document.querySelectorAll('#md-content h1,#md-content h2,#md-content h3,#md-content h4,#md-content h5,#md-content h6');
                        for (var i = 0; i < headings.length; i++) {
                            var h = headings[i];
                            var hid = h.id || '';
                            if (hid === id || hid.toLowerCase() === id.toLowerCase()) return h;
                            if (slugify(h.textContent || '') === id || slugify(h.textContent || '') === id.toLowerCase()) return h;
                        }
                        return null;
                    }

                    function scrollToHash(hash) {
                        if (!hash || hash === '#') return false;
                        var id;
                        try { id = decodeURIComponent(hash.replace(/^#/, '')); }
                        catch (ex) { id = hash.replace(/^#/, ''); }
                        if (!id) return false;
                        var el = findHeadingByHash(id);
                        if (!el) return false;

                        window.__mdSyncSuppress = true;
                        try {
                            var top = Math.max(0, el.getBoundingClientRect().top + window.scrollY - 8);
                            window.scrollTo({ top: top, left: 0, behavior: 'instant' });
                        } finally {
                            setTimeout(function () { window.__mdSyncSuppress = false; }, 280);
                        }

                        var lineEl = el.closest ? el.closest('.code-line[data-line]') : null;
                        if (!lineEl && el.getAttribute) lineEl = el.getAttribute('data-line') != null ? el : null;
                        var line = lineEl ? parseInt(lineEl.getAttribute('data-line'), 10) : NaN;
                        if (!isNaN(line) && window.chrome && window.chrome.webview) {
                            window.chrome.webview.postMessage(JSON.stringify({
                                type: 'scroll',
                                line: line,
                                force: true
                            }));
                        }
                        return true;
                    }

                    function slugify(text) {
                        return (text || '').toLowerCase().trim()
                            .replace(/[^\w\u00C0-\u024f\s-]/g, '')
                            .replace(/\s+/g, '-');
                    }

                    window.__mdScrollToHash = scrollToHash;
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

                function getCodeLineElements() {
                    var nodes = document.querySelectorAll('.code-line[data-line]');
                    var result = [];
                    for (var i = 0; i < nodes.length; i++) {
                        var el = nodes[i];
                        var line = parseInt(el.getAttribute('data-line'), 10);
                        if (isNaN(line)) continue;
                        var rect = el.getBoundingClientRect();
                        if (rect.height === 0 && rect.width === 0) continue;
                        if (window.getComputedStyle(el).display === 'none') continue;
                        result.push({ element: el, line: line });
                    }
                    result.sort(function (a, b) { return a.line - b.line; });
                    return result;
                }

                function documentTop(el) {
                    return el.getBoundingClientRect().top + window.scrollY;
                }

                function getLineAtScroll() {
                    var lines = getCodeLineElements();
                    if (lines.length === 0) return 0;
                    // Anchor near the top of the viewport (VS Code-style).
                    var offset = window.scrollY + Math.min(32, window.innerHeight * 0.08);
                    var lo = 0, hi = lines.length - 1;
                    while (lo < hi) {
                        var mid = Math.ceil((lo + hi) / 2);
                        if (documentTop(lines[mid].element) > offset) hi = mid - 1;
                        else lo = mid;
                    }
                    return lines[lo].line;
                }

                function scrollToLine(line) {
                    window.__mdSyncSuppress = true;
                    try {
                        var lines = getCodeLineElements();
                        if (lines.length === 0) return;
                        var target = lines[0];
                        for (var i = 0; i < lines.length; i++) {
                            if (lines[i].line <= line) target = lines[i];
                            else break;
                        }
                        var top = Math.max(0, documentTop(target.element));
                        window.scrollTo({ top: top, left: 0, behavior: 'instant' });
                    } finally {
                        setTimeout(function () { window.__mdSyncSuppress = false; }, 260);
                    }
                }

                function postLine(force, lineOverride) {
                    if (window.__mdSyncSuppress) return;
                    var line = typeof lineOverride === 'number' ? lineOverride : getLineAtScroll();
                    if (!force && line === lastPosted) return;
                    lastPosted = line;
                    if (window.chrome && window.chrome.webview) {
                        window.chrome.webview.postMessage(JSON.stringify({
                            type: 'scroll',
                            line: line,
                            force: !!force
                        }));
                    }
                }

                window.__mdScrollToLine = scrollToLine;
                window.__mdGetLineAtScroll = getLineAtScroll;

                window.addEventListener('scroll', function () {
                    if (window.__mdSyncSuppress || ticking) return;
                    ticking = true;
                    requestAnimationFrame(function () {
                        ticking = false;
                        postLine(false);
                    });
                }, { passive: true });

                window.addEventListener('click', function (e) {
                    // Link clicks are handled by host navigation / anchor script — don't steal them.
                    if (e.target && e.target.closest && e.target.closest('a')) return;

                    var el = e.target && e.target.closest ? e.target.closest('.code-line[data-line]') : null;
                    if (el) {
                        var line = parseInt(el.getAttribute('data-line'), 10);
                        if (!isNaN(line)) {
                            postLine(true, line);
                            return;
                        }
                    }
                    postLine(true);
                }, true);
            })();
        </script>
        """;
}
