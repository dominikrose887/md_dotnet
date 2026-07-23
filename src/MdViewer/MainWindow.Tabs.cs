using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MdViewer.Models;
using MdViewer.Services;

namespace MdViewer;

public partial class MainWindow
{
    public ObservableCollection<DocumentTab> Tabs { get; } = [];

    private DocumentTab? _activeTab;
    private bool _suppressTabEditorSync;
    private DocumentTab? _dragTab;
    private Point _dragStart;
    private bool _dragMoved;

    private DocumentTab? ActiveTab => _activeTab;

    private void InitializeTabs()
    {
        DataContext = this;
        TabStrip.ItemsSource = Tabs;
        TabStrip.PreviewMouseMove += Tab_PreviewMouseMove;
        TabStrip.PreviewMouseLeftButtonUp += Tab_PreviewMouseLeftButtonUp;

        // Start with one empty untitled tab.
        var tab = CreateUntitledTab();
        Tabs.Add(tab);
        ActivateTab(tab, updateEditor: true);
    }

    private static DocumentTab CreateUntitledTab() => new()
    {
        FilePath = null,
        Content = string.Empty,
        SavedContent = string.Empty
    };

    private DocumentTab CreateTabFromFile(string path, string content)
    {
        var full = Path.GetFullPath(path);
        return new DocumentTab
        {
            FilePath = full,
            Content = content,
            SavedContent = content
        };
    }

    private void PersistActiveTabFromEditor()
    {
        if (_activeTab is null || _suppressTabEditorSync) return;
        _activeTab.Content = Editor.Text;
        _activeTab.CaretOffset = Editor.CaretOffset;
        _activeTab.ScrollRatio = CaptureCurrentScrollRatio();
        _activeTab.NotifyTitleChanged();
    }

    private double CaptureCurrentScrollRatio()
    {
        // Preview-only: editor is collapsed, so use the last known preview ratio.
        if (_viewMode == ViewMode.Viewer)
        {
            var known = _scrollSync.LastKnownRatio;
            return known >= 0 ? known : (_activeTab?.ScrollRatio ?? 0);
        }

        return GetEditorScrollRatio();
    }

    private void RememberActiveTabScroll(double ratio)
    {
        if (_activeTab is null || _suppressTabEditorSync) return;
        _activeTab.ScrollRatio = ratio;
        _scrollSync.RememberRatio(ratio);
    }

    private void ActivateTab(DocumentTab tab, bool updateEditor)
    {
        if (_activeTab == tab && updateEditor == false) return;

        if (_activeTab is not null && _activeTab != tab)
            PersistActiveTabFromEditor();

        foreach (var t in Tabs)
            t.IsActive = t == tab;

        _activeTab = tab;
        _currentFilePath = tab.FilePath;

        if (!updateEditor) return;

        var restoreRatio = Math.Clamp(tab.ScrollRatio, 0, 1);
        _scrollSync.BeginReload(restoreRatio);

        _suppressTabEditorSync = true;
        try
        {
            Editor.Text = tab.Content;
            _savedContent = tab.SavedContent;
            _isDirty = tab.IsDirty;

            Editor.CaretOffset = Math.Clamp(tab.CaretOffset, 0, Editor.Document.TextLength);
        }
        finally
        {
            _suppressTabEditorSync = false;
        }

        // AvalonEdit resets scroll after Text assignment; restore after layout.
        Dispatcher.BeginInvoke(() =>
        {
            if (_activeTab != tab) return;
            SetEditorScrollRatio(restoreRatio);
            _scrollSync.RememberRatio(restoreRatio);
        }, System.Windows.Threading.DispatcherPriority.Loaded);

        UpdateTitle();
        RefreshFileTree();
        RebuildCodeBlockHighlighting();
        StatusText.Text = tab.FilePath ?? "Untitled";
        _ = UpdatePreviewAsync(scrollRatio: restoreRatio);
    }

