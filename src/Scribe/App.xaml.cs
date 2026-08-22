using System.IO;
using System.Text;
using System.Windows;
using Scribe.Diagnostics;
using Scribe.Import;
using Scribe.Storage;

namespace Scribe;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Headless ink render, used to inspect stroke quality without a pen.
        if (TryGetOption(e.Args, "--render-selftest", out var renderPath))
        {
            try
            {
                InkSelfTest.Run(renderPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Self test failed");
            }

            Shutdown();
            return;
        }

        // Scripted import, for bringing a large OneNote library across without
        // sitting through the dialog. Writes a log next to the notebooks.
        if (TryGetOption(e.Args, "--import-onenote", out var importRoot))
        {
            RunHeadlessImport(importRoot);
            Shutdown();
            return;
        }

        new MainWindow().Show();
    }

    private static bool TryGetOption(string[] args, string name, out string value)
    {
        value = "";
        int i = Array.IndexOf(args, name);

        if (i < 0 || i + 1 >= args.Length) return false;

        value = args[i + 1];
        return true;
    }

    private static void RunHeadlessImport(string root)
    {
        var log = new StringBuilder();

        try
        {
            var workspace = new Workspace(root);
            var importer = new OneNoteImporter(workspace);
            importer.Progress += m => log.AppendLine(m);

            var summary = importer.Import(new ImportOptions(), CancellationToken.None);

            log.AppendLine();
            log.AppendLine($"notebooks={summary.Notebooks} sections={summary.Sections} " +
                           $"pages={summary.Pages} strokes={summary.Strokes} images={summary.Images} " +
                           $"warnings={summary.Warnings.Count}");

            foreach (var w in summary.Warnings.Take(50)) log.AppendLine($"warning: {w}");

            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "import-log.txt"), log.ToString());
        }
        catch (Exception ex)
        {
            log.AppendLine($"FAILED: {ex.Message}");

            try
            {
                Directory.CreateDirectory(root);
                File.WriteAllText(Path.Combine(root, "import-log.txt"), log.ToString());
            }
            catch
            {
                // Nowhere to report to in headless mode; the exit is the signal.
            }
        }
    }
}
