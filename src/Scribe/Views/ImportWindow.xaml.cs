using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Windows;
using Scribe.Import;
using Scribe.Storage;

namespace Scribe.Views;

public partial class ImportWindow : Window
{
    private readonly Workspace _workspace;
    private readonly ObservableCollection<NotebookRow> _notebooks = new();
    private readonly StringBuilder _log = new();

    private CancellationTokenSource? _cancellation;
    private bool _running;

    public ImportWindow(Workspace workspace)
    {
        InitializeComponent();

        _workspace = workspace;
        NotebookList.ItemsSource = _notebooks;

        Loaded += (_, _) => LoadNotebooks();
        Closing += OnClosing;
    }

    public sealed class NotebookRow : INotifyPropertyChanged
    {
        public required string Id { get; init; }
        public required string Name { get; init; }

        private bool _selected = true;

        public bool Selected
        {
            get => _selected;
            set
            {
                _selected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    // ------------------------------------------------------------------ notebooks

    private async void LoadNotebooks()
    {
        SetBusy(true, "Looking for OneNote…");

        try
        {
            var found = await RunSta(OneNoteImporter.ListNotebooks);

            _notebooks.Clear();
            foreach (var n in found)
                _notebooks.Add(new NotebookRow { Id = n.Id, Name = n.Name });

            WriteLog(found.Count == 0
                ? "OneNote is running but has no notebooks open."
                : $"Found {found.Count} notebook(s). Choose which to import, then press Import.");
        }
        catch (OneNoteUnavailableException ex)
        {
            WriteLog(ex.Message);
            ImportButton.IsEnabled = false;
        }
        catch (Exception ex)
        {
            WriteLog($"Could not reach OneNote: {ex.Message}");
            ImportButton.IsEnabled = false;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        ImportButton.IsEnabled = true;
        LoadNotebooks();
    }

    // --------------------------------------------------------------------- import

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var selected = _notebooks.Where(n => n.Selected).Select(n => n.Id).ToHashSet();

        if (selected.Count == 0)
        {
            MessageBox.Show(this, "Tick at least one notebook to import.", "Import",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var options = new ImportOptions
        {
            NotebookIds = selected,
            IncludeImages = IncludeImages.IsChecked == true,
        };

        _cancellation = new CancellationTokenSource();
        var token = _cancellation.Token;

        SetBusy(true, "Importing…");
        _log.Clear();
        WriteLog("Starting import…");

        try
        {
            var importer = new OneNoteImporter(_workspace);

            // Progress arrives on the import thread; hop to the UI thread to
            // touch the log.
            importer.Progress += message =>
                Dispatcher.BeginInvoke(new Action(() => WriteLog(message)));

            var summary = await RunSta(() => importer.Import(options, token));

            WriteLog("");
            WriteLog($"Finished: {summary.Notebooks} notebook(s), {summary.Sections} section(s), " +
                     $"{summary.Pages} page(s), {summary.Strokes} ink stroke(s), {summary.Images} picture(s).");

            if (summary.Warnings.Count > 0)
            {
                WriteLog("");
                WriteLog($"{summary.Warnings.Count} item(s) were skipped:");
                foreach (var warning in summary.Warnings.Take(40))
                    WriteLog($"  • {warning}");

                if (summary.Warnings.Count > 40)
                    WriteLog($"  …and {summary.Warnings.Count - 40} more.");
            }

            WriteLog("");
            WriteLog($"Saved into: {_workspace.Root}");
        }
        catch (OperationCanceledException)
        {
            WriteLog("Import cancelled. Anything already written has been kept.");
        }
        catch (OneNoteUnavailableException ex)
        {
            WriteLog(ex.Message);
        }
        catch (Exception ex)
        {
            WriteLog($"Import failed: {ex.Message}");
        }
        finally
        {
            _cancellation?.Dispose();
            _cancellation = null;
            SetBusy(false);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _cancellation?.Cancel();
        WriteLog("Cancelling…");
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_running) return;

        var answer = MessageBox.Show(this,
            "An import is still running. Stop it and close?",
            "Import in progress", MessageBoxButton.OKCancel, MessageBoxImage.Question);

        if (answer != MessageBoxResult.OK)
        {
            e.Cancel = true;
            return;
        }

        _cancellation?.Cancel();
    }

    // ---------------------------------------------------------------------- infra

    private void SetBusy(bool busy, string? message = null)
    {
        _running = busy;

        ImportButton.IsEnabled = !busy;
        CancelButton.IsEnabled = busy;
        CloseButton.IsEnabled = !busy;
        NotebookList.IsEnabled = !busy;

        Spinner.IsIndeterminate = busy;
        Spinner.Visibility = busy ? Visibility.Visible : Visibility.Hidden;

        if (message is not null) WriteLog(message);
    }

    private void WriteLog(string message)
    {
        _log.AppendLine(message);
        Log.Text = _log.ToString();
        LogScroller.ScrollToEnd();
    }

    /// <summary>
    /// Runs work on a dedicated single-threaded-apartment thread.
    ///
    /// The OneNote COM object requires STA, and the WPF ink objects the
    /// importer builds must not be created on the UI thread, where a long
    /// import would freeze the window.
    /// </summary>
    private static Task<T> RunSta<T>(Func<T> work)
    {
        var completion = new TaskCompletionSource<T>();

        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(work());
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        })
        {
            IsBackground = true,
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return completion.Task;
    }
}
