using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using static System.Int32;


namespace PalworldServerManager;

class SteamModsList
{
    public ObservableCollection<SteamMod> ModList { get; set; } = new();
}

public class SteamMod : PropertyChangedBase
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public string AuthorUrl { get; set; } = "";
    public string PreviewImage { get; set; } = "";
    public string Description { get; set; } = "";
    public string AppId { get; set; } = "";

    private bool _isChecked = false;
    public bool IsChecked
    {
        get => _isChecked;
        set => SetField(ref _isChecked, value);
    }
}

public class InstalledModViewModel : PropertyChangedBase
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    private bool _isPlaceholder;
    public bool IsPlaceholder
    {
        get => _isPlaceholder;
        set { _isPlaceholder = value; OnPropertyChanged(); }
    }

    public bool IsMarkedForDelete { get; set; }
}

public class VersionToolTipConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string localVersion = value as string;
        if (string.IsNullOrEmpty(localVersion))
            return "无本地版本";

        return $"本地版本: {localVersion}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

// 布尔值到Visibility转换器
public class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            // 如果值为true，返回Visible；否则返回Collapsed
            return boolValue ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

// 字符串到Visibility转换器
public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string stringValue)
        {
            // 如果字符串不为空，返回Visible；否则返回Collapsed
            return !string.IsNullOrEmpty(stringValue) ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
