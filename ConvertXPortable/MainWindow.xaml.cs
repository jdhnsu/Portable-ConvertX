using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using ConvertXPortable.Models;
using ConvertXPortable.Services;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace ConvertXPortable;

public partial class MainWindow : Window
{
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMSBT_MAINWINDOW = 2;
    private const int WM_SETTINGCHANGE = 0x001A;
    private const string IMMERSIVE_COLOR_SET = "ImmersiveColorSet";
    private const int BUILD_WIN11 = 22000;

    [DllImport("dwmapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const string NvidiaAccelerationArguments = "-hwaccel cuda -c:v h264_nvenc";

    private readonly AppViewModel _viewModel = new();
    private readonly PathResolver _pathResolver = new();
    private readonly ConfigurationService _configurationService;
    private readonly ConversionManifest _manifest;
    private readonly ConversionRouter _router;
    private readonly ConversionExecutor _executor;
    private CancellationTokenSource? _conversionCts;
    private bool _updatingSelection;
    private bool _syncingNvidiaToggle;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        ThemeManager.ThemeChanged += OnThemeChanged;
        Loaded += (_, _) => UpdateThemeCardSelection();
        Closed += (_, _) => ThemeManager.ThemeChanged -= OnThemeChanged;

        _configurationService = new ConfigurationService(_pathResolver);
        var catalog = _configurationService.LoadToolCatalog();
        _manifest = _configurationService.LoadConversionManifest();
        var toolStatuses = _configurationService.GetToolStatuses(catalog);
        _router = new ConversionRouter(_manifest.Conversions, toolStatuses);
        _executor = new ConversionExecutor(_pathResolver);

        foreach (var toolStatus in toolStatuses)
        {
            _viewModel.ToolStatuses.Add(toolStatus);
        }

        var available = toolStatuses.Count(status => status.IsAvailable);
        _viewModel.AvailableToolSummary = $"{available}/{toolStatuses.Count} 个工具可用";
        _viewModel.RootStatusText = $"根目录: {_pathResolver.WorkspaceRoot}";
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;

        if (!File.Exists(_pathResolver.ToolsJsonPath))
        {
            _viewModel.AppendLog("未找到 TestTools/tools.json。请从项目根目录启动，或把 TestTools 放在应用目录的上级路径中。");
        }

        if (!File.Exists(_pathResolver.ConversionsJsonPath))
        {
            _viewModel.AppendLog("未找到 conversions.json。转换规则列表为空。");
        }

        DataContext = _viewModel;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_updatingSelection)
        {
            return;
        }

        if (e.PropertyName == nameof(AppViewModel.InputFilePath))
        {
            RefreshOutputFormats();
        }
        else if (e.PropertyName == nameof(AppViewModel.SelectedOutputFormat))
        {
            RefreshConverterOptions();
        }
        else if (e.PropertyName == nameof(AppViewModel.AdvancedArguments))
        {
            SyncNvidiaToggleFromArguments();
        }
    }

