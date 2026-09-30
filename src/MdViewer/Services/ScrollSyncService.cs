using System.Globalization;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Wpf;

namespace MdViewer.Services;

/// <summary>
/// Keeps AvalonEdit and WebView2 aligned by markdown source line (0-based),
/// matching the VS Code preview approach — not by scroll-height ratio.
/// </summary>
public sealed class ScrollSyncService
{
    private enum ScrollSource
    {
        None,
        Editor,
        Preview
    }

    private readonly DispatcherTimer _editorFlushTimer;
    private readonly DispatcherTimer _releaseTimer;
    private ScrollSource _activeSource = ScrollSource.None;
    private int _lastEditorLine = -1;
    private int _lastPreviewLine = -1;
    private int _pendingEditorLine;
    private bool _editorFlushQueued;
    private bool _reloadSuppress;
    private int _reloadRestoreLine = -1;
    private WebView2? _webView;
    private Func<int>? _getEditorLine;
    private Action<int>? _setEditorLine;
    private Action<int>? _moveEditorCaret;
    private bool _enabled = true;

    public ScrollSyncService()
    {
        _editorFlushTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _editorFlushTimer.Tick += EditorFlushTimer_Tick;

        // Longer ownership window reduces editor↔preview feedback loops.
        _releaseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(420) };
        _releaseTimer.Tick += (_, _) =>
        {
            _releaseTimer.Stop();
            _activeSource = ScrollSource.None;
        };
    }

    public void Attach(
        WebView2 webView,
        Func<int> getEditorLine,
        Action<int> setEditorLine,
        Action<int>? moveEditorCaret = null)
    {
        _webView = webView;
        _getEditorLine = getEditorLine;
        _setEditorLine = setEditorLine;
        _moveEditorCaret = moveEditorCaret;
    }

    public void SetEnabled(bool enabled) => _enabled = enabled;

    /// <summary>True while a tab switch / document reload is restoring scroll.</summary>
    public bool IsReloadSuppressing => _reloadSuppress;

    /// <summary>Last known synced source line (0-based), or -1 if unknown.</summary>
    public int LastKnownLine =>
        _lastPreviewLine >= 0 ? _lastPreviewLine : _lastEditorLine;

    public void RememberLine(int line)
    {
        line = Math.Max(0, line);
        _lastEditorLine = line;
        _lastPreviewLine = line;
    }

    /// <summary>
    /// Reads the live preview source line from WebView2. Returns null if unavailable.
    /// </summary>
    public async Task<int?> QueryPreviewLineAsync()
    {
        if (_webView?.CoreWebView2 is null) return null;

        const string script = """
            (function() {
              if (typeof window.__mdGetLineAtScroll === 'function')
                return window.__mdGetLineAtScroll();
              return 0;
            })()
            """;

        try
        {
            var raw = await _webView.ExecuteScriptAsync(script);
            if (string.IsNullOrWhiteSpace(raw) || raw == "null") return null;
            if (int.TryParse(raw.Trim('"'), NumberStyles.Integer, CultureInfo.InvariantCulture, out var line))
                return Math.Max(0, line);
        }
        catch
        {
            // WebView may be mid-navigation
        }

        return null;
    }

    public void ApplyLineToEditor(int line)
    {
        line = Math.Max(0, line);
        _lastEditorLine = line;
        _lastPreviewLine = line;
        _setEditorLine?.Invoke(line);
    }

    /// <summary>
    /// Call before a full preview document reload so a transient scrollY=0
    /// cannot yank the editor to the top.
    /// </summary>
    public void BeginReload(int? restoreLine = null)
    {
        _reloadSuppress = true;
        _reloadRestoreLine = restoreLine ?? (_lastEditorLine >= 0
            ? _lastEditorLine
            : Math.Max(0, _getEditorLine?.Invoke() ?? 0));
        RememberLine(_reloadRestoreLine);
        _activeSource = ScrollSource.None;
        _editorFlushQueued = false;
        _editorFlushTimer.Stop();
    }

    public async Task EndReloadAsync()
    {
        var line = _reloadRestoreLine >= 0
            ? _reloadRestoreLine
            : Math.Max(0, _getEditorLine?.Invoke() ?? 0);

        await ApplyLineToPreviewAsync(line);

        await Task.Delay(200);
        _reloadSuppress = false;
        _reloadRestoreLine = -1;
    }

    public void OnEditorScrolled()
    {
        if (_reloadSuppress) return;

        var line = Math.Max(0, _getEditorLine?.Invoke() ?? 0);
        _lastEditorLine = line;

        if (!_enabled || _activeSource == ScrollSource.Preview) return;
        if (line == _lastPreviewLine) return;

        _pendingEditorLine = line;
        _activeSource = ScrollSource.Editor;
        _editorFlushQueued = true;

        if (!_editorFlushTimer.IsEnabled)
            _editorFlushTimer.Start();

        BumpReleaseTimer();
    }

    /// <summary>
    /// Jump preview to the caret line after an editor click (does not require scroll change).
    /// </summary>
    public void OnEditorCaretMoved(int line)
    {
        if (_reloadSuppress || !_enabled) return;
        if (_activeSource == ScrollSource.Preview) return;

        line = Math.Max(0, line);
        _lastEditorLine = line;
        if (line == _lastPreviewLine) return;

        _activeSource = ScrollSource.Editor;
        _lastPreviewLine = line;
        _pendingEditorLine = line;
        _editorFlushQueued = false;
        _editorFlushTimer.Stop();
        _ = ApplyLineToPreviewAsync(line);
        BumpReleaseTimer();
    }

    public void OnPreviewScrolled(int line, bool force = false)
    {
        line = Math.Max(0, line);
        _lastPreviewLine = line;

        if (!_enabled || _reloadSuppress) return;
        if (!force && _activeSource == ScrollSource.Editor) return;
        if (!force && line == _lastEditorLine) return;

        _activeSource = ScrollSource.Preview;
        _lastEditorLine = line;
        _setEditorLine?.Invoke(line);
        if (force)
            _moveEditorCaret?.Invoke(line);
        BumpReleaseTimer();
    }

    public async Task ApplyLineToPreviewAsync(int line)
    {
        if (_webView?.CoreWebView2 is null) return;

        line = Math.Max(0, line);
        _lastEditorLine = line;
        _lastPreviewLine = line;

        var script = $$"""
            (function() {
              if (typeof window.__mdScrollToLine === 'function')
                window.__mdScrollToLine({{line.ToString(CultureInfo.InvariantCulture)}});
            })();
            """;

        try
        {
            await _webView.ExecuteScriptAsync(script);
        }
        catch
        {
            // WebView may be mid-navigation
        }
    }

    public async Task RestorePreviewScrollAsync()
    {
        if (_reloadSuppress)
        {
            await EndReloadAsync();
            return;
        }

        var line = _lastEditorLine >= 0
            ? _lastEditorLine
            : Math.Max(0, _getEditorLine?.Invoke() ?? 0);
        await ApplyLineToPreviewAsync(line);
    }

    private async void EditorFlushTimer_Tick(object? sender, EventArgs e)
    {
        if (!_editorFlushQueued || _reloadSuppress)
        {
            _editorFlushTimer.Stop();
            return;
        }

        _editorFlushQueued = false;
        _editorFlushTimer.Stop();

        var line = _pendingEditorLine;
        if (line == _lastPreviewLine) return;

        _lastEditorLine = line;
        _lastPreviewLine = line;
        await ApplyLineToPreviewAsync(line);
    }

    private void BumpReleaseTimer()
    {
        _releaseTimer.Stop();
        _releaseTimer.Start();
    }
}
