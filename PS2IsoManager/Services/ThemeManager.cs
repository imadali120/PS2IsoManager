using System.IO;
using System.Windows;

namespace PS2IsoManager.Services;

public enum AppTheme
{
    OplDark,
    Midnight,
    Light,
    Cosmic
}

public static class ThemeManager
{
    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PS2IsoManager");

    private static readonly string ThemeSettingsPath = Path.Combine(SettingsDir, "theme.txt");

    public static AppTheme CurrentTheme { get; private set; } = AppTheme.OplDark;

    public static void ApplyTheme(AppTheme theme)
    {
        CurrentTheme = theme;

        var dicts = Application.Current.Resources.MergedDictionaries;
        dicts.Clear();

        string colorFile = theme switch
        {
            AppTheme.OplDark  => "Resources/Themes/Colors/OplDarkColors.xaml",
            AppTheme.Midnight => "Resources/Themes/Colors/MidnightColors.xaml",
            AppTheme.Light    => "Resources/Themes/Colors/LightColors.xaml",
            AppTheme.Cosmic   => "Resources/Themes/Colors/CosmicColors.xaml",
            _ => "Resources/Themes/Colors/OplDarkColors.xaml"
        };

        dicts.Add(new ResourceDictionary { Source = new Uri(colorFile, UriKind.Relative) });
        dicts.Add(new ResourceDictionary { Source = new Uri("Resources/Themes/BaseStyles.xaml", UriKind.Relative) });

        SaveTheme(theme);
    }

    public static AppTheme LoadSavedTheme()
    {
        try
        {
            if (File.Exists(ThemeSettingsPath))
            {
                string value = File.ReadAllText(ThemeSettingsPath).Trim();
                if (Enum.TryParse<AppTheme>(value, out var theme))
                    return theme;
            }
        }
        catch { }
        return AppTheme.OplDark;
    }

    private static void SaveTheme(AppTheme theme)
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            File.WriteAllText(ThemeSettingsPath, theme.ToString());
        }
        catch { }
    }
}