    private void ChooseFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            CheckFileExists = true,
            Multiselect = false,
            Title = "选择要转换的文件",
            Filter = "所有文件 (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) == true)
        {
            SetInputFile(dialog.FileName);
        }
    }

    private void ChooseOutputDirectory_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = "选择输出目录",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(_viewModel.OutputDirectory)
                ? _viewModel.OutputDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
        };

        if (dialog.ShowDialog() == WinForms.DialogResult.OK)
        {
            _viewModel.OutputDirectory = dialog.SelectedPath;
        }
    }

    private async void StartConvert_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanStart || _viewModel.SelectedConverterOption is null)
        {
            return;
        }

        var option = _viewModel.SelectedConverterOption;
        var inputPath = _viewModel.InputFilePath;
        var outputFormat = ConversionRouter.NormalizeExtension(_viewModel.SelectedOutputFormat);
        var outputDirectory = _viewModel.OutputDirectory;
        var outputPath = BuildOutputPath(inputPath, outputDirectory, outputFormat);

        _conversionCts = new CancellationTokenSource();
        _viewModel.IsConverting = true;
        _viewModel.LogText = "";
        _viewModel.StatusText = "正在转换...";
        _viewModel.AppendLog($"Input: {inputPath}");
        _viewModel.AppendLog($"Output: {outputPath}");
        _viewModel.AppendLog($"Converter: {option.Rule.Converter}");
        _viewModel.AppendLog("");

        try
        {
            var exitCode = await _executor.ExecuteAsync(
                option,
                inputPath,
                outputPath,
                outputDirectory,
                outputFormat,
                _viewModel.AdvancedArguments,
                AppendLogOnUiThread,
                _conversionCts.Token);

            if (exitCode == 0)
            {
                _viewModel.StatusText = "转换完成。";
                _viewModel.AppendLog("");
                _viewModel.AppendLog("转换完成。");
            }
            else
            {
                _viewModel.StatusText = $"转换失败，退出码 {exitCode}。";
                _viewModel.AppendLog("");
                _viewModel.AppendLog($"转换失败，退出码 {exitCode}。");
            }
        }
        catch (OperationCanceledException)
        {
            _viewModel.StatusText = "转换已取消。";
            _viewModel.AppendLog("转换已取消。");
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = "转换失败。";
            _viewModel.AppendLog(ex.ToString());
        }
        finally
        {
            _conversionCts?.Dispose();
            _conversionCts = null;
            _viewModel.IsConverting = false;
        }
    }

    private void CancelConvert_Click(object sender, RoutedEventArgs e)
    {
        _conversionCts?.Cancel();
        _executor.Cancel();
        _viewModel.StatusText = "正在取消...";
    }

    private void OpenOutputDirectory_Click(object sender, RoutedEventArgs e)
    {
        var directory = _viewModel.OutputDirectory;
        if (!Directory.Exists(directory))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = directory,
            UseShellExecute = true
        });
    }

    private void HomeNav_Click(object sender, RoutedEventArgs e)
        => ShowPage(home: true);

    private void SettingsNav_Click(object sender, RoutedEventArgs e)
        => ShowPage(home: false);

    private void ShowPage(bool home)
    {
        HomePage.Visibility = home ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = home ? Visibility.Collapsed : Visibility.Visible;
        HomeNavButton.Style = (Style)FindResource(home ? "ActiveNavButtonStyle" : "NavButtonStyle");
        SettingsNavButton.Style = (Style)FindResource(home ? "NavButtonStyle" : "ActiveNavButtonStyle");
    }

    private void NvidiaAccelerationToggle_Checked(object sender, RoutedEventArgs e)
    {
        if (_syncingNvidiaToggle)
        {
            return;
        }

        var current = _viewModel.AdvancedArguments.Trim();
        if (ContainsNvidiaAccelerationArguments(current))
        {
            return;
        }

        _viewModel.AdvancedArguments = string.IsNullOrWhiteSpace(current)
            ? NvidiaAccelerationArguments
            : $"{current} {NvidiaAccelerationArguments}";
    }

    private void NvidiaAccelerationToggle_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_syncingNvidiaToggle)
        {
            return;
        }

        _viewModel.AdvancedArguments = RemoveNvidiaAccelerationArguments(_viewModel.AdvancedArguments);
    }

    private void DropZone_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)
            ? System.Windows.DragDropEffects.Copy
            : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void DropZone_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            SetInputFile(files[0]);
        }
    }

    private void SetInputFile(string path)
    {
        _viewModel.InputFilePath = path;
        if (string.IsNullOrWhiteSpace(_viewModel.OutputDirectory) || !Directory.Exists(_viewModel.OutputDirectory))
        {
            _viewModel.OutputDirectory = Path.GetDirectoryName(path) ?? "";
        }
    }

    private void RefreshOutputFormats()
    {
        _updatingSelection = true;
        try
        {
            _viewModel.OutputFormats.Clear();
            _viewModel.ConverterOptions.Clear();
            _viewModel.SelectedOutputFormat = "";
            _viewModel.SelectedConverterOption = null;

            if (!File.Exists(_viewModel.InputFilePath))
            {
                _viewModel.StatusText = "请选择一个有效文件。";
                return;
            }

            var formats = _router.GetOutputFormats(_viewModel.InputFilePath);
            foreach (var format in formats)
            {
                _viewModel.OutputFormats.Add(format);
            }

            _viewModel.StatusText = formats.Count == 0
                ? "当前输入格式暂无匹配规则。"
                : $"找到 {formats.Count} 种可选输出格式。";

            if (_viewModel.OutputFormats.Count > 0)
            {
                _viewModel.SelectedOutputFormat = _viewModel.OutputFormats[0];
            }
        }
        finally
        {
            _updatingSelection = false;
        }

        RefreshConverterOptions();
    }

    private void RefreshConverterOptions()
    {
        _viewModel.ConverterOptions.Clear();
        _viewModel.SelectedConverterOption = null;

        if (!File.Exists(_viewModel.InputFilePath) || string.IsNullOrWhiteSpace(_viewModel.SelectedOutputFormat))
        {
            return;
        }

        var options = _router.GetConverterOptions(_viewModel.InputFilePath, _viewModel.SelectedOutputFormat);
        foreach (var option in options)
        {
            _viewModel.ConverterOptions.Add(option);
        }

        _viewModel.SelectedConverterOption = _viewModel.ConverterOptions.FirstOrDefault(option => option.Tool.IsAvailable)
            ?? _viewModel.ConverterOptions.FirstOrDefault();

        if (_viewModel.SelectedConverterOption is null)
        {
            _viewModel.StatusText = "没有可用转换器。";
        }
    }

    private void AppendLogOnUiThread(string message)
    {
        Dispatcher.Invoke(() => _viewModel.AppendLog(message));
    }

    private void SyncNvidiaToggleFromArguments()
    {
        _syncingNvidiaToggle = true;
        try
        {
            NvidiaAccelerationToggle.IsChecked = ContainsNvidiaAccelerationArguments(_viewModel.AdvancedArguments);
        }
        finally
        {
            _syncingNvidiaToggle = false;
        }
    }

    private static bool ContainsNvidiaAccelerationArguments(string arguments)
    {
        return arguments.Contains(NvidiaAccelerationArguments, StringComparison.OrdinalIgnoreCase);
    }

    private static string RemoveNvidiaAccelerationArguments(string arguments)
    {
        return arguments
            .Replace(NvidiaAccelerationArguments, "", StringComparison.OrdinalIgnoreCase)
            .Trim();
    }

    private static string BuildOutputPath(string inputPath, string outputDirectory, string outputFormat)
    {
        var fileName = Path.GetFileNameWithoutExtension(inputPath);
        var extension = outputFormat.TrimStart('.');
        if (extension.Contains('.', StringComparison.Ordinal))
        {
            extension = extension.Split('.', StringSplitOptions.RemoveEmptyEntries).Last();
        }

        return Path.Combine(outputDirectory, $"{fileName}.{extension}");
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        => SystemCommands.MinimizeWindow(this);

    private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(this);
        }
        else
        {
            SystemCommands.MaximizeWindow(this);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
        => SystemCommands.CloseWindow(this);

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(hwnd);
        source?.AddHook(WndProc);
        ApplyMica(hwnd);
    }

    private void ApplyMica(IntPtr hwnd)
    {
        var isWin11 = Environment.OSVersion.Version.Build >= BUILD_WIN11;
        var dark = ThemeManager.IsDarkActive;

        if (isWin11)
        {
            var backdrop = DWMSBT_MAINWINDOW;
            DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
        }
        else
        {
            var (start, end) = dark ? ("#101419", "#1C2026") : ("#F2F3FB", "#F9F9FF");
            RootGrid.Background = new System.Windows.Media.LinearGradientBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(start),
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(end),
                new System.Windows.Point(0, 0),
                new System.Windows.Point(1, 1));
        }
        ApplyDarkMode(hwnd, dark);
    }

    private void ApplyDarkMode(IntPtr hwnd, bool useDarkMode)
    {
        var useDark = useDarkMode ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_SETTINGCHANGE)
        {
            var str = Marshal.PtrToStringUni(lParam);
            if (str == IMMERSIVE_COLOR_SET)
            {
                if (ThemeManager.CurrentMode == AppTheme.System)
                {
                    ThemeManager.Apply(AppTheme.System);
                }
                ApplyMica(hwnd);
            }
        }
        return IntPtr.Zero;
    }

    private void ThemeCardLight_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => ThemeManager.Apply(AppTheme.Light);

    private void ThemeCardDark_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => ThemeManager.Apply(AppTheme.Dark);

    private void ThemeCardSystem_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => ThemeManager.Apply(AppTheme.System);

    private void OnThemeChanged(object? sender, bool isDark)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            ApplyMica(hwnd);
        }
        UpdateThemeCardSelection();
    }

    private void UpdateThemeCardSelection()
    {
        if (ThemeCardLight is null || ThemeCardDark is null || ThemeCardSystem is null)
        {
            return;
        }

        var unselected = TryFindResource("ThemeCardBorderBrush") as System.Windows.Media.Brush
            ?? System.Windows.Media.Brushes.Transparent;
        var selected = TryFindResource("ThemeCardSelectedBorderBrush") as System.Windows.Media.Brush
            ?? unselected;

        ThemeCardLight.BorderBrush = unselected;
        ThemeCardDark.BorderBrush = unselected;
        ThemeCardSystem.BorderBrush = unselected;

        var active = ThemeManager.CurrentMode switch
        {
            AppTheme.Dark => ThemeCardDark,
            AppTheme.System => ThemeCardSystem,
            _ => ThemeCardLight
        };
        active.BorderBrush = selected;
    }
}
