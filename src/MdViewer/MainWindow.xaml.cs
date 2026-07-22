using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MdViewer.Models;
using MdViewer.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace MdViewer;

public partial class MainWindow : Window
{
    private string? _currentFilePath;
    private string? _folderPath;
    private string _savedContent = string.Empty;
    private bool _isDirty;
    private bool _webViewReady;
    private bool _previewShellReady;
    private bool _suppressTreeSelection;
    private bool _findBarOpen;
    private ViewMode _viewMode = ViewMode.Split;
    private FindReplaceService? _findReplace;
    private FocusModeService? _focusMode;
    private TypewriterModeService? _typewriterMode;
    private CodeBlockHighlightingTransformer? _codeBlockHighlighter;
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _previewTimer;
    private readonly DispatcherTimer _findTimer;
    private readonly DispatcherTimer _statsTimer;
    private readonly ScrollSyncService _scrollSync = new();

    public MainWindow()
    {
        AppLogger.Info("MainWindow constructor begin");

        try
        {
            InitializeComponent();
            AppLogger.Info("InitializeComponent done");

            _settings = AppSettings.Load();
            AppLogger.Info($"Settings loaded (dark={_settings.IsDarkTheme}, sidebar={_settings.IsSidebarVisible}, lastFolder={_settings.LastFolder ?? "(none)"})");

            _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _previewTimer.Tick += PreviewTimer_Tick;

            _findTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            _findTimer.Tick += (_, _) =>
            {
                _findTimer.Stop();
                RunFind();
            };

            _statsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _statsTimer.Tick += (_, _) =>
            {
                _statsTimer.Stop();
                UpdateStats();
            };

            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;
            Editor.TextArea.TextView.ScrollOffsetChanged += Editor_ScrollOffsetChanged;

            _scrollSync.Attach(
                PreviewWebView,
                getEditorRatio: GetEditorScrollRatio,
                setEditorRatio: SetEditorScrollRatio);

            _findReplace = new FindReplaceService(Editor);
            _focusMode = new FocusModeService(Editor);
            _typewriterMode = new TypewriterModeService(Editor);
            _codeBlockHighlighter = new CodeBlockHighlightingTransformer();
            Editor.TextArea.TextView.LineTransformers.Add(_codeBlockHighlighter);
            AppLogger.Info("Editor services ready");

            CommandBindings.Add(new CommandBinding(ApplicationCommands.New, (_, _) => NewFile()));
            CommandBindings.Add(new CommandBinding(ApplicationCommands.Open, (_, _) => OpenFile()));
            CommandBindings.Add(new CommandBinding(ApplicationCommands.Save, (_, _) => SaveFile()));
            CommandBindings.Add(new CommandBinding(ApplicationCommands.Find, (_, _) => ShowFind()));
            CommandBindings.Add(new CommandBinding(ApplicationCommands.Replace, (_, _) => ShowFind(focusReplace: true)));

            var saveAsCommand = new RoutedUICommand("Save As", "SaveAs", typeof(MainWindow));
            CommandBindings.Add(new CommandBinding(saveAsCommand, (_, _) => SaveAsFile()));
            InputBindings.Add(new KeyBinding(saveAsCommand, Key.S, ModifierKeys.Control | ModifierKeys.Shift));

            var openFolderCommand = new RoutedUICommand("Open Folder", "OpenFolder", typeof(MainWindow));
            CommandBindings.Add(new CommandBinding(openFolderCommand, (_, _) => OpenFolder()));
            InputBindings.Add(new KeyBinding(openFolderCommand, Key.O, ModifierKeys.Control | ModifierKeys.Shift));

            var formatCommand = new RoutedUICommand("Format Document", "FormatDocument", typeof(MainWindow));
            CommandBindings.Add(new CommandBinding(formatCommand, (_, _) => FormatDocument()));
            InputBindings.Add(new KeyBinding(formatCommand, Key.F, ModifierKeys.Control | ModifierKeys.Shift));

            AppLogger.Info("Applying theme");
            ApplyTheme(_settings.IsDarkTheme);
            SetSidebarVisible(_settings.IsSidebarVisible);
            _focusMode.IsEnabled = _settings.IsFocusMode;
            _typewriterMode.IsEnabled = _settings.IsTypewriterMode;
            ToolbarFocusButton.IsChecked = _settings.IsFocusMode;
            ToolbarTypewriterButton.IsChecked = _settings.IsTypewriterMode;
            UpdateTitle();
            UpdateStats();
            SetViewMode(ViewMode.Split);
            RebuildCodeBlockHighlighting();

            if (!string.IsNullOrEmpty(_settings.LastFolder) && Directory.Exists(_settings.LastFolder))
            {
                AppLogger.Info($"Restoring last folder: {_settings.LastFolder}");
                OpenFolderPath(_settings.LastFolder, selectFirstFile: false);
            }

            AppLogger.Info("MainWindow constructor complete");
        }
        catch (Exception ex)
        {
            AppLogger.Fatal("MainWindow constructor failed", ex);
            throw;
        }
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        AppLogger.Info("MainWindow closed");
    }

