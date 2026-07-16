using System.Windows;

namespace Phonie.Services;

public static class ThemeService
{
    public const string Dark = "Dark";
    public const string Light = "Light";

    public static void Apply(string? theme)
    {
        var normalizedTheme = string.Equals(theme, Light, StringComparison.OrdinalIgnoreCase) ? Light : Dark;
        var dictionary = new ResourceDictionary
        {
            Source = new Uri($"Themes/{normalizedTheme}.xaml", UriKind.Relative),
        };

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        if (dictionaries.Count == 0)
        {
            dictionaries.Add(dictionary);
        }
        else
        {
            dictionaries[0] = dictionary;
        }
    }
}
