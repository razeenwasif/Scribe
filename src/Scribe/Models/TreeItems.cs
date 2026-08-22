using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Scribe.Models;

public abstract class TreeItem : INotifyPropertyChanged
{
    private string _name = "";

    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    /// <summary>Absolute path to the folder or file backing this item.</summary>
    public required string Path { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}

public sealed class NotebookItem : TreeItem
{
    public string Color { get; set; } = "#7C5CE6";
    public ObservableCollection<SectionItem> Sections { get; } = new();
}

public sealed class SectionItem : TreeItem
{
    public string Color { get; set; } = "#4C9AFF";

    /// <summary>Folder of the notebook this section belongs to.</summary>
    public required string NotebookPath { get; set; }

    public ObservableCollection<PageItem> Pages { get; } = new();
}

public sealed class PageItem : TreeItem
{
    /// <summary>Folder of the section this page belongs to.</summary>
    public required string SectionPath { get; set; }

    private DateTimeOffset _modified;

    public DateTimeOffset Modified
    {
        get => _modified;
        set
        {
            if (Set(ref _modified, value)) OnPropertyChangedModifiedLabel();
        }
    }

    public string ModifiedLabel => Modified == default
        ? ""
        : Modified.LocalDateTime.ToString("d MMM yyyy");

    private void OnPropertyChangedModifiedLabel() =>
        Set(ref _modifiedLabelTrigger, !_modifiedLabelTrigger, nameof(ModifiedLabel));

    private bool _modifiedLabelTrigger;
}
