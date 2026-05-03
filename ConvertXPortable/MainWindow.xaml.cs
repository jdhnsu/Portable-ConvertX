using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
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
    private readonly ConversionCommandPreviewBuilder _commandPreviewBuilder;
    private readonly AiSettingsService _aiSettingsService;
    private readonly AiHistoryService _aiHistoryService;
    private readonly AiChatService _aiChatService;
    private readonly AiConversionPlanner _aiPlanner;
    private readonly McpSettingsService _mcpSettingsService;
    private readonly McpHttpServer _mcpServer;
    private AiSettings _aiSettings;
    private AiHistoryDocument _aiHistory;
    private McpSettings _mcpSettings;
    private AiPlanResult? _lastAiPlan;
    private CancellationTokenSource? _conversionCts;
    private CancellationTokenSource? _agentCts;
    private bool _updatingSelection;
    private bool _syncingNvidiaToggle;
    private bool _syncingAgentSelection;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        ThemeManager.ThemeChanged += OnThemeChanged;
        Loaded += (_, _) => UpdateThemeCardSelection();

        _configurationService = new ConfigurationService(_pathResolver);
        var catalog = _configurationService.LoadToolCatalog();
        _manifest = _configurationService.LoadConversionManifest();
        var toolStatuses = _configurationService.GetToolStatuses(catalog);
        _router = new ConversionRouter(_manifest.Conversions, toolStatuses);
        _executor = new ConversionExecutor(_pathResolver);
        _commandPreviewBuilder = new ConversionCommandPreviewBuilder(_pathResolver);
        _aiSettingsService = new AiSettingsService();
        _aiHistoryService = new AiHistoryService();
        _aiChatService = new AiChatService();
        _aiPlanner = new AiConversionPlanner(_pathResolver, _router, _commandPreviewBuilder, _aiChatService);
        _mcpSettingsService = new McpSettingsService();
        _mcpServer = new McpHttpServer(_pathResolver, _router, toolStatuses);
        _aiSettings = _aiSettingsService.Load();
        _aiHistory = _aiHistoryService.Load();
        _mcpSettings = _mcpSettingsService.Load();
        Loaded += async (_, _) =>
        {
            if (_mcpSettings.IsEnabled)
            {
                await StartMcpAsync(showPrompt: false);
            }
        };
        Closed += (_, _) =>
        {
            ThemeManager.ThemeChanged -= OnThemeChanged;
            _mcpServer.Stop();
        };

        foreach (var toolStatus in toolStatuses)
        {
            _viewModel.ToolStatuses.Add(toolStatus);
        }

        var available = toolStatuses.Count(status => status.IsAvailable);
        _viewModel.AvailableToolSummary = $"{available}/{toolStatuses.Count} 个工具可用";
        _viewModel.RootStatusText = $"根目录: {_pathResolver.WorkspaceRoot}";
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        LoadAiSettingsIntoViewModel();
        LoadAiHistoryIntoViewModel();
        LoadMcpSettingsIntoViewModel();

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
        else if (e.PropertyName == nameof(AppViewModel.AgentInputFilePath))
        {
            RefreshAgentOutputFormats();
        }
        else if (e.PropertyName == nameof(AppViewModel.AgentSelectedOutputFormat))
        {
            RefreshAgentRecommendationState();
        }
        else if (e.PropertyName == nameof(AppViewModel.EnableCommandLineExecution))
        {
            UpdateAdvancedCommandUi();
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
        => ShowPage(home: false, agent: false);

    private void AgentNav_Click(object sender, RoutedEventArgs e)
        => ShowPage(home: false, agent: true);

    private void ShowPage(bool home, bool agent = false)
    {
        HomePage.Visibility = home ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = !home && !agent ? Visibility.Visible : Visibility.Collapsed;
        AgentPage.Visibility = agent ? Visibility.Visible : Visibility.Collapsed;
        HomeNavButton.Style = (Style)FindResource(home ? "ActiveNavButtonStyle" : "NavButtonStyle");
        SettingsNavButton.Style = (Style)FindResource(!home && !agent ? "ActiveNavButtonStyle" : "NavButtonStyle");
        AgentNavButton.Style = (Style)FindResource(agent ? "ActiveNavButtonStyle" : "NavButtonStyle");
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

    private void AgentDropZone_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            SetAgentInputFile(files[0]);
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

    private void AgentChooseFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            CheckFileExists = true,
            Multiselect = false,
            Title = "选择 AI 转换输入文件",
            Filter = "所有文件 (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) == true)
        {
            SetAgentInputFile(dialog.FileName);
        }
    }

    private void AgentChooseOutputDirectory_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = "选择 AI 输出目录",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(_viewModel.AgentOutputDirectory)
                ? _viewModel.AgentOutputDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
        };

        if (dialog.ShowDialog() == WinForms.DialogResult.OK)
        {
            _viewModel.AgentOutputDirectory = dialog.SelectedPath;
        }
    }

    private async void AgentGenerate_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsAgentBusy)
        {
            return;
        }

        await GenerateAgentRecommendationAsync();
    }

    private async Task GenerateAgentRecommendationAsync()
    {
        if (!_viewModel.CanStartAgent)
        {
            _viewModel.AgentStatusText = "请先选择有效输入文件、输出目录和目标格式。";
            return;
        }

        if (!_aiSettings.IsConfigured)
        {
            _viewModel.AgentStatusText = "请先在设置页配置 AI 模型。";
            return;
        }

        SaveAiSettingsFromViewModel();
        _agentCts?.Cancel();
        _agentCts?.Dispose();
        _agentCts = new CancellationTokenSource();
        _viewModel.IsAgentBusy = true;
        _viewModel.AgentStatusText = "正在请求 AI 推荐...";
        _viewModel.AgentTerminalLog = "";

        try
        {
            _viewModel.AppendAgentChat($"你: {_viewModel.AgentRequestText}");
            var result = await _aiPlanner.PlanAsync(
                _aiSettings,
                _viewModel.AgentInputFilePath,
                _viewModel.AgentOutputDirectory,
                _viewModel.AgentSelectedOutputFormat,
                _viewModel.AgentRequestText,
                _agentCts.Token);

            _viewModel.AgentCommandPreview = result.Preview.DisplayCommand;
            _viewModel.AgentCommandInput = result.Preview.DisplayCommand;
            _viewModel.AgentRecommendation = result.Explanation;
            _viewModel.AgentRiskText = result.Risk;
            _viewModel.AgentStatusText = $"推荐完成：{result.Option.Rule.Converter}";
            _lastAiPlan = result;
            _viewModel.AppendAgentChat("");
            _viewModel.AppendAgentChat("AI: " + BuildAssistantCommandMessage(result));
            if (result.IsReadOnlyCommand || IsReadOnlyPowerShellCommand(result.Preview.DisplayCommand))
            {
                await RunGeneratedReadOnlyCommandAsync(result.Preview.DisplayCommand);
            }
            AppendAiHistory(new AiHistoryItem
            {
                CreatedAt = DateTime.Now,
                Title = Path.GetFileName(_viewModel.AgentInputFilePath),
                UserRequest = _viewModel.AgentRequestText,
                ResponseSummary = result.Explanation,
                Command = result.Preview.DisplayCommand,
                Status = "推荐完成"
            });
        }
        catch (Exception ex)
        {
            _viewModel.AgentStatusText = "推荐失败。";
            _viewModel.AgentRecommendation = ex.Message;
            _viewModel.AppendAgentTerminal(ex.ToString());
            AppendAiHistory(new AiHistoryItem
            {
                CreatedAt = DateTime.Now,
                Title = Path.GetFileName(_viewModel.AgentInputFilePath),
                UserRequest = _viewModel.AgentRequestText,
                ResponseSummary = ex.Message,
                Command = "",
                Status = "推荐失败"
            });
        }
        finally
        {
            _viewModel.IsAgentBusy = false;
            _agentCts?.Dispose();
            _agentCts = null;
        }
    }

    private async void AgentExecuteRecommended_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanStartAgent || string.IsNullOrWhiteSpace(_viewModel.AgentCommandPreview))
        {
            return;
        }

        if (_viewModel.SelectedAgentHistory is not null)
        {
            _viewModel.SelectedAgentHistory.Status = "执行中";
        }

        _viewModel.IsAgentBusy = true;
        _viewModel.AgentTerminalLog = "";
        _viewModel.AgentStatusText = "正在执行推荐命令...";

        try
        {
            var result = await ExecuteRecommendedAsync();
            _viewModel.AgentStatusText = result == 0 ? "执行完成。" : $"执行失败，退出码 {result}。";
            AppendAiHistory(new AiHistoryItem
            {
                CreatedAt = DateTime.Now,
                Title = Path.GetFileName(_viewModel.AgentInputFilePath),
                UserRequest = _viewModel.AgentRequestText,
                ResponseSummary = _viewModel.AgentRecommendation,
                Command = _viewModel.AgentCommandPreview,
                Status = result == 0 ? "执行完成" : $"失败 {result}"
            });

            if (result != 0)
            {
                await AppendFailureAnalysisAsync(_viewModel.AgentCommandPreview, _viewModel.AgentTerminalLog);
            }
        }
        catch (Exception ex)
        {
            _viewModel.AgentStatusText = "执行失败。";
            _viewModel.AppendAgentTerminal(ex.ToString());
            await AppendFailureAnalysisAsync(_viewModel.AgentCommandPreview, _viewModel.AgentTerminalLog);
        }
        finally
        {
            _viewModel.IsAgentBusy = false;
        }
    }

    private async Task<int> ExecuteRecommendedAsync()
    {
        if (_viewModel.SelectedAgentHistory is not null)
        {
            _viewModel.SelectedAgentHistory.Status = "执行中";
        }

        var plan = _lastAiPlan ?? throw new InvalidOperationException("请先生成一次 AI 推荐。");

        var outputPath = plan.Preview.OutputPath;
        return await _executor.ExecuteAsync(
            plan.Option,
            _viewModel.AgentInputFilePath,
            outputPath,
            _viewModel.AgentOutputDirectory,
            _viewModel.AgentSelectedOutputFormat,
            plan.Preview.AdvancedArguments,
            AppendAgentLogOnUiThread,
            CancellationToken.None);
    }

    private void AgentRunPowerShell_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.EnableCommandLineExecution)
        {
            _viewModel.AgentStatusText = "请先在设置的高级设置中开启命令行执行。";
            return;
        }

        _ = RunPowerShellCommandAsync();
    }

    private void AgentClearTerminal_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.AgentTerminalLog = "";
    }

    private bool _agentChatAutoScroll = true;

    private void AgentMessagesScrollViewer_ScrollChanged(object sender, System.Windows.Controls.ScrollChangedEventArgs e)
    {
        if (e.ExtentHeightChange.Equals(0))
        {
            _agentChatAutoScroll = e.VerticalOffset >= e.ExtentHeight - e.ViewportHeight - 1;
        }
        else if (_agentChatAutoScroll)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_agentChatAutoScroll)
                {
                    AgentMessagesScrollViewer?.ScrollToBottom();
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private void AgentMessagesScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is System.Windows.Controls.ScrollViewer sv)
        {
            sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta);
            e.Handled = true;
        }
    }

    private void AgentNewChat_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.AgentMessages.Clear();
        _viewModel.AgentChatLog = "";
        _viewModel.AgentChatInput = "";
        _syncingAgentSelection = true;
        try
        {
            _viewModel.SelectedAgentHistory = null;
        }
        finally
        {
            _syncingAgentSelection = false;
        }
        _agentChatAutoScroll = true;
        AgentChatInputBox?.Focus();
    }

    private void AgentChatInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter || (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
        {
            return;
        }

        e.Handled = true;
        if (_viewModel.CanSendChat)
        {
            _ = SendAgentChatAsync();
        }
    }

    private void AgentQuickPrompt_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string prompt } && !string.IsNullOrWhiteSpace(prompt))
        {
            _viewModel.AgentChatInput = prompt;
            AgentChatInputBox?.Focus();
        }
    }

    private void AgentBubbleCopy_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string content } || string.IsNullOrEmpty(content))
        {
            return;
        }

        try
        {
            System.Windows.Clipboard.SetText(content);
            _viewModel.AgentStatusText = "已复制到剪贴板。";
        }
        catch
        {
            // ignore clipboard failures (rare; e.g. another app holds the clipboard)
        }
    }

    private async Task RunPowerShellCommandAsync()
    {
        if (string.IsNullOrWhiteSpace(_viewModel.AgentCommandInput))
        {
            _viewModel.AgentStatusText = "请输入 PowerShell 命令。";
            return;
        }

        var isReadOnly = IsReadOnlyPowerShellCommand(_viewModel.AgentCommandInput);
        if (!isReadOnly)
        {
            var confirm = System.Windows.MessageBox.Show(
                this,
                "这条命令可能会修改文件或系统状态，确认执行吗？",
                "确认执行 PowerShell",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes)
            {
                _viewModel.AgentStatusText = "已取消执行。";
                return;
            }
        }

        _viewModel.IsAgentBusy = true;
        _viewModel.AgentTerminalLog = "";
        _viewModel.AgentStatusText = "正在执行 PowerShell...";

        try
        {
            var exitCode = await ExecutePowerShellAsync(_viewModel.AgentCommandInput, AppendAgentLogOnUiThread, CancellationToken.None);
            _viewModel.AgentStatusText = exitCode == 0 ? "PowerShell 执行完成。" : $"PowerShell 失败，退出码 {exitCode}。";
        }
        catch (Exception ex)
        {
            _viewModel.AgentStatusText = "PowerShell 执行失败。";
            _viewModel.AppendAgentTerminal(ex.ToString());
        }
        finally
        {
            _viewModel.IsAgentBusy = false;
        }
    }

    private async Task AppendFailureAnalysisAsync(string command, string logText)
    {
        if (!_aiSettings.IsConfigured || string.IsNullOrWhiteSpace(logText))
        {
            return;
        }

        try
        {
            var analysis = await _aiPlanner.AnalyzeFailureAsync(_aiSettings, command, logText, CancellationToken.None);
            _viewModel.AppendAgentTerminal("");
            _viewModel.AppendAgentTerminal("AI 故障分析:");
            _viewModel.AppendAgentTerminal(analysis);
        }
        catch (Exception ex)
        {
            _viewModel.AppendAgentTerminal("AI 故障分析失败: " + ex.Message);
        }
    }

    private async Task<int> ExecutePowerShellAsync(string command, Action<string> log, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(command);

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                log(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                log(e.Data);
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException("无法启动 PowerShell。");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }

    private void AgentHistorySelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_syncingAgentSelection)
        {
            return;
        }

        if (_viewModel.SelectedAgentHistory is null)
        {
            return;
        }

        _syncingAgentSelection = true;
        try
        {
            _viewModel.AgentRequestText = _viewModel.SelectedAgentHistory.UserRequest;
            _viewModel.AgentCommandPreview = _viewModel.SelectedAgentHistory.Command;
            _viewModel.AgentRecommendation = _viewModel.SelectedAgentHistory.ResponseSummary;
            _viewModel.AgentStatusText = _viewModel.SelectedAgentHistory.Status;
        }
        finally
        {
            _syncingAgentSelection = false;
        }
    }

    private void AgentTestConnection_Click(object sender, RoutedEventArgs e)
    {
        _ = TestAiConnectionAsync();
    }

    private async Task TestAiConnectionAsync()
    {
        try
        {
            SaveAiSettingsFromViewModel();
            await _aiChatService.TestConnectionAsync(_aiSettings, CancellationToken.None);
            _viewModel.AgentStatusText = "AI 连接成功。";
        }
        catch (Exception ex)
        {
            _viewModel.AgentStatusText = "AI 连接失败。";
            _viewModel.AppendAgentTerminal(ex.Message);
        }
    }

    private void AgentSaveSettings_Click(object sender, RoutedEventArgs e)
    {
        SaveAiSettingsFromViewModel();
        _viewModel.AgentStatusText = "AI 设置已保存。";
    }

    private void AgentCommandPreview_Copy(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_viewModel.AgentCommandPreview))
        {
            System.Windows.Clipboard.SetText(_viewModel.AgentCommandPreview);
            _viewModel.AgentStatusText = "命令已复制。";
        }
    }

    private void AgentSendChat_Click(object sender, RoutedEventArgs e)
    {
        _ = SendAgentChatAsync();
    }

    private async Task SendAgentChatAsync()
    {
        if (string.IsNullOrWhiteSpace(_viewModel.AgentChatInput))
        {
            return;
        }

        if (!_aiSettings.IsConfigured)
        {
            _viewModel.AgentStatusText = "请先配置 AI 模型。";
            return;
        }

        var userMessage = _viewModel.AgentChatInput.Trim();
        _viewModel.AgentChatInput = "";
        _viewModel.IsAgentBusy = true;
        try
        {
            var conversation = _viewModel.CanStartAgent
                ? null
                : BuildConversationMessages(userMessage);
            _viewModel.AppendAgentChat($"你: {userMessage}");

            if (_viewModel.CanStartAgent)
            {
                _viewModel.AgentRequestText = userMessage;
                var result = await _aiPlanner.PlanAsync(
                    _aiSettings,
                    _viewModel.AgentInputFilePath,
                    _viewModel.AgentOutputDirectory,
                    _viewModel.AgentSelectedOutputFormat,
                    userMessage,
                    CancellationToken.None);

                _lastAiPlan = result;
                _viewModel.AgentCommandPreview = result.Preview.DisplayCommand;
                _viewModel.AgentCommandInput = result.Preview.DisplayCommand;
                _viewModel.AgentRecommendation = result.Explanation;
                _viewModel.AgentRiskText = result.Risk;
                _viewModel.AppendAgentChat("");
                _viewModel.AppendAgentChat("AI: " + BuildAssistantCommandMessage(result));
                _viewModel.AgentStatusText = result.IsReadOnlyCommand ? "已生成只读命令，可直接执行。" : "已生成命令，执行前需要确认。";
                if (result.IsReadOnlyCommand || IsReadOnlyPowerShellCommand(result.Preview.DisplayCommand))
                {
                    await RunGeneratedReadOnlyCommandAsync(result.Preview.DisplayCommand);
                }
                AppendAiHistory(new AiHistoryItem
                {
                    CreatedAt = DateTime.Now,
                    Title = Path.GetFileName(_viewModel.AgentInputFilePath),
                    UserRequest = userMessage,
                    ResponseSummary = result.Explanation,
                    Command = result.Preview.DisplayCommand,
                    Status = "命令已生成"
                });
            }
            else
            {
                var reply = await _aiChatService.SendAsync(
                    _aiSettings,
                    conversation!,
                    CancellationToken.None);
                _viewModel.AppendAgentChat("");
                _viewModel.AppendAgentChat("AI: " + reply.Trim());
                _viewModel.AgentStatusText = "聊天完成。";
            }
        }
        catch (Exception ex)
        {
            _viewModel.AppendAgentChat("");
            _viewModel.AppendAgentChat("AI: " + ex.Message);
            _viewModel.AgentStatusText = "聊天失败。";
        }
        finally
        {
            _viewModel.IsAgentBusy = false;
            SaveHistorySnapshot();
        }
    }

    private IReadOnlyList<AiChatMessage> BuildConversationMessages(string userMessage)
    {
        var messages = new List<AiChatMessage>
        {
            new() { Role = "system", Content = "你是 ConvertXPortable 的转换助手。回答要围绕可执行的 Windows PowerShell 命令：如果能给命令，请用一条完整命令回答；如果是只读命令，明确标注只读；不要编造不存在的工具。"}
        };

        foreach (var bubble in _viewModel.AgentMessages)
        {
            if (string.IsNullOrWhiteSpace(bubble.Content))
            {
                continue;
            }

            messages.Add(new AiChatMessage
            {
                Role = bubble.IsUser ? "user" : "assistant",
                Content = bubble.Content
            });
        }

        messages.Add(new AiChatMessage { Role = "user", Content = userMessage });
        return messages;
    }

    private static string BuildAssistantCommandMessage(AiPlanResult result)
    {
        var safety = result.IsReadOnlyCommand ? "只读命令，可直接执行。" : "会写入或转换文件，执行前需要确认。";
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(result.Explanation))
        {
            builder.AppendLine(result.Explanation.Trim());
        }

        if (!string.IsNullOrWhiteSpace(result.Risk))
        {
            builder.AppendLine("注意: " + result.Risk.Trim());
        }

        builder.AppendLine(safety);
        builder.AppendLine("命令:");
        builder.Append(result.Preview.DisplayCommand);
        return builder.ToString();
    }

    private async Task RunGeneratedReadOnlyCommandAsync(string command)
    {
        _viewModel.AgentTerminalLog = "";
        _viewModel.AgentStatusText = "正在自动执行只读命令...";
        var exitCode = await ExecutePowerShellAsync(command, AppendAgentLogOnUiThread, CancellationToken.None);
        _viewModel.AgentStatusText = exitCode == 0 ? "只读命令执行完成。" : $"只读命令失败，退出码 {exitCode}。";
    }

    private static bool IsReadOnlyPowerShellCommand(string command)
    {
        var lowered = command.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(lowered))
        {
            return false;
        }

        var dangerousTokens = new[]
        {
            ">", ">>", "| set-", "| remove-", " remove-", " del ", " erase ", " rm ",
            "move-", "copy-", "new-", "set-content", "add-content", "out-file",
            "start-process", "invoke-webrequest", "curl ", "wget ", "install", "winget",
            "choco", "scoop", "mkdir", "md ", "rmdir", "rd ", "format"
        };
        if (dangerousTokens.Any(token => lowered.Contains(token, StringComparison.Ordinal)))
        {
            return false;
        }

        var readOnlyPrefixes = new[]
        {
            "get-", "dir", "ls", "pwd", "echo", "where", "where.exe", "test-path",
            "resolve-path", "select-string", "type", "cat", "ffprobe", "nvidia-smi"
        };
        return readOnlyPrefixes.Any(prefix => lowered.StartsWith(prefix, StringComparison.Ordinal));
    }

    private void RefreshAgentOutputFormats()
    {
        _updatingSelection = true;
        try
        {
            _viewModel.AgentOutputFormats.Clear();
            _viewModel.AgentSelectedOutputFormat = "";

            if (!File.Exists(_viewModel.AgentInputFilePath))
            {
                _viewModel.AgentStatusText = "请选择一个有效文件。";
                return;
            }

            var formats = _router.GetOutputFormats(_viewModel.AgentInputFilePath);
            foreach (var format in formats)
            {
                _viewModel.AgentOutputFormats.Add(format);
            }

            if (_viewModel.AgentOutputFormats.Count > 0)
            {
                _viewModel.AgentSelectedOutputFormat = _viewModel.AgentOutputFormats[0];
            }
        }
        finally
        {
            _updatingSelection = false;
        }
    }

    private void RefreshAgentRecommendationState()
    {
        if (File.Exists(_viewModel.AgentInputFilePath) &&
            Directory.Exists(_viewModel.AgentOutputDirectory) &&
            !string.IsNullOrWhiteSpace(_viewModel.AgentSelectedOutputFormat))
        {
            _viewModel.AgentStatusText = "可开始 AI 推荐。";
        }
    }

    private void SetAgentInputFile(string path)
    {
        _viewModel.AgentInputFilePath = path;
        if (string.IsNullOrWhiteSpace(_viewModel.AgentOutputDirectory) || !Directory.Exists(_viewModel.AgentOutputDirectory))
        {
            _viewModel.AgentOutputDirectory = Path.GetDirectoryName(path) ?? "";
        }
    }

    private void AppendAgentLogOnUiThread(string message)
    {
        Dispatcher.Invoke(() => _viewModel.AppendAgentTerminal(message));
    }

    private void LoadAiSettingsIntoViewModel()
    {
        _viewModel.AgentProviderType = _aiSettings.ProviderType;
        _viewModel.AgentEndpoint = _aiSettings.Endpoint;
        _viewModel.AgentApiKey = _aiSettings.ApiKey;
        _viewModel.AgentModel = _aiSettings.Model;
        _viewModel.EnableCommandLineExecution = _aiSettings.EnableCommandLineExecution;
        AgentApiKeyBox.Password = _aiSettings.ApiKey;
        UpdateAdvancedCommandUi();
    }

    private void SaveAiSettingsFromViewModel()
    {
        _aiSettings.ProviderType = _viewModel.AgentProviderType;
        _aiSettings.Endpoint = _viewModel.AgentEndpoint;
        _aiSettings.ApiKey = _viewModel.AgentApiKey;
        _aiSettings.Model = _viewModel.AgentModel;
        _aiSettings.EnableCommandLineExecution = _viewModel.EnableCommandLineExecution;
        _aiSettingsService.Save(_aiSettings);
    }

    private void LoadMcpSettingsIntoViewModel()
    {
        _viewModel.IsMcpEnabled = _mcpSettings.IsEnabled;
        _viewModel.McpPort = _mcpSettings.Port;
        _viewModel.McpRequireToken = _mcpSettings.RequireToken;
        _viewModel.McpToken = _mcpSettings.Token;
        _viewModel.McpDocsUrl = $"http://127.0.0.1:{_mcpSettings.Port}/docs";
        _viewModel.McpStatusText = _mcpSettings.IsEnabled ? "MCP 正在准备启动..." : "MCP 未启用。";
    }

    private void SaveMcpSettingsFromViewModel()
    {
        _mcpSettings.IsEnabled = _viewModel.IsMcpEnabled;
        _mcpSettings.Port = _viewModel.McpPort <= 0 ? 8765 : _viewModel.McpPort;
        _mcpSettings.RequireToken = _viewModel.McpRequireToken;
        _mcpSettings.Token = string.IsNullOrWhiteSpace(_viewModel.McpToken)
            ? McpSettingsService.GenerateToken()
            : _viewModel.McpToken;
        _viewModel.McpPort = _mcpSettings.Port;
        _viewModel.McpToken = _mcpSettings.Token;
        _viewModel.McpDocsUrl = $"http://127.0.0.1:{_mcpSettings.Port}/docs";
        _mcpSettingsService.Save(_mcpSettings);
    }

    private async void McpEnabled_Checked(object sender, RoutedEventArgs e)
    {
        _viewModel.IsMcpEnabled = true;
        await StartMcpAsync(showPrompt: true);
    }

    private void McpEnabled_Unchecked(object sender, RoutedEventArgs e)
    {
        _viewModel.IsMcpEnabled = false;
        StopMcp();
    }

    private async Task StartMcpAsync(bool showPrompt)
    {
        try
        {
            SaveMcpSettingsFromViewModel();
            _mcpSettings.IsEnabled = true;
            _mcpSettingsService.Save(_mcpSettings);
            _viewModel.McpStatusText = "MCP 正在启动...";
            await _mcpServer.StartAsync(_mcpSettings);
            _viewModel.McpDocsUrl = _mcpServer.DocsUrl;
            _viewModel.McpStatusText = $"MCP 运行中: {_mcpServer.BaseUrl}";
            _viewModel.IsMcpEnabled = true;

            if (showPrompt)
            {
                System.Windows.MessageBox.Show(
                    this,
                    $"MCP 已成功开启。\n\n接口文档: {_mcpServer.DocsUrl}",
                    "MCP 已开启",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            _mcpServer.Stop();
            _mcpSettings.IsEnabled = false;
            _mcpSettingsService.Save(_mcpSettings);
            _viewModel.IsMcpEnabled = false;
            _viewModel.McpStatusText = "MCP 启动失败: " + ex.Message;
        }
    }

    private void StopMcp()
    {
        _mcpServer.Stop();
        _mcpSettings.IsEnabled = false;
        _mcpSettingsService.Save(_mcpSettings);
        _viewModel.McpStatusText = "MCP 已关闭。";
    }

    private void McpSaveSettings_Click(object sender, RoutedEventArgs e)
    {
        var restart = _mcpServer.IsRunning && _viewModel.IsMcpEnabled;
        if (_mcpServer.IsRunning)
        {
            _mcpServer.Stop();
        }

        SaveMcpSettingsFromViewModel();
        if (restart || _viewModel.IsMcpEnabled)
        {
            _ = StartMcpAsync(showPrompt: false);
        }
        else
        {
            _viewModel.McpStatusText = "MCP 设置已保存。";
        }
    }

    private void McpRegenerateToken_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.McpToken = McpSettingsService.GenerateToken();
        SaveMcpSettingsFromViewModel();
        _viewModel.McpStatusText = "MCP token 已重新生成。";
    }

    private void McpOpenDocs_Click(object sender, RoutedEventArgs e)
    {
        var url = string.IsNullOrWhiteSpace(_viewModel.McpDocsUrl)
            ? $"http://127.0.0.1:{_viewModel.McpPort}/docs"
            : _viewModel.McpDocsUrl;
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    private void UpdateAdvancedCommandUi()
    {
        if (RunCommandButton is null)
        {
            return;
        }

        RunCommandButton.Visibility = _viewModel.EnableCommandLineExecution
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void AgentApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.PasswordBox box)
        {
            _viewModel.AgentApiKey = box.Password;
        }
    }

    private void AgentOpenOutputDirectory_Click(object sender, RoutedEventArgs e)
    {
        var directory = _viewModel.AgentOutputDirectory;
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

    private void LoadAiHistoryIntoViewModel()
    {
        _viewModel.AgentHistory.Clear();
        foreach (var item in _aiHistory.Items.OrderByDescending(item => item.CreatedAt))
        {
            _viewModel.AgentHistory.Add(item);
        }
    }

    private void AppendAiHistory(AiHistoryItem item)
    {
        _aiHistory.Items.Insert(0, item);
        _viewModel.AgentHistory.Insert(0, item);
        SaveHistorySnapshot();
    }

    private void SaveHistorySnapshot()
    {
        _aiHistoryService.Save(_aiHistory);
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