    public void OpenFileFromPath(string path)
    {
        if (!File.Exists(path)) return;
        LoadFile(path, confirmDiscard: true, adoptWorkspaceIfEmpty: true);
    }

    public void OpenFolderFromPath(string path)
    {
        if (!Directory.Exists(path)) return;
        OpenFolderPath(path, selectFirstFile: true);
    }

    public void DisableSyntaxHighlightingSafe()
    {
        try
        {
            AppLogger.Warn("Disabling syntax highlighting to recover from highlighting error");
            Editor.SyntaxHighlighting = null;
        }
        catch (Exception ex)
        {
            AppLogger.Error("DisableSyntaxHighlightingSafe failed", ex);
        }
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        AppLogger.Info("MainWindow Loaded — initializing WebView2");
        try
        {
            await PreviewWebView.EnsureCoreWebView2Async();
            PreviewWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            PreviewWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            PreviewWebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
            PreviewWebView.CoreWebView2.ProcessFailed += (_, args) =>
                AppLogger.Error($"WebView2 process failed: {args.ProcessFailedKind}");
            PreviewWebView.NavigationCompleted += PreviewWebView_NavigationCompleted;
            _webViewReady = true;
            AppLogger.Info("WebView2 ready");
            await UpdatePreviewAsync();
        }
        catch (Exception ex)
        {
            AppLogger.Error("WebView2 initialization failed", ex);
            StatusText.Text = $"Preview unavailable: {ex.Message}";
        }
    }

