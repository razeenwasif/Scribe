using System.IO;
using System.Text.Json;
using Scribe.Models;

namespace Scribe.Storage;

/// <summary>
/// The on-disk workspace. Everything is plain folders and JSON files:
///
///   &lt;root&gt;/
///     My Notebook/
///       notebook.json
///       Quick Notes/
///         section.json
///         Meeting 2026-08-11.json
///         assets/ab12cd.png
///
/// Folder and file names track the user-visible titles, so the tree is
/// navigable in Explorer and syncs cleanly through OneDrive/Dropbox/git.
/// </summary>
public sealed class Workspace
{
    public string Root { get; }

    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public Workspace(string root)
    {
        Root = root;
        Directory.CreateDirectory(root);
    }

    public static string DefaultRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Scribe Notebooks");

    // ---------------------------------------------------------------- notebooks

    public IReadOnlyList<string> ListNotebookDirs()
    {
        if (!Directory.Exists(Root)) return Array.Empty<string>();
        return Directory.EnumerateDirectories(Root)
            .Where(d => File.Exists(Path.Combine(d, "notebook.json")))
            .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public NotebookDoc ReadNotebook(string dir) =>
        ReadJson<NotebookDoc>(Path.Combine(dir, "notebook.json"))
        ?? new NotebookDoc { Name = Path.GetFileName(dir) };

    public void WriteNotebook(string dir, NotebookDoc doc)
    {
        Directory.CreateDirectory(dir);
        WriteJson(Path.Combine(dir, "notebook.json"), doc);
    }

    public string CreateNotebook(string name)
    {
        var dir = UniqueDirectory(Root, name);
        Directory.CreateDirectory(dir);
        WriteNotebook(dir, new NotebookDoc { Name = name });
        return dir;
    }

    // ----------------------------------------------------------------- sections

    public IReadOnlyList<string> ListSectionDirs(string notebookDir)
    {
        if (!Directory.Exists(notebookDir)) return Array.Empty<string>();

        var found = Directory.EnumerateDirectories(notebookDir)
            .Where(d => File.Exists(Path.Combine(d, "section.json")))
            .ToList();

        return OrderByManifest(found, ReadNotebook(notebookDir).SectionOrder);
    }

    public SectionDoc ReadSection(string dir) =>
        ReadJson<SectionDoc>(Path.Combine(dir, "section.json"))
        ?? new SectionDoc { Name = Path.GetFileName(dir) };

    public void WriteSection(string dir, SectionDoc doc)
    {
        Directory.CreateDirectory(dir);
        WriteJson(Path.Combine(dir, "section.json"), doc);
    }

    public string CreateSection(string notebookDir, string name)
    {
        var dir = UniqueDirectory(notebookDir, name);
        Directory.CreateDirectory(dir);
        WriteSection(dir, new SectionDoc { Name = name });

        var nb = ReadNotebook(notebookDir);
        nb.SectionOrder.Add(Path.GetFileName(dir));
        WriteNotebook(notebookDir, nb);
        return dir;
    }

    // -------------------------------------------------------------------- pages

    public IReadOnlyList<string> ListPageFiles(string sectionDir)
    {
        if (!Directory.Exists(sectionDir)) return Array.Empty<string>();

        var found = Directory.EnumerateFiles(sectionDir, "*.json")
            .Where(f => !string.Equals(Path.GetFileName(f), "section.json", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var order = ReadSection(sectionDir).PageOrder
            .Select(n => n.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? n : n + ".json")
            .ToList();

        return OrderByManifest(found, order);
    }

    public PageDoc ReadPage(string file) =>
        ReadJson<PageDoc>(file) ?? new PageDoc { Title = Path.GetFileNameWithoutExtension(file) };

    public void WritePage(string file, PageDoc doc)
    {
        doc.Modified = DateTimeOffset.Now;
        WriteJson(file, doc);
    }

    public string CreatePage(string sectionDir, string title)
    {
        var file = UniqueFile(sectionDir, title, ".json");
        WriteJson(file, new PageDoc { Title = title });

        var sec = ReadSection(sectionDir);
        sec.PageOrder.Add(Path.GetFileName(file));
        WriteSection(sectionDir, sec);
        return file;
    }

    /// <summary>
    /// Renames a page's file to follow its title, keeping the section manifest
    /// in step. Returns the (possibly new) path.
    /// </summary>
    public string RenamePageFile(string sectionDir, string file, string newTitle)
    {
        var desired = SanitizeName(newTitle);
        if (string.Equals(Path.GetFileNameWithoutExtension(file), desired, StringComparison.Ordinal))
            return file;

        var target = UniqueFile(sectionDir, newTitle, ".json");
        File.Move(file, target);

        var sec = ReadSection(sectionDir);
        var oldName = Path.GetFileName(file);
        var newName = Path.GetFileName(target);
        var idx = sec.PageOrder.FindIndex(n =>
            string.Equals(n, oldName, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0) sec.PageOrder[idx] = newName;
        else sec.PageOrder.Add(newName);
        WriteSection(sectionDir, sec);

        return target;
    }

    public void DeletePage(string sectionDir, string file)
    {
        if (File.Exists(file)) File.Delete(file);

        var sec = ReadSection(sectionDir);
        sec.PageOrder.RemoveAll(n =>
            string.Equals(n, Path.GetFileName(file), StringComparison.OrdinalIgnoreCase));
        WriteSection(sectionDir, sec);
    }

    // ------------------------------------------------------------------- assets

    public string SaveAsset(string sectionDir, byte[] data, string extension)
    {
        var assets = Path.Combine(sectionDir, "assets");
        Directory.CreateDirectory(assets);

        var name = Guid.NewGuid().ToString("n")[..12] + extension;
        File.WriteAllBytes(Path.Combine(assets, name), data);
        return Path.Combine("assets", name).Replace('\\', '/');
    }

    // -------------------------------------------------------------------- infra

    private static T? ReadJson<T>(string path) where T : class
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json);
        }
        catch
        {
            // A corrupt or partially-synced file must not take the app down;
            // callers fall back to a sensible default.
            return null;
        }
    }

    /// <summary>
    /// Writes via a temp file then swaps it in, so a crash or a sync client
    /// reading mid-write can never observe a half-written page.
    /// </summary>
    private static void WriteJson<T>(string path, T value)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Json));

        if (File.Exists(path)) File.Replace(tmp, path, null);
        else File.Move(tmp, path);
    }

    /// <summary>
    /// Orders discovered paths by an explicit manifest, appending anything the
    /// manifest does not mention. That keeps files dropped into the folder by
    /// hand (or by a sync client) visible instead of silently ignored.
    /// </summary>
    private static List<string> OrderByManifest(List<string> paths, List<string> order)
    {
        var byName = paths.ToDictionary(p => Path.GetFileName(p)!, StringComparer.OrdinalIgnoreCase);
        var result = new List<string>(paths.Count);

        foreach (var name in order)
            if (byName.Remove(name, out var path))
                result.Add(path);

        result.AddRange(byName.Values.OrderBy(p => p, StringComparer.OrdinalIgnoreCase));
        return result;
    }

    private static readonly char[] Invalid =
        Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\' }).Distinct().ToArray();

    public static string SanitizeName(string name)
    {
        var cleaned = new string(name.Select(c => Invalid.Contains(c) ? ' ' : c).ToArray());
        cleaned = string.Join(" ", cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        cleaned = cleaned.Trim().TrimEnd('.');

        if (cleaned.Length == 0) cleaned = "Untitled";
        if (cleaned.Length > 80) cleaned = cleaned[..80].Trim();

        // Names Windows refuses regardless of extension.
        var reserved = new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4",
                               "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3",
                               "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
        if (reserved.Contains(cleaned, StringComparer.OrdinalIgnoreCase)) cleaned += "_";

        return cleaned;
    }

    private static string UniqueDirectory(string parent, string name)
    {
        var baseName = SanitizeName(name);
        var candidate = Path.Combine(parent, baseName);
        int n = 2;
        while (Directory.Exists(candidate) || File.Exists(candidate))
            candidate = Path.Combine(parent, $"{baseName} ({n++})");
        return candidate;
    }

    private static string UniqueFile(string parent, string name, string ext)
    {
        var baseName = SanitizeName(name);
        var candidate = Path.Combine(parent, baseName + ext);
        int n = 2;
        while (File.Exists(candidate))
            candidate = Path.Combine(parent, $"{baseName} ({n++}){ext}");
        return candidate;
    }
}
