using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Scribe.Controls;
using Scribe.Ink;
using Scribe.Models;
using Scribe.Services;
using Scribe.Storage;
using Scribe.Views;

// System.Windows.Shapes is imported for the pen-tray swatches, and it also
// defines a Path type.
using Path = System.IO.Path;

namespace Scribe;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly ObservableCollection<NotebookItem> _notebooks = new();
    private readonly List<InkTool> _tray = InkTool.DefaultTray().Select(t => t.Clone()).ToList();

    private Workspace _workspace = null!;
    private SectionItem? _section;
    private PageItem? _page;

    private InkTool _pen;
    private InkTool _highlighter;

    private readonly DispatcherTimer _autosaveTimer;
    private readonly DispatcherTimer _searchTimer;

    private bool _dirty;
    private bool _suppressTitleSync;
    private string _titleAtLoad = "";

    public MainWindow()
    {
        InitializeComponent();

        _pen = _tray.First(t => t.Kind == ToolKind.Pen);
        _highlighter = _tray.First(t => t.Kind == ToolKind.Highlighter);

        // Debounced rather than save-on-every-stroke: writing a dense page is
        // megabytes of JSON, and doing that on each pen lift would stutter.
        _autosaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _autosaveTimer.Tick += (_, _) =>
        {
            _autosaveTimer.Stop();
            SaveCurrentPage();
        };

        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            RunSearch();
        };

        Tree.ItemsSource = _notebooks;
        PageView.ContentChanged += (_, _) => OnContentChanged();
        PageView.Undo.Changed += (_, _) => UpdateHistoryButtons();

        Loaded += OnLoaded;
        Closing += OnClosing;
        PreviewKeyDown += OnPreviewKeyDown;
        StateChanged += (_, _) => UpdateWindowState();
    }

    // ------------------------------------------------------------ window chrome

    private void Minimise_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void MaxRestore_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();

    private void UpdateWindowState()
    {
        bool maximised = WindowState == WindowState.Maximized;

        // A maximised WindowChrome window overhangs the work area by the resize
        // border, which would push the right edge and the caption buttons off
        // screen. Inset the content by the same amount to compensate.
        RootBorder.Margin = maximised
            ? new Thickness(SystemParameters.WindowResizeBorderThickness.Left + 3)
            : new Thickness(0);

        MaxGlyph.Data = (Geometry)FindResource(maximised ? "IconWinRestore" : "IconWinMaximise");
        MaxButton.ToolTip = maximised ? "Restore" : "Maximise";
    }

    // -------------------------------------------------------------- lifecycle

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _workspace = new Workspace(_settings.WorkspaceRoot ?? Workspace.DefaultRoot);

        ThemeService.Apply(_settings.DarkMode);
        UpdateThemeButton();

        BuildPenTray();
        SelectTool(ToolKind.Pen);
        ReloadTree();

        if (_notebooks.Count == 0)
        {
            CreateStarterNotebook();
            ReloadTree();
        }

        RestoreLastPage();
        UpdateEmptyState();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        CommitTitleRename();
        SaveCurrentPage();

        _settings.LastPageFile = _page?.Path;
        _settings.Save();
    }

    private void CreateStarterNotebook()
    {
        var nbDir = _workspace.CreateNotebook("My Notebook");
        var secDir = _workspace.CreateSection(nbDir, "Quick Notes");
        var pageFile = _workspace.CreatePage(secDir, "Welcome to Scribe");

        var doc = _workspace.ReadPage(pageFile);
        doc.TextBoxes.Add(new TextBoxDto
        {
            X = 90,
            Y = 96,
            Width = 620,
            FontSize = 15,
            Text =
                "Welcome to Scribe.\n\n" +
                "• Pick up the Pen and write — pressure and tilt come through, and a resting " +
                "palm will not leave marks.\n" +
                "• One finger pans, two fingers pinch to zoom. Only the pen draws.\n" +
                "• Flip the pen over to erase with its eraser end.\n" +
                "• Click Text, then click anywhere, to type.\n" +
                "• Ctrl+Z undoes, Ctrl+N makes a new page.\n\n" +
                "Everything lives in plain folders and JSON files on your disk — open the " +
                "notebook folder in File Explorer any time and it is all readable.\n\n" +
                "To bring your existing notes across, use Import from OneNote in the top right.",
        });

        _workspace.WritePage(pageFile, doc);
    }

    private void RestoreLastPage()
    {
        var last = _settings.LastPageFile;

        if (!string.IsNullOrEmpty(last) && File.Exists(last))
        {
            foreach (var nb in _notebooks)
                foreach (var sec in nb.Sections)
                    if (last.StartsWith(sec.Path + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    {
                        SelectSection(sec);
                        var match = sec.Pages.FirstOrDefault(p =>
                            string.Equals(p.Path, last, StringComparison.OrdinalIgnoreCase));
                        if (match is not null)
                        {
                            PageList.SelectedItem = match;
                            return;
                        }
                    }
        }

        // Fall back to the first section that has anything in it.
        var first = _notebooks.SelectMany(n => n.Sections).FirstOrDefault();
        if (first is not null)
        {
            SelectSection(first);
            if (PageList.Items.Count > 0) PageList.SelectedIndex = 0;
        }
    }

    // ------------------------------------------------------------------- tree

    private void ReloadTree()
    {
        var selectedSectionPath = _section?.Path;

        _notebooks.Clear();

        foreach (var nbDir in _workspace.ListNotebookDirs())
        {
            var nbDoc = _workspace.ReadNotebook(nbDir);
            var nb = new NotebookItem
            {
                Name = nbDoc.Name,
                Path = nbDir,
                Color = nbDoc.Color,
            };

            foreach (var secDir in _workspace.ListSectionDirs(nbDir))
            {
                var secDoc = _workspace.ReadSection(secDir);
                nb.Sections.Add(new SectionItem
                {
                    Name = secDoc.Name,
                    Path = secDir,
                    NotebookPath = nbDir,
                    Color = secDoc.Color,
                });
            }

            _notebooks.Add(nb);
        }

        if (selectedSectionPath is not null)
        {
            var again = _notebooks.SelectMany(n => n.Sections)
                .FirstOrDefault(s => string.Equals(s.Path, selectedSectionPath, StringComparison.OrdinalIgnoreCase));
            if (again is not null) SelectSection(again);
        }
    }

    private void SelectSection(SectionItem section)
    {
        _section = section;
        section.Pages.Clear();

        foreach (var file in _workspace.ListPageFiles(section.Path))
        {
            var doc = _workspace.ReadPage(file);
            section.Pages.Add(new PageItem
            {
                Name = doc.Title,
                Path = file,
                SectionPath = section.Path,
                Modified = doc.Modified,
            });
        }

        SearchBox.Text = "";
        PageList.ItemsSource = section.Pages;
    }

    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is SectionItem section)
        {
            CommitTitleRename();
            SaveCurrentPage();
            SelectSection(section);

            if (PageList.Items.Count > 0) PageList.SelectedIndex = 0;
            else ClearOpenPage();
        }
    }

    // ------------------------------------------------------------------ pages

    private void PageList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PageList.SelectedItem is not PageItem item)
            return;

        if (_page is not null && string.Equals(_page.Path, item.Path, StringComparison.OrdinalIgnoreCase))
            return;

        CommitTitleRename();
        SaveCurrentPage();
        OpenPage(item);
    }

    private void OpenPage(PageItem item)
    {
        if (!File.Exists(item.Path))
        {
            // The file vanished underneath us — most likely a sync client.
            ReloadTree();
            return;
        }

        _page = item;

        var doc = _workspace.ReadPage(item.Path);
        PageView.LoadPage(item.SectionPath, item.Path, doc);

        _suppressTitleSync = true;
        TitleBox.Text = doc.Title;
        TitleBox.IsEnabled = true;
        _suppressTitleSync = false;
        _titleAtLoad = doc.Title;

        _dirty = false;
        SyncPaperCombo(doc.Background.Kind);
        UpdateHistoryButtons();
        UpdateEmptyState();
        SetStatus("");
    }

    private void ClearOpenPage()
    {
        _page = null;
        PageView.Unload();

        _suppressTitleSync = true;
        TitleBox.Text = "";
        TitleBox.IsEnabled = false;
        _suppressTitleSync = false;

        _dirty = false;
        UpdateEmptyState();
    }

    private void UpdateEmptyState() =>
        EmptyState.Visibility = _page is null ? Visibility.Visible : Visibility.Collapsed;

    private void OnContentChanged()
    {
        _dirty = true;
        SetStatus("Unsaved changes");

        _autosaveTimer.Stop();
        _autosaveTimer.Start();
    }

    private void SaveCurrentPage()
    {
        if (_page is null || !_dirty) return;

        try
        {
            var doc = PageView.ToDoc();
            doc.Title = string.IsNullOrWhiteSpace(TitleBox.Text) ? "Untitled page" : TitleBox.Text.Trim();

            _workspace.WritePage(_page.Path, doc);

            _page.Name = doc.Title;
            _page.Modified = doc.Modified;
            _dirty = false;

            SetStatus($"Saved {DateTime.Now:HH:mm}");
        }
        catch (Exception ex)
        {
            SetStatus("Could not save");
            MessageBox.Show(this,
                $"Scribe could not save this page.\n\n{ex.Message}",
                "Save failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Renames the page file to match its title. Deferred until the page is
    /// closed or switched away from, rather than run per keystroke, which would
    /// otherwise create a file for every intermediate spelling of the title.
    /// </summary>
    private void CommitTitleRename()
    {
        if (_page is null) return;

        var title = TitleBox.Text.Trim();
        if (string.IsNullOrEmpty(title) || title == _titleAtLoad) return;

        try
        {
            var newPath = _workspace.RenamePageFile(_page.SectionPath, _page.Path, title);
            _page.Path = newPath;
            _page.Name = title;
            _titleAtLoad = title;
        }
        catch (Exception ex)
        {
            SetStatus($"Rename failed: {ex.Message}");
        }
    }

    private void Title_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressTitleSync || _page is null) return;
        OnContentChanged();
    }

    private void NewPage_Click(object sender, RoutedEventArgs e) => CreatePage();

    private void NewPage_Executed(object sender, ExecutedRoutedEventArgs e) => CreatePage();

    private void CreatePage()
    {
        if (_section is null)
        {
            MessageBox.Show(this, "Pick a section first.", "New page",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        CommitTitleRename();
        SaveCurrentPage();

        var file = _workspace.CreatePage(_section.Path, "Untitled page");
        SelectSection(_section);

        var item = _section.Pages.FirstOrDefault(p =>
            string.Equals(p.Path, file, StringComparison.OrdinalIgnoreCase));

        if (item is not null)
        {
            PageList.SelectedItem = item;
            TitleBox.Focus();
            TitleBox.SelectAll();
        }
    }

    private void RenamePage_Click(object sender, RoutedEventArgs e)
    {
        if (PageList.SelectedItem is not PageItem item) return;

        var name = PromptDialog.Ask(this, "Rename page", "Page title", item.Name);
        if (name is null) return;

        var doc = _workspace.ReadPage(item.Path);
        doc.Title = name;
        _workspace.WritePage(item.Path, doc);

        item.Path = _workspace.RenamePageFile(item.SectionPath, item.Path, name);
        item.Name = name;

        if (_page == item)
        {
            _suppressTitleSync = true;
            TitleBox.Text = name;
            _suppressTitleSync = false;
            _titleAtLoad = name;
        }
    }

    private void DeletePage_Click(object sender, RoutedEventArgs e)
    {
        if (PageList.SelectedItem is not PageItem item) return;

        var answer = MessageBox.Show(this,
            $"Delete “{item.Name}”?\n\nThe file is removed from disk.",
            "Delete page", MessageBoxButton.OKCancel, MessageBoxImage.Warning);

        if (answer != MessageBoxResult.OK) return;

        bool wasOpen = _page == item;
        if (wasOpen) ClearOpenPage();

        _workspace.DeletePage(item.SectionPath, item.Path);
        if (_section is not null) SelectSection(_section);
    }

    // ------------------------------------------------- notebooks and sections

    private void NewNotebook_Click(object sender, RoutedEventArgs e)
    {
        var name = PromptDialog.Ask(this, "New notebook", "Notebook name", "My Notebook");
        if (name is null) return;

        var dir = _workspace.CreateNotebook(name);
        _workspace.CreateSection(dir, "New Section");
        ReloadTree();
    }

    private void NewSection_Click(object sender, RoutedEventArgs e)
    {
        var notebookPath = Tree.SelectedItem switch
        {
            NotebookItem nb => nb.Path,
            SectionItem sec => sec.NotebookPath,
            _ => null,
        };

        if (notebookPath is null)
        {
            MessageBox.Show(this, "Pick a notebook first.", "New section",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var name = PromptDialog.Ask(this, "New section", "Section name", "New Section");
        if (name is null) return;

        _workspace.CreateSection(notebookPath, name);
        ReloadTree();
    }

    private void RenameItem_Click(object sender, RoutedEventArgs e)
    {
        switch (Tree.SelectedItem)
        {
            case NotebookItem nb:
            {
                var name = PromptDialog.Ask(this, "Rename notebook", "Notebook name", nb.Name);
                if (name is null) return;

                var doc = _workspace.ReadNotebook(nb.Path);
                doc.Name = name;
                _workspace.WriteNotebook(nb.Path, doc);
                ReloadTree();
                break;
            }

            case SectionItem sec:
            {
                var name = PromptDialog.Ask(this, "Rename section", "Section name", sec.Name);
                if (name is null) return;

                var doc = _workspace.ReadSection(sec.Path);
                doc.Name = name;
                _workspace.WriteSection(sec.Path, doc);
                ReloadTree();
                break;
            }
        }
    }

    private void DeleteItem_Click(object sender, RoutedEventArgs e)
    {
        if (Tree.SelectedItem is not TreeItem item) return;

        string what = item is NotebookItem ? "notebook" : "section";

        var answer = MessageBox.Show(this,
            $"Delete the {what} “{item.Name}” and everything inside it?\n\n" +
            $"The folder is removed from disk:\n{item.Path}",
            $"Delete {what}", MessageBoxButton.OKCancel, MessageBoxImage.Warning);

        if (answer != MessageBoxResult.OK) return;

        // The open page may be inside what is about to go.
        if (_page is not null &&
            _page.Path.StartsWith(item.Path, StringComparison.OrdinalIgnoreCase))
        {
            ClearOpenPage();
        }

        try
        {
            Directory.Delete(item.Path, recursive: true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Delete failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        _section = null;
        PageList.ItemsSource = null;
        ReloadTree();
    }

    private void Reveal_Click(object sender, RoutedEventArgs e)
    {
        var path = (Tree.SelectedItem as TreeItem)?.Path ?? _workspace.Root;

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch
        {
            // Explorer being unavailable is not worth an error dialog.
        }
    }

    // ------------------------------------------------------------------ tools

    private void BuildPenTray()
    {
        PenTray.Items.Clear();

        foreach (var tool in _tray)
        {
            // A highlighter reads as a wider, softer disc so the tray shows the
            // difference between pens and markers at a glance.
            bool marker = tool.Kind == ToolKind.Highlighter;

            var swatch = new Ellipse
            {
                Width = marker ? 17 : 14,
                Height = marker ? 17 : 14,
                // Show the colour the pen will actually paint, so the swatch
                // does not promise black and deliver white on a dark page.
                Fill = new SolidColorBrush(InkTheme.Adapt(tool.Color)),
                Stroke = (Brush)FindResource("SubtleBorderBrush"),
                StrokeThickness = 1,
            };

            var button = new ToggleButton
            {
                Content = swatch,
                Tag = tool,
                Style = (Style)FindResource("SwatchButtonStyle"),
                ToolTip = $"{tool.Name} — {tool.Width:0.#} px",
            };

            button.Click += PenTray_Click;
            PenTray.Items.Add(button);
        }
    }

    private void PenTray_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton button || button.Tag is not InkTool tool) return;

        if (tool.Kind == ToolKind.Highlighter) _highlighter = tool;
        else _pen = tool;

        SelectTool(tool.Kind);
    }

    private void Tool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton button || button.Tag is not string tag) return;

        if (!Enum.TryParse<ToolKind>(tag, out var kind)) return;

        // A second press of the eraser switches to rubbing out part of a stroke
        // instead of removing the whole thing, which is the distinction OneNote
        // buries in a submenu.
        if (kind == ToolKind.StrokeEraser && PageView.ActiveTool.Kind == ToolKind.StrokeEraser)
            kind = ToolKind.PointEraser;

        SelectTool(kind);
    }

    private void SelectTool(ToolKind kind)
    {
        InkTool tool = kind switch
        {
            ToolKind.Pen => _pen,
            ToolKind.Highlighter => _highlighter,
            ToolKind.StrokeEraser => new InkTool { Kind = ToolKind.StrokeEraser, Name = "Eraser" },
            ToolKind.PointEraser => new InkTool { Kind = ToolKind.PointEraser, Name = "Partial eraser", Width = 18 },
            _ => new InkTool { Kind = kind, Name = kind.ToString() },
        };

        PageView.ActiveTool = tool;

        PenToolButton.IsChecked = kind == ToolKind.Pen;
        MarkerToolButton.IsChecked = kind == ToolKind.Highlighter;
        EraserToolButton.IsChecked = kind is ToolKind.StrokeEraser or ToolKind.PointEraser;
        SelectToolButton.IsChecked = kind == ToolKind.Select;
        TextToolButton.IsChecked = kind == ToolKind.Text;

        EraserToolButton.ToolTip = kind == ToolKind.PointEraser
            ? "Partial eraser — rubs out part of a stroke. Press again for whole strokes."
            : "Eraser  (E) — press again for a partial eraser";

        foreach (var item in PenTray.Items.OfType<ToggleButton>())
            item.IsChecked = ReferenceEquals(item.Tag, tool);

        if (tool.IsInkTool)
        {
            WidthSlider.IsEnabled = true;
            WidthSlider.Value = Math.Clamp(tool.Width, WidthSlider.Minimum, WidthSlider.Maximum);
        }
        else
        {
            WidthSlider.IsEnabled = tool.Kind == ToolKind.PointEraser;
        }
    }

    private void WidthSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded) return;

        var tool = PageView.ActiveTool;
        if (!tool.IsInkTool && tool.Kind != ToolKind.PointEraser) return;

        tool.Width = e.NewValue;

        // Push the change through, since the canvas caches the attributes.
        PageView.ActiveTool = tool;

        foreach (var item in PenTray.Items.OfType<ToggleButton>())
            if (ReferenceEquals(item.Tag, tool))
                item.ToolTip = $"{tool.Name} — {tool.Width:0.#} px";
    }

    private void Paper_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _page is null) return;
        if (PaperCombo.SelectedItem is not ComboBoxItem item || item.Tag is not string kind) return;

        var doc = PageView.ToDoc();
        doc.Background.Kind = kind;

        PageView.LoadPage(_page.SectionPath, _page.Path, doc);
        OnContentChanged();
    }

    private void SyncPaperCombo(string kind)
    {
        foreach (var item in PaperCombo.Items.OfType<ComboBoxItem>())
            item.IsSelected = string.Equals((string?)item.Tag, kind, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------- history

    private void Undo_Click(object sender, RoutedEventArgs e) => DoUndo();

    private void Redo_Click(object sender, RoutedEventArgs e) => DoRedo();

    private void Undo_Executed(object sender, ExecutedRoutedEventArgs e) => DoUndo();

    private void Redo_Executed(object sender, ExecutedRoutedEventArgs e) => DoRedo();

    private void DoUndo()
    {
        PageView.Undo.Undo();
        OnContentChanged();
    }

    private void DoRedo()
    {
        PageView.Undo.Redo();
        OnContentChanged();
    }

    private void UpdateHistoryButtons()
    {
        UndoButton.IsEnabled = PageView.Undo.CanUndo;
        RedoButton.IsEnabled = PageView.Undo.CanRedo;
    }

    private void Save_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        CommitTitleRename();
        SaveCurrentPage();
    }

    // ----------------------------------------------------------------- search

    private void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void Find_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void RunSearch()
    {
        var query = SearchBox.Text.Trim();

        if (query.Length == 0)
        {
            if (_section is not null) PageList.ItemsSource = _section.Pages;
            return;
        }

        // Searches every notebook, not just the open section: when you cannot
        // remember where you wrote something, scoping to the current section is
        // exactly the wrong default.
        var results = new List<PageItem>();

        foreach (var nb in _notebooks)
        {
            foreach (var sec in nb.Sections)
            {
                foreach (var file in _workspace.ListPageFiles(sec.Path))
                {
                    if (!PageMatches(file, query, out var title, out var modified)) continue;

                    results.Add(new PageItem
                    {
                        Name = title,
                        Path = file,
                        SectionPath = sec.Path,
                        Modified = modified,
                    });
                }
            }
        }

        PageList.ItemsSource = results;
        SetStatus(results.Count == 0 ? "No matches" : $"{results.Count} match(es)");
    }

    /// <summary>
    /// Matches a page without fully deserialising its ink. Stroke arrays
    /// dominate the file, and parsing them to search typed text would make
    /// every keystroke in the search box read megabytes per page.
    /// </summary>
    private bool PageMatches(string file, string query, out string title, out DateTimeOffset modified)
    {
        title = Path.GetFileNameWithoutExtension(file);
        modified = default;

        if (title.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            var quick = _workspace.ReadPage(file);
            title = quick.Title;
            modified = quick.Modified;
            return true;
        }

        try
        {
            var doc = _workspace.ReadPage(file);
            title = doc.Title;
            modified = doc.Modified;

            if (doc.Title.Contains(query, StringComparison.OrdinalIgnoreCase)) return true;

            return doc.TextBoxes.Any(t =>
                t.Text.Contains(query, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    // ------------------------------------------------------------------ theme

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        SaveCurrentPage();

        ThemeService.Toggle();
        _settings.DarkMode = ThemeService.IsDark;
        _settings.Save();

        UpdateThemeButton();

        // Swatches and ink are both theme-adapted, so both have to be rebuilt.
        BuildPenTray();
        SelectTool(PageView.ActiveTool.Kind);

        // Strokes are cached as rendered visuals inside the ink presenter;
        // reloading the page is what forces them to be drawn again in the new
        // palette.
        if (_page is not null)
        {
            var doc = PageView.ToDoc();
            PageView.LoadPage(_page.SectionPath, _page.Path, doc);
        }
    }

    private void UpdateThemeButton()
    {
        // The icon shows what you will get, not what you have.
        ThemeIcon.Data = (Geometry)FindResource(ThemeService.IsDark ? "IconSun" : "IconMoon");
        ThemeButton.ToolTip = ThemeService.IsDark ? "Switch to light" : "Switch to dark";
    }

    // ----------------------------------------------------------------- import

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        CommitTitleRename();
        SaveCurrentPage();

        var window = new ImportWindow(_workspace) { Owner = this };
        window.ShowDialog();

        ReloadTree();
        UpdateEmptyState();
    }

    // -------------------------------------------------------------- shortcuts

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Single-letter tool switching must not fire while typing.
        if (Keyboard.Modifiers != ModifierKeys.None) return;
        if (Keyboard.FocusedElement is TextBox) return;

        ToolKind? kind = e.Key switch
        {
            Key.P => ToolKind.Pen,
            Key.H => ToolKind.Highlighter,
            Key.E => ToolKind.StrokeEraser,
            Key.S => ToolKind.Select,
            Key.T => ToolKind.Text,
            Key.Space => ToolKind.Pan,
            _ => null,
        };

        if (kind is null) return;

        SelectTool(kind.Value);
        e.Handled = true;
    }

    private void SetStatus(string text) => StatusLabel.Text = text;
}
