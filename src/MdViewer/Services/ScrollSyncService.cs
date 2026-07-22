using System.Globalization;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Wpf;

namespace MdViewer.Services;

/// <summary>
/// Keeps AvalonEdit and WebView2 scroll positions aligned without feedback loops.
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
    private double _lastEditorRatio = -1;
    private double _lastPreviewRatio = -1;
    private double _pendingEditorRatio;
    private bool _editorFlushQueued;
    private bool _reloadSuppress;
    private double _reloadRestoreRatio = -1;
    private WebView2? _webView;
    private Func<double>? _getEditorRatio;
    private Action<double>? _setEditorRatio;
    private bool _enabled = true;

    public ScrollSyncService()
    {
        _editorFlushTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(32) };
        _editorFlushTimer.Tick += EditorFlushTimer_Tick;

        _releaseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _releaseTimer.Tick += (_, _) =>
        {
            _releaseTimer.Stop();
            _activeSource = ScrollSource.None;
        };
    }

    public void Attach(
        WebView2 webView,
        Func<double> getEditorRatio,
        Action<double> setEditorRatio)
    {
        _webView = webView;
        _getEditorRatio = getEditorRatio;
        _setEditorRatio = setEditorRatio;
    }

    public void SetEnabled(bool enabled) => _enabled = enabled;

    public double CurrentEditorRatio => _getEditorRatio?.Invoke() ?? 0;

    /// <summary>
    /// Call before a full preview document reload so a transient scrollY=0
    /// cannot yank the editor to the top.
    /// </summary>
    public void BeginReload()
    {
        _reloadSuppress = true;
        _reloadRestoreRatio = _lastEditorRatio >= 0
            ? _lastEditorRatio
            : Clamp01(_getEditorRatio?.Invoke() ?? 0);
        _activeSource = ScrollSource.None;
        _editorFlushQueued = false;
        _editorFlushTimer.Stop();
    }

    public async Task EndReloadAsync()
    {
        var ratio = _reloadRestoreRatio >= 0
            ? _reloadRestoreRatio
            : Clamp01(_getEditorRatio?.Invoke() ?? 0);

        await ApplyRatioToPreviewAsync(ratio);

        // Late scroll events from the new document must still be ignored briefly.
        await Task.Delay(160);
        _reloadSuppress = false;
        _reloadRestoreRatio = -1;
    }

    public void OnEditorScrolled()
    {
        if (!_enabled || _reloadSuppress || _activeSource == ScrollSource.Preview) return;

        var ratio = Clamp01(_getEditorRatio?.Invoke() ?? 0);
        if (NearlyEqual(ratio, _lastEditorRatio)) return;

        _pendingEditorRatio = ratio;
        _activeSource = ScrollSource.Editor;
        _editorFlushQueued = true;

        if (!_editorFlushTimer.IsEnabled)
            _editorFlushTimer.Start();

        BumpReleaseTimer();
    }

    public void OnPreviewScrolled(double ratio)
    {
        if (!_enabled || _reloadSuppress || _activeSource == ScrollSource.Editor) return;

        ratio = Clamp01(ratio);
        if (NearlyEqual(ratio, _lastPreviewRatio) && NearlyEqual(ratio, _lastEditorRatio))
            return;

        _activeSource = ScrollSource.Preview;
        _lastPreviewRatio = ratio;
        _lastEditorRatio = ratio;
        _setEditorRatio?.Invoke(ratio);
        BumpReleaseTimer();
    }

    public async Task ApplyRatioToPreviewAsync(double ratio)
    {
        if (_webView?.CoreWebView2 is null) return;

        ratio = Clamp01(ratio);
        _lastEditorRatio = ratio;
        _lastPreviewRatio = ratio;

        var script = $$"""
            (function() {
              window.__mdSyncSuppress = true;
              var max = document.documentElement.scrollHeight - window.innerHeight;
              var y = max > 0 ? max * {{ratio.ToString(CultureInfo.InvariantCulture)}} : 0;
              window.scrollTo({ top: y, left: 0, behavior: 'instant' });
              setTimeout(function() { window.__mdSyncSuppress = false; }, 120);
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

        var ratio = _lastEditorRatio >= 0
            ? _lastEditorRatio
            : Clamp01(_getEditorRatio?.Invoke() ?? 0);
        await ApplyRatioToPreviewAsync(ratio);
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

        var ratio = _pendingEditorRatio;
        if (NearlyEqual(ratio, _lastPreviewRatio)) return;

        _lastEditorRatio = ratio;
        _lastPreviewRatio = ratio;
        await ApplyRatioToPreviewAsync(ratio);
    }

    private void BumpReleaseTimer()
    {
        _releaseTimer.Stop();
        _releaseTimer.Start();
    }

    private static double Clamp01(double value) =>
        value < 0 ? 0 : value > 1 ? 1 : value;

    private static bool NearlyEqual(double a, double b) =>
        Math.Abs(a - b) < 0.008;
}
