using System;
using Microsoft.Win32;
using Application = System.Windows.Application;
using ResourceDictionary = System.Windows.ResourceDictionary;

namespace ConvertXPortable.Services;

public enum AppTheme
{
    Light,
    Dark,
    System
}

public static class ThemeManager
{
    private const string LightThemeUri = "Themes/LightTheme.xaml";
    private const string DarkThemeUri = "Themes/DarkTheme.xaml";

    private static AppTheme _currentMode = AppTheme.Light;
    private static bool _isDarkActive;

    public static event EventHandler<bool>? ThemeChanged;

    public static AppTheme CurrentMode => _currentMode;

    public static bool IsDarkActive => _isDarkActive;

    public static void Initialize(AppTheme mode)
    {
        Apply(mode);
    }

    public static void Apply(AppTheme mode)
    {
        _currentMode = mode;
        var dark = mode switch
        {
            AppTheme.Dark => true,
            AppTheme.Light => false,
            AppTheme.System => IsSystemDark(),
            _ => false
        };

        SwapDictionary(dark);
        _isDarkActive = dark;
        ThemeChanged?.Invoke(null, dark);
    }

    private static void SwapDictionary(bool dark)
    {
        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        var targetUri = new Uri(dark ? DarkThemeUri : LightThemeUri, UriKind.Relative);
        var newDict = new ResourceDictionary { Source = targetUri };

        var merged = app.Resources.MergedDictionaries;
        for (var i = merged.Count - 1; i >= 0; i--)
        {
            var existing = merged[i];
            if (existing.Source is { } src &&
                (src.OriginalString.EndsWith("LightTheme.xaml", StringComparison.OrdinalIgnoreCase) ||
                 src.OriginalString.EndsWith("DarkTheme.xaml", StringComparison.OrdinalIgnoreCase)))
            {
                merged.RemoveAt(i);
            }
        }

        merged.Insert(0, newDict);
    }

    private static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            if (value is int intValue)
            {
                return intValue == 0;
            }
        }
        catch
        {
        }
        return false;
    }
}
