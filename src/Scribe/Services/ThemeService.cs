using System.Windows;
using Scribe.Ink;

namespace Scribe.Services;

/// <summary>
/// Swaps the application palette at runtime.
///
/// The palette is always merged dictionary 0, so switching theme is a single
/// replacement. Every style refers to the palette through DynamicResource,
/// which means the whole window repaints without anything being rebuilt.
/// </summary>
public static class ThemeService
{
    public static bool IsDark { get; private set; } = true;

    public static event EventHandler? Changed;

    public static void Apply(bool dark)
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;

        var palette = new ResourceDictionary
        {
            Source = new Uri(
                $"/Scribe;component/Themes/{(dark ? "Dark" : "Light")}.xaml",
                UriKind.Relative),
        };

        if (dictionaries.Count > 0) dictionaries[0] = palette;
        else dictionaries.Add(palette);

        IsDark = dark;
        InkTheme.SetDark(dark);

        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static void Toggle() => Apply(!IsDark);
}