    private void OpenOrFocusFile(string path, bool preferPreviewIfNonEmpty = true)
    {
        if (!File.Exists(path)) return;
        var full = Path.GetFullPath(path);

        var existing = Tabs.FirstOrDefault(t =>
            t.FilePath is not null &&
            string.Equals(t.FilePath, full, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            ActivateTab(existing, updateEditor: true);
            if (preferPreviewIfNonEmpty && !string.IsNullOrWhiteSpace(existing.Content))
                SetViewMode(ViewMode.Viewer);
            return;
        }

        try
        {
            var content = File.ReadAllText(full, Encoding.UTF8);
            var tab = CreateTabFromFile(full, content);

            // Replace a single pristine untitled tab.
            if (Tabs.Count == 1 &&
                Tabs[0].FilePath is null &&
                !Tabs[0].IsDirty &&
                string.IsNullOrEmpty(Tabs[0].Content))
            {
                Tabs[0] = tab;
            }
            else
            {
                Tabs.Add(tab);
            }

            _settings.AddRecentFile(full);
            _settings.Save();
            ActivateTab(tab, updateEditor: true);

            if (preferPreviewIfNonEmpty && !string.IsNullOrWhiteSpace(content))
                SetViewMode(ViewMode.Viewer);

            AppLogger.Info($"Opened tab: {full}");
        }
        catch (Exception ex)
        {
            AppLogger.Error($"OpenOrFocusFile failed: {path}", ex);
            MessageBox.Show($"Could not open file:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void NewTab()
    {
        PersistActiveTabFromEditor();
        var tab = CreateUntitledTab();
        Tabs.Add(tab);
        ActivateTab(tab, updateEditor: true);
        SetViewMode(ViewMode.Split);
        StatusText.Text = "New document";
    }

    private bool CloseTab(DocumentTab tab)
    {
        if (tab.IsDirty || (tab == _activeTab && Editor.Text != tab.SavedContent && !_suppressTabEditorSync))
        {
            if (tab == _activeTab)
                PersistActiveTabFromEditor();

            if (tab.IsDirty)
            {
                var result = MessageBox.Show(
                    $"Save changes to {tab.Title}?",
                    "Unsaved Changes",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Cancel) return false;
                if (result == MessageBoxResult.Yes)
                {
                    if (tab != _activeTab)
                        ActivateTab(tab, updateEditor: true);

                    if (!SaveFile()) return false;
                    PersistActiveTabFromEditor();
                    if (tab.IsDirty) return false;
                }
            }
        }

        var index = Tabs.IndexOf(tab);
        if (index < 0) return true;

        var wasActive = tab == _activeTab;
        Tabs.RemoveAt(index);

        if (Tabs.Count == 0)
        {
            var fresh = CreateUntitledTab();
            Tabs.Add(fresh);
            ActivateTab(fresh, updateEditor: true);
            SetViewMode(ViewMode.Split);
            return true;
        }

        if (wasActive)
        {
            var next = Tabs[Math.Clamp(index, 0, Tabs.Count - 1)];
            ActivateTab(next, updateEditor: true);
        }

        return true;
    }

    private void CloseAllTabs(Func<DocumentTab, bool>? predicate = null)
    {
        var targets = Tabs.Where(t => predicate?.Invoke(t) ?? true).ToList();
        foreach (var tab in targets)
        {
            if (!CloseTab(tab))
                break;
        }
    }

    private bool ConfirmDiscardAllTabs()
    {
        PersistActiveTabFromEditor();
        foreach (var tab in Tabs.ToList())
        {
            if (!tab.IsDirty) continue;

            ActivateTab(tab, updateEditor: true);
            if (!ConfirmDiscardChanges())
                return false;
        }

        return true;
    }

    private void Tab_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: DocumentTab tab }) return;
        if (e.OriginalSource is DependencyObject source && FindAncestor<Button>(source) is not null)
            return;

        _dragTab = tab;
        _dragStart = e.GetPosition(TabStrip);
        _dragMoved = false;
        ActivateTab(tab, updateEditor: true);
    }

    private void Tab_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragTab is null || e.LeftButton != MouseButtonState.Pressed) return;

        var pos = e.GetPosition(TabStrip);
        if (!_dragMoved)
        {
            if (Math.Abs(pos.X - _dragStart.X) < 6 && Math.Abs(pos.Y - _dragStart.Y) < 6)
                return;
            _dragMoved = true;
            Mouse.Capture(TabStrip);
        }

        var target = FindTabAtPoint(pos);
        if (target is null || target == _dragTab) return;

        var oldIndex = Tabs.IndexOf(_dragTab);
        var newIndex = Tabs.IndexOf(target);
        if (oldIndex < 0 || newIndex < 0 || oldIndex == newIndex) return;

        Tabs.Move(oldIndex, newIndex);
    }

    private void Tab_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (Mouse.Captured == TabStrip)
            Mouse.Capture(null);
        _dragTab = null;
        _dragMoved = false;
    }

    private void Tab_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: DocumentTab tab }) return;
        ActivateTab(tab, updateEditor: true);

        var menu = new ContextMenu();
        menu.Items.Add(CreateMenuItem("Close", (_, _) => CloseTab(tab)));
        menu.Items.Add(CreateMenuItem("Close others", (_, _) => CloseAllTabs(t => t != tab)));
        menu.Items.Add(CreateMenuItem("Close all", (_, _) => CloseAllTabs()));
        menu.Items.Add(new Separator());

        var index = Tabs.IndexOf(tab);
        menu.Items.Add(CreateMenuItem("Close all to the left", (_, _) =>
        {
            foreach (var t in Tabs.Take(index).ToList())
                if (!CloseTab(t)) break;
        }));
        menu.Items.Add(CreateMenuItem("Close all to the right", (_, _) =>
        {
            foreach (var t in Tabs.Skip(index + 1).ToList())
                if (!CloseTab(t)) break;
        }));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("Copy path", (_, _) =>
        {
            if (!string.IsNullOrEmpty(tab.FilePath))
                Clipboard.SetText(tab.FilePath);
        }));

        menu.PlacementTarget = sender as UIElement;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void TabClose_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: DocumentTab tab })
            CloseTab(tab);
        e.Handled = true;
    }

    private DocumentTab? FindTabAtPoint(Point point)
    {
        var hit = VisualTreeHelper.HitTest(TabStrip, point);
        var current = hit?.VisualHit as DependencyObject;
        while (current is not null)
        {
            if (current is FrameworkElement { Tag: DocumentTab tab })
                return tab;
            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    public void HandleExternalArgs(string[] args)
    {
        WindowActivation.BringToFront(this);

        if (args.Length == 0) return;

        foreach (var raw in args)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var path = raw.Trim().Trim('"');

            if (File.Exists(path) && IsMarkdownPath(path))
                OpenOrFocusFile(path);
            else if (Directory.Exists(path))
                OpenFolderFromPath(path);
        }
    }

    private static bool IsMarkdownPath(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".md", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".markdown", StringComparison.OrdinalIgnoreCase);
    }
}