    private async void PreviewWebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            _previewShellReady = false;
            return;
        }

        _previewShellReady = true;
        await _scrollSync.RestorePreviewScrollAsync();
    }

    private void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var message = JsonDocument.Parse(e.TryGetWebMessageAsString());
            if (message.RootElement.GetProperty("type").GetString() != "scroll") return;

            var ratio = message.RootElement.GetProperty("ratio").GetDouble();
            Dispatcher.Invoke(() => _scrollSync.OnPreviewScrolled(ratio));
        }
        catch
        {
            // Ignore malformed scroll messages
        }
    }

    private void Editor_ScrollOffsetChanged(object? sender, EventArgs e)
    {
        if (!_webViewReady || _viewMode == ViewMode.Viewer) return;
        _scrollSync.OnEditorScrolled();
    }

    private double GetEditorScrollRatio()
    {
        var maxScroll = Math.Max(0, Editor.ExtentHeight - Editor.ViewportHeight);
        return maxScroll > 0 ? Editor.VerticalOffset / maxScroll : 0;
    }

    private void SetEditorScrollRatio(double ratio)
    {
        var maxScroll = Math.Max(0, Editor.ExtentHeight - Editor.ViewportHeight);
        Editor.ScrollToVerticalOffset(maxScroll * Math.Clamp(ratio, 0, 1));
    }

    private void Editor_TextChanged(object? sender, EventArgs e)
    {
        _isDirty = Editor.Text != _savedContent;
        UpdateTitle();
        _previewTimer.Stop();
        _previewTimer.Start();
        _statsTimer.Stop();
        _statsTimer.Start();
        RebuildCodeBlockHighlighting();
        _focusMode?.Refresh();

        if (_findBarOpen && !string.IsNullOrEmpty(FindTextBox.Text))
        {
            _findTimer.Stop();
            _findTimer.Start();
        }
    }

    private void RebuildCodeBlockHighlighting()
    {
        EnsureTransformerOrder();
        _codeBlockHighlighter?.Rebuild(Editor.Document);
        Editor.TextArea.TextView.Redraw();
    }

    private void UpdateStats()
    {
        var stats = DocumentStatsService.Calculate(Editor.Text);
        StatusStatsText.Text = stats.ToStatusText();
    }

    private async void PreviewTimer_Tick(object? sender, EventArgs e)
    {
        _previewTimer.Stop();
        await UpdatePreviewAsync();
    }

    private async Task UpdatePreviewAsync(bool forceNavigate = false)
    {
        if (!_webViewReady) return;

        try
        {
            var dark = _settings.IsDarkTheme;
            var bodyHtml = MarkdownService.ConvertToHtml(Editor.Text, dark);

            if (!forceNavigate && _previewShellReady && PreviewWebView.CoreWebView2 is not null)
            {
                var payload = JsonSerializer.Serialize(bodyHtml);
                var darkJs = dark ? "true" : "false";
                var script = $$"""
                    (function(){
                      window.__mdSyncSuppress = true;
                      var root = document.getElementById('md-content');
                      if (!root) return 'missing';
                      var y = window.scrollY;
                      if (window.__mdSetTheme) window.__mdSetTheme({{darkJs}});
                      root.innerHTML = {{payload}};
                      if (window.__mdRender) window.__mdRender();
                      window.scrollTo({ top: y, left: 0, behavior: 'instant' });
                      setTimeout(function(){ window.__mdSyncSuppress = false; }, 80);
                      return 'ok';
                    })();
                    """;

                var result = await PreviewWebView.ExecuteScriptAsync(script);
                if (result.Contains("ok", StringComparison.Ordinal))
                    return;

                _previewShellReady = false;
            }

            _scrollSync.BeginReload();
            PreviewWebView.NavigateToString(MarkdownService.ToPreviewHtml(Editor.Text, dark));
        }
        catch (Exception ex)
        {
            AppLogger.Error("UpdatePreviewAsync failed", ex);
        }
    }

    private void UpdateTitle()
    {
        var name = _currentFilePath is not null
            ? Path.GetFileName(_currentFilePath)
            : "Untitled";
        var dirty = _isDirty ? " *" : "";
        Title = $"{name}{dirty} - MdViewer";
    }

    private void ApplyTheme(bool dark)
    {
        AppLogger.Info($"ApplyTheme dark={dark}");
        _settings.IsDarkTheme = dark;

        void SetBrush(string key, string hex) =>
            Application.Current.Resources[key] = new SolidColorBrush(ThemePalette.Parse(hex));

        var palette = dark ? ThemePalette.Dark : ThemePalette.Light;
        ThemePalette.ApplyBrushes(palette, SetBrush);
        ApplyPreviewChrome(palette, dark);

        try
        {
            Editor.SyntaxHighlighting = HighlightingService.LoadMarkdown(dark);
            Editor.TextArea.SelectionBrush = new SolidColorBrush(ThemePalette.Parse(palette.Selection));
            Editor.TextArea.SelectionBorder = null;
            Editor.Options.EnableHyperlinks = false;
            Editor.Options.EnableEmailHyperlinks = false;
            Editor.Options.AllowScrollBelowDocument = false;

            _findReplace?.SetMarkerBrush(new SolidColorBrush(ThemePalette.Parse(palette.FindHighlight))
            {
                Opacity = dark ? 0.7 : 1.0
            });

            _codeBlockHighlighter?.SetTheme(dark);
            _focusMode?.SetDimBrush(new SolidColorBrush(ThemePalette.Parse(palette.FocusDim)));

            EnsureTransformerOrder();
            RebuildCodeBlockHighlighting();
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to apply editor highlighting/theme options", ex);
        }

        ToolbarDarkButton.IsChecked = dark;
        _ = UpdatePreviewAsync();
    }

    private void ApplyPreviewChrome(ThemePalette.Swatch palette, bool dark)
    {
        var bg = ThemePalette.Parse(palette.EditorBackground);
        PreviewWebView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(bg.A, bg.R, bg.G, bg.B);

        if (!_webViewReady || !_previewShellReady || PreviewWebView.CoreWebView2 is null)
            return;

        var darkJs = dark ? "true" : "false";
        _ = PreviewWebView.ExecuteScriptAsync(
            $"(function(){{ if (window.__mdSetTheme) window.__mdSetTheme({darkJs}); }})();");
    }

    private void EnsureTransformerOrder()
    {
        var transformers = Editor.TextArea.TextView.LineTransformers;
        if (_codeBlockHighlighter is not null)
        {
            transformers.Remove(_codeBlockHighlighter);
            transformers.Add(_codeBlockHighlighter);
        }

        _focusMode?.BringToFront();
    }

    private void SetSidebarVisible(bool visible)
    {
        _settings.IsSidebarVisible = visible;
        ToolbarSidebarButton.IsChecked = visible;

        if (visible)
        {
            SidebarColumn.Width = new GridLength(240);
            SidebarSplitterColumn.Width = new GridLength(6);
            SidebarPanel.Visibility = Visibility.Visible;
            SidebarSplitter.Visibility = Visibility.Visible;
        }
        else
        {
            SidebarColumn.Width = new GridLength(0);
            SidebarSplitterColumn.Width = new GridLength(0);
            SidebarPanel.Visibility = Visibility.Collapsed;
            SidebarSplitter.Visibility = Visibility.Collapsed;
        }
    }

    private void SetViewMode(ViewMode mode)
    {
        _viewMode = mode;

        ToolbarEditorButton.IsChecked = mode == ViewMode.Editor;
        ToolbarViewerButton.IsChecked = mode == ViewMode.Viewer;
        ToolbarSplitButton.IsChecked = mode == ViewMode.Split;

        StatusModeText.Text = mode switch
        {
            ViewMode.Editor => "Editor",
            ViewMode.Viewer => "Preview",
            _ => "Split"
        };

        switch (mode)
        {
            case ViewMode.Editor:
                EditorColumn.Width = new GridLength(1, GridUnitType.Star);
                SplitterColumn.Width = new GridLength(0);
                PreviewColumn.Width = new GridLength(0);
                ViewSplitter.Visibility = Visibility.Collapsed;
                EditorPanel.Visibility = Visibility.Visible;
                PreviewPanel.Visibility = Visibility.Collapsed;
                EditorPanel.Margin = new Thickness(10, 0, 10, 0);
                _scrollSync.SetEnabled(false);
                break;
            case ViewMode.Viewer:
                EditorColumn.Width = new GridLength(0);
                SplitterColumn.Width = new GridLength(0);
                PreviewColumn.Width = new GridLength(1, GridUnitType.Star);
                ViewSplitter.Visibility = Visibility.Collapsed;
                EditorPanel.Visibility = Visibility.Collapsed;
                PreviewPanel.Visibility = Visibility.Visible;
                PreviewPanel.Margin = new Thickness(10, 0, 0, 0);
                _scrollSync.SetEnabled(false);
                break;
            case ViewMode.Split:
                EditorColumn.Width = new GridLength(1, GridUnitType.Star);
                SplitterColumn.Width = new GridLength(6);
                PreviewColumn.Width = new GridLength(1, GridUnitType.Star);
                ViewSplitter.Visibility = Visibility.Visible;
                EditorPanel.Visibility = Visibility.Visible;
                PreviewPanel.Visibility = Visibility.Visible;
                EditorPanel.Margin = new Thickness(10, 0, 0, 0);
                PreviewPanel.Margin = new Thickness(0);
                _scrollSync.SetEnabled(true);
                break;
        }
    }

    private void ShowFind(bool focusReplace = false)
    {
        if (_viewMode == ViewMode.Viewer)
            SetViewMode(ViewMode.Split);

        _findBarOpen = true;
        FindReplaceBar.Visibility = Visibility.Visible;

        if (!string.IsNullOrEmpty(Editor.SelectedText) && !Editor.SelectedText.Contains('\n'))
            FindTextBox.Text = Editor.SelectedText;

        RunFind();

        if (focusReplace)
        {
            ReplaceTextBox.Focus();
            ReplaceTextBox.SelectAll();
        }
        else
        {
            FindTextBox.Focus();
            FindTextBox.SelectAll();
        }
    }

    private void CloseFind()
    {
        _findBarOpen = false;
        FindReplaceBar.Visibility = Visibility.Collapsed;
        _findReplace?.Clear();
        FindCountText.Text = string.Empty;
        Editor.Focus();
    }

    private void RunFind()
    {
        if (_findReplace is null) return;

        var count = _findReplace.FindAll(
            FindTextBox.Text,
            MatchCaseToggle.IsChecked == true,
            RegexToggle.IsChecked == true);

        if (_findReplace.Error is not null)
        {
            FindCountText.Text = "Invalid regex";
            FindCountText.Foreground = (Brush)FindResource("DangerBrush");
            return;
        }

        FindCountText.Foreground = (Brush)FindResource("MutedForegroundBrush");
        FindCountText.Text = string.IsNullOrEmpty(FindTextBox.Text)
            ? string.Empty
            : count == 0 ? "No results" : $"{_findReplace.CurrentIndex + 1}/{count}";
    }

    private void UpdateFindCountLabel()
    {
        if (_findReplace is null || !_findReplace.HasMatches)
        {
            FindCountText.Text = string.IsNullOrEmpty(FindTextBox.Text) ? string.Empty : "No results";
            return;
        }

        FindCountText.Text = $"{_findReplace.CurrentIndex + 1}/{_findReplace.MatchCount}";
    }

    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = BuildOverflowMenu();
        menu.PlacementTarget = MoreButton;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private ContextMenu BuildOverflowMenu()
    {
        var menu = new ContextMenu();

        menu.Items.Add(CreateMenuItem("Open file…", (_, _) => OpenFile()));
        menu.Items.Add(CreateMenuItem("Open folder / workspace…", (_, _) => OpenFolder()));

        var recentFolders = new MenuItem { Header = "Recent workspaces" };
        var folders = _settings.RecentFolders.Where(Directory.Exists).ToList();
        if (folders.Count == 0)
        {
            recentFolders.Items.Add(new MenuItem { Header = "(empty)", IsEnabled = false });
        }
        else
        {
            foreach (var path in folders)
            {
                var item = new MenuItem { Header = path, Tag = path };
                item.Click += RecentFolder_Click;
                recentFolders.Items.Add(item);
            }

            recentFolders.Items.Add(new Separator());
            recentFolders.Items.Add(CreateMenuItem("Clear recent workspaces", (_, _) =>
            {
                _settings.RecentFolders.Clear();
                _settings.Save();
            }));
        }

        menu.Items.Add(recentFolders);
        menu.Items.Add(CreateMenuItem("Close workspace", (_, _) => CloseWorkspace()));
        menu.Items.Add(CreateMenuItem("Save as…", (_, _) => SaveAsFile()));

        var recent = new MenuItem { Header = "Open recent file" };
        var existing = _settings.RecentFiles.Where(File.Exists).ToList();
        if (existing.Count == 0)
        {
            recent.Items.Add(new MenuItem { Header = "(empty)", IsEnabled = false });
        }
        else
        {
            foreach (var path in existing)
            {
                var item = new MenuItem { Header = path, Tag = path };
                item.Click += RecentFile_Click;
                recent.Items.Add(item);
            }

            recent.Items.Add(new Separator());
            recent.Items.Add(CreateMenuItem("Clear recent files", (_, _) =>
            {
                _settings.RecentFiles.Clear();
                _settings.Save();
            }));
        }

        menu.Items.Add(recent);
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("Format document", (_, _) => FormatDocument()));
        menu.Items.Add(CreateMenuItem("Export to HTML…", (_, _) => ExportHtml()));
        menu.Items.Add(CreateMenuItem("Export to PDF…", (_, _) => ExportPdf()));
        menu.Items.Add(new Separator());

        var registered = FileAssociationService.IsRegistered();
        var assoc = CreateMenuItem(
            registered ? "Explorer integration (active)" : "Add Explorer integration",
            RegisterAssociation_Click);
        assoc.IsEnabled = !registered;
        menu.Items.Add(assoc);
        menu.Items.Add(CreateMenuItem("Remove Explorer integration", UnregisterAssociation_Click));
        menu.Items.Add(CreateMenuItem("Open log folder", OpenLogFolder_Click));
        menu.Items.Add(new Separator());

        var info = new MenuItem { Header = "Info" };
        info.Items.Add(CreateMenuItem("Keyboard shortcuts…", (_, _) => ShowKeyboardShortcuts()));
        menu.Items.Add(info);
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("Exit", (_, _) => Close()));

        return menu;
    }

    private void ShowKeyboardShortcuts()
    {
        var dialog = new KeyboardShortcutsWindow { Owner = this };
        dialog.ShowDialog();
    }

    private static MenuItem CreateMenuItem(string header, RoutedEventHandler handler)
    {
        var item = new MenuItem { Header = header };
        item.Click += handler;
        return item;
    }

    private void RecentFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string path })
            OpenFileFromPath(path);
    }

    private void RecentFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string path })
            OpenFolderFromPath(path);
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e) => OpenFolder();

    private void CloseWorkspace_Click(object sender, RoutedEventArgs e) => CloseWorkspace();

    private void RefreshFileTree()
    {
        _suppressTreeSelection = true;
        try
        {
            AppLogger.Debug($"RefreshFileTree folder={_folderPath ?? "(none)"}");
            FileTree.Items.Clear();

            if (string.IsNullOrEmpty(_folderPath) || !Directory.Exists(_folderPath))
            {
                SidebarEmptyText.Visibility = Visibility.Visible;
                FileTree.Visibility = Visibility.Collapsed;
                SidebarTitle.Text = "No folder open";
                SidebarTitle.ToolTip = "Use Folder to open a workspace";
                CloseWorkspaceButton.Visibility = Visibility.Collapsed;
                return;
            }

            SidebarEmptyText.Visibility = Visibility.Collapsed;
            FileTree.Visibility = Visibility.Visible;
            SidebarTitle.Text = new DirectoryInfo(_folderPath).Name;
            SidebarTitle.ToolTip = _folderPath;
            CloseWorkspaceButton.Visibility = Visibility.Visible;

            var root = BuildDirectoryNode(_folderPath, isRoot: true);
            if (root is not null)
                FileTree.Items.Add(root);

            AppLogger.Debug("RefreshFileTree complete");
        }
        catch (Exception ex)
        {
            AppLogger.Error("RefreshFileTree failed", ex);
        }
        finally
        {
            _suppressTreeSelection = false;
        }
    }

    private TreeViewItem? BuildDirectoryNode(string directory, bool isRoot)
    {
        var node = new TreeViewItem
        {
            Header = isRoot ? new DirectoryInfo(directory).Name : Path.GetFileName(directory),
            Tag = directory,
            IsExpanded = isRoot
        };

        try
        {
            foreach (var subDir in Directory.EnumerateDirectories(directory)
                         .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
            {
                if (HasMarkdownFiles(subDir))
                {
                    var child = BuildDirectoryNode(subDir, isRoot: false);
                    if (child is not null)
                        node.Items.Add(child);
                }
            }

            foreach (var file in EnumerateMarkdownFiles(directory))
            {
                node.Items.Add(new TreeViewItem
                {
                    Header = Path.GetFileName(file),
                    Tag = file,
                    IsSelected = string.Equals(file, _currentFilePath, StringComparison.OrdinalIgnoreCase)
                });
            }
        }
        catch
        {
            // Skip inaccessible directories
        }

        return node.Items.Count > 0 || isRoot ? node : null;
    }

    private static bool HasMarkdownFiles(string directory)
    {
        try
        {
            if (EnumerateMarkdownFiles(directory).Any())
                return true;

            return Directory.EnumerateDirectories(directory).Any(HasMarkdownFiles);
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<string> EnumerateMarkdownFiles(string directory)
    {
        return Directory.EnumerateFiles(directory, "*.md")
            .Concat(Directory.EnumerateFiles(directory, "*.markdown"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase);
    }

    private void FileTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_suppressTreeSelection) return;
        if (FileTree.SelectedItem is not TreeViewItem { Tag: string path }) return;
        if (!File.Exists(path)) return;
        if (string.Equals(path, _currentFilePath, StringComparison.OrdinalIgnoreCase)) return;

        if (!ConfirmDiscardChanges())
        {
            RefreshFileTree();
            return;
        }

        LoadFile(path, confirmDiscard: false, adoptWorkspaceIfEmpty: false);
    }

    private void FileTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FileTree.SelectedItem is TreeViewItem { Tag: string path } && File.Exists(path))
            LoadFile(path, confirmDiscard: true, adoptWorkspaceIfEmpty: false);
    }

    private bool ConfirmDiscardChanges()
    {
        if (!_isDirty) return true;

        var result = MessageBox.Show(
            "You have unsaved changes. Do you want to save before continuing?",
            "Unsaved Changes",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);

        return result switch
        {
            MessageBoxResult.Yes => SaveFile(),
            MessageBoxResult.No => true,
            _ => false
        };
    }

    private void NewFile()
    {
        if (!ConfirmDiscardChanges()) return;

        _currentFilePath = null;
        _savedContent = string.Empty;
        Editor.Text = string.Empty;
        _isDirty = false;
        UpdateTitle();
        StatusText.Text = "New document";
        RefreshFileTree();
        _ = UpdatePreviewAsync();
    }

    private void OpenFile()
    {
        if (!ConfirmDiscardChanges()) return;

        var dialog = new OpenFileDialog
        {
            Filter = "Markdown files (*.md;*.markdown)|*.md;*.markdown|All files (*.*)|*.*",
            Title = "Open Markdown File"
        };

        if (!string.IsNullOrEmpty(_folderPath) && Directory.Exists(_folderPath))
            dialog.InitialDirectory = _folderPath;

        if (dialog.ShowDialog() != true) return;
        LoadFile(dialog.FileName, confirmDiscard: false, adoptWorkspaceIfEmpty: true);
    }

    private void OpenFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Open folder as workspace"
        };

        if (!string.IsNullOrEmpty(_folderPath) && Directory.Exists(_folderPath))
            dialog.InitialDirectory = _folderPath;
        else if (!string.IsNullOrEmpty(_settings.LastFolder) && Directory.Exists(_settings.LastFolder))
            dialog.InitialDirectory = _settings.LastFolder;

        if (dialog.ShowDialog() != true) return;
        OpenFolderPath(dialog.FolderName, selectFirstFile: true);
    }

    private void OpenFolderPath(string path, bool selectFirstFile)
    {
        AppLogger.Info($"OpenFolderPath path={path}, selectFirstFile={selectFirstFile}");
        SetWorkspace(Path.GetFullPath(path));

        if (!selectFirstFile) return;

        var firstFile = FindFirstMarkdownFile(_folderPath!);
        if (firstFile is null)
        {
            AppLogger.Info("No markdown files found in folder");
            StatusText.Text = $"Workspace: {_folderPath} (no .md files yet)";
            return;
        }

        AppLogger.Info($"Selecting first markdown file: {firstFile}");
        if (!ConfirmDiscardChanges()) return;
        LoadFile(firstFile, confirmDiscard: false, adoptWorkspaceIfEmpty: false);
    }

    private void SetWorkspace(string path)
    {
        _folderPath = Path.GetFullPath(path);
        _settings.AddRecentFolder(_folderPath);
        _settings.Save();
        SetSidebarVisible(true);
        RefreshFileTree();
        StatusText.Text = $"Workspace: {_folderPath}";
    }

    private void CloseWorkspace()
    {
        _folderPath = null;
        RefreshFileTree();
        StatusText.Text = "Workspace closed";
        AppLogger.Info("Workspace closed");
    }

    private static string? FindFirstMarkdownFile(string directory)
    {
        try
        {
            var direct = EnumerateMarkdownFiles(directory).FirstOrDefault();
            if (direct is not null) return direct;

            foreach (var subDir in Directory.EnumerateDirectories(directory)
                         .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
            {
                var nested = FindFirstMarkdownFile(subDir);
                if (nested is not null) return nested;
            }
        }
        catch
        {
            // Ignore inaccessible paths
        }

        return null;
    }

    private void LoadFile(string path, bool confirmDiscard = true, bool adoptWorkspaceIfEmpty = false)
    {
        AppLogger.Info($"LoadFile path={path}, confirmDiscard={confirmDiscard}, adoptWorkspaceIfEmpty={adoptWorkspaceIfEmpty}");
        if (confirmDiscard && !ConfirmDiscardChanges()) return;

        try
        {
            var content = File.ReadAllText(path, Encoding.UTF8);
            _currentFilePath = Path.GetFullPath(path);

            // Opening a file must not steal an existing workspace.
            if (adoptWorkspaceIfEmpty && string.IsNullOrEmpty(_folderPath))
            {
                var parent = Path.GetDirectoryName(_currentFilePath);
                if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                    SetWorkspace(parent);
            }

            _savedContent = content;
            Editor.Text = content;
            _isDirty = false;

            _settings.AddRecentFile(_currentFilePath);
            _settings.Save();
            RefreshFileTree();
            UpdateTitle();
            StatusText.Text = _currentFilePath;
            AppLogger.Info($"LoadFile success ({content.Length} chars)");
            _ = UpdatePreviewAsync();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"LoadFile failed: {path}", ex);
            MessageBox.Show($"Could not open file:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool SaveFile()
    {
        if (_currentFilePath is null) return SaveAsFile();

        try
        {
            File.WriteAllText(_currentFilePath, Editor.Text, Encoding.UTF8);
            _savedContent = Editor.Text;
            _isDirty = false;
            UpdateTitle();
            StatusText.Text = $"Saved: {_currentFilePath}";
            RefreshFileTree();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not save file:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private bool SaveAsFile()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Markdown files (*.md)|*.md|All files (*.*)|*.*",
            Title = "Save Markdown File",
            DefaultExt = ".md",
            FileName = _currentFilePath is not null ? Path.GetFileName(_currentFilePath) : "Untitled.md"
        };

        if (!string.IsNullOrEmpty(_folderPath) && Directory.Exists(_folderPath))
            dialog.InitialDirectory = _folderPath;

        if (dialog.ShowDialog() != true) return false;

        _currentFilePath = dialog.FileName;
        var saved = SaveFile();
        if (saved)
        {
            _settings.AddRecentFile(_currentFilePath);
            // Don't replace an existing workspace when saving elsewhere.
            if (string.IsNullOrEmpty(_folderPath))
            {
                var parent = Path.GetDirectoryName(_currentFilePath);
                if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                    SetWorkspace(parent);
            }
            else
            {
                _settings.Save();
                RefreshFileTree();
            }
        }

        return saved;
    }

    private async void ExportHtml()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "HTML files (*.html)|*.html|All files (*.*)|*.*",
            Title = "Export to HTML",
            DefaultExt = ".html",
            FileName = GetDefaultExportName(".html")
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            var title = Path.GetFileNameWithoutExtension(dialog.FileName);
            var html = MarkdownService.ToExportHtml(Editor.Text, title, _settings.IsDarkTheme);
            await File.WriteAllTextAsync(dialog.FileName, html, Encoding.UTF8);
            StatusText.Text = $"Exported: {dialog.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not export HTML:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ExportPdf()
    {
        if (!_webViewReady)
        {
            MessageBox.Show("Preview is not ready. PDF export requires the WebView2 runtime.",
                "Export Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "PDF files (*.pdf)|*.pdf",
            Title = "Export to PDF",
            DefaultExt = ".pdf",
            FileName = GetDefaultExportName(".pdf")
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            var title = Path.GetFileNameWithoutExtension(dialog.FileName);
            var html = MarkdownService.ToExportHtml(Editor.Text, title, darkTheme: false);
            _previewShellReady = false;
            _scrollSync.BeginReload();
            PreviewWebView.NavigateToString(html);

            await Task.Delay(500);

            await PreviewWebView.CoreWebView2.PrintToPdfAsync(dialog.FileName);
            StatusText.Text = $"Exported: {dialog.FileName}";

            if (_viewMode != ViewMode.Editor)
                await UpdatePreviewAsync(forceNavigate: true);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not export PDF:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private string GetDefaultExportName(string extension)
    {
        if (_currentFilePath is not null)
            return Path.GetFileNameWithoutExtension(_currentFilePath) + extension;
        return "Untitled" + extension;
    }

    private void NewFile_Click(object sender, RoutedEventArgs e) => NewFile();
    private void OpenFile_Click(object sender, RoutedEventArgs e) => OpenFile();
    private void SaveFile_Click(object sender, RoutedEventArgs e) => SaveFile();
    private void Find_Click(object sender, RoutedEventArgs e) => ShowFind();
    private void ViewEditor_Click(object sender, RoutedEventArgs e) => SetViewMode(ViewMode.Editor);
    private void ViewViewer_Click(object sender, RoutedEventArgs e) => SetViewMode(ViewMode.Viewer);
    private void ViewSplit_Click(object sender, RoutedEventArgs e) => SetViewMode(ViewMode.Split);

    private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
    {
        var visible = ToolbarSidebarButton.IsChecked == true;
        SetSidebarVisible(visible);
        _settings.Save();
    }

    private void ToggleDarkTheme_Click(object sender, RoutedEventArgs e)
    {
        var dark = ToolbarDarkButton.IsChecked == true;
        ApplyTheme(dark);
        _settings.Save();
    }

    private void ToggleFocusMode_Click(object sender, RoutedEventArgs e)
    {
        var enabled = ToolbarFocusButton.IsChecked == true;
        if (_focusMode is not null)
            _focusMode.IsEnabled = enabled;
        _settings.IsFocusMode = enabled;
        _settings.Save();
        StatusText.Text = enabled ? "Focus mode on" : "Focus mode off";
    }

    private void ToggleTypewriterMode_Click(object sender, RoutedEventArgs e)
    {
        var enabled = ToolbarTypewriterButton.IsChecked == true;
        if (_typewriterMode is not null)
            _typewriterMode.IsEnabled = enabled;
        _settings.IsTypewriterMode = enabled;
        _settings.Save();
        StatusText.Text = enabled ? "Typewriter mode on" : "Typewriter mode off";
    }

    private void FormatDocument()
    {
        var caret = Editor.CaretOffset;
        var formatted = MarkdownFormatter.Format(Editor.Text);
        if (formatted == Editor.Text)
        {
            StatusText.Text = "Already formatted";
            return;
        }

        Editor.Document.Text = formatted;
        Editor.CaretOffset = Math.Min(caret, Editor.Document.TextLength);
        StatusText.Text = "Document formatted";
    }

    private void Editor_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (ImageDropService.CanAccept(e))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void Editor_Drop(object sender, DragEventArgs e)
    {
        if (!ImageDropService.CanAccept(e)) return;
        e.Handled = true;
        var count = ImageDropService.InsertDroppedImages(Editor, _currentFilePath, e);
        StatusText.Text = count == 0
            ? "No images inserted"
            : $"Inserted {count} image(s) into ./assets";
    }

    private void FindTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _findTimer.Stop();
        _findTimer.Start();
    }

    private void FindOptions_Changed(object sender, RoutedEventArgs e) => RunFind();

    private void FindTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                FindPrevious_Click(sender, e);
            else
                FindNext_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CloseFind();
            e.Handled = true;
        }
    }

    private void ReplaceTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Replace_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CloseFind();
            e.Handled = true;
        }
    }

    private void FindNext_Click(object sender, RoutedEventArgs e)
    {
        _findReplace?.FindNext();
        UpdateFindCountLabel();
    }

    private void FindPrevious_Click(object sender, RoutedEventArgs e)
    {
        _findReplace?.FindPrevious();
        UpdateFindCountLabel();
    }

    private void Replace_Click(object sender, RoutedEventArgs e)
    {
        if (_findReplace is null) return;
        if (!_findReplace.HasMatches)
            RunFind();
        _findReplace.ReplaceCurrent(ReplaceTextBox.Text);
        UpdateFindCountLabel();
    }

    private void ReplaceAll_Click(object sender, RoutedEventArgs e)
    {
        if (_findReplace is null) return;
        if (!_findReplace.HasMatches)
            RunFind();

        var count = _findReplace.ReplaceAll(ReplaceTextBox.Text);
        StatusText.Text = count == 0 ? "Nothing to replace" : $"Replaced {count} occurrence(s)";
        UpdateFindCountLabel();
    }

    private void CloseFind_Click(object sender, RoutedEventArgs e) => CloseFind();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _findBarOpen)
        {
            CloseFind();
            e.Handled = true;
            return;
        }

        // Don't steal shortcuts while typing in find/replace boxes
        if (Keyboard.FocusedElement is TextBox)
            return;

        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.B:
                    FormattingShortcuts.Bold(Editor);
                    e.Handled = true;
                    break;
                case Key.I:
                    FormattingShortcuts.Italic(Editor);
                    e.Handled = true;
                    break;
                case Key.K:
                    FormattingShortcuts.Link(Editor);
                    e.Handled = true;
                    break;
                case Key.D1:
                    FormattingShortcuts.Heading(Editor, 1);
                    e.Handled = true;
                    break;
                case Key.D2:
                    FormattingShortcuts.Heading(Editor, 2);
                    e.Handled = true;
                    break;
                case Key.D3:
                    FormattingShortcuts.Heading(Editor, 3);
                    e.Handled = true;
                    break;
            }
        }
        else if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            switch (e.Key)
            {
                case Key.C:
                    FormattingShortcuts.InlineCode(Editor);
                    e.Handled = true;
                    break;
                case Key.X:
                    FormattingShortcuts.Strikethrough(Editor);
                    e.Handled = true;
                    break;
            }
        }
    }

    private void RegisterAssociation_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            AppLogger.Info("Registering Explorer integration (.md + folder context menu)");
            FileAssociationService.Register();
            StatusText.Text = "Explorer integration registered (current user)";
            MessageBox.Show(
                "MdViewer is now integrated with Windows Explorer for your user account:\n\n" +
                "• Open .md / .markdown files with MdViewer\n" +
                "• Right-click a folder → \"Open with MdViewer\"\n" +
                "• Right-click empty space inside a folder → \"Open with MdViewer\"\n\n" +
                "Tip: use a published/release build if you want Explorer shortcuts to keep working after rebuilds.",
                "Explorer Integration",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AppLogger.Error("Register Explorer integration failed", ex);
            MessageBox.Show($"Could not register Explorer integration:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UnregisterAssociation_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            AppLogger.Info("Unregistering Explorer integration");
            FileAssociationService.Unregister();
            StatusText.Text = "Explorer integration removed";
            MessageBox.Show(
                "MdViewer Explorer integration was removed for your user account.",
                "Explorer Integration",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AppLogger.Error("Unregister Explorer integration failed", ex);
            MessageBox.Show($"Could not remove Explorer integration:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenLogFolder_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Info("Open log folder requested");
        AppLogger.OpenLogFolder();
        StatusText.Text = $"Log: {AppLogger.CurrentLogPath}";
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        AppLogger.Info("Window_Closing");
        if (!ConfirmDiscardChanges())
        {
            AppLogger.Info("Window close cancelled (unsaved changes)");
            e.Cancel = true;
            return;
        }

        _settings.Save();
        AppLogger.Info("Window closing confirmed");
    }
}
