using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace ConvertXPortable.Models;

public sealed class ToolCatalog
{
    [JsonPropertyName("tools")]
    public List<ToolDefinition> Tools { get; set; } = [];
}

public sealed class ToolDefinition
{
    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("mainExecutable")]
    public string MainExecutable { get; set; } = "";

    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("executables")]
    public List<string> Executables { get; set; } = [];
}

public sealed class ConversionRule
{
    public string Converter { get; set; } = "";
    public List<string> From { get; set; } = [];
    public List<string> To { get; set; } = [];
    public string Executable { get; set; } = "";
    public string ArgumentTemplate { get; set; } = "";
    public string Category { get; set; } = "";
    public int Priority { get; set; }
    public string Description { get; set; } = "";
    public bool WriteStdoutToOutput { get; set; }
    public bool PipeInputToStdin { get; set; }
    public Dictionary<string, string> OutputArgumentTemplates { get; set; } = [];
}

public sealed class ConversionManifest
{
    public List<ConversionRule> Conversions { get; set; } = [];
}

public sealed class ToolStatus
{
    public string Name { get; init; } = "";
    public string Category { get; init; } = "";
    public string ExecutablePath { get; init; } = "";
    public bool IsAvailable { get; init; }
    public string StatusText => IsAvailable ? "可用" : "缺失";
}

public sealed class ConverterOption
{
    public required ConversionRule Rule { get; init; }
    public required ToolStatus Tool { get; init; }
    public string DisplayName => $"{Rule.Converter} - {Rule.Description}";
}

public sealed class AppViewModel : INotifyPropertyChanged
{
    private string _inputFilePath = "";
    private string _outputDirectory = "";
    private string _advancedArguments = "";
    private string _selectedOutputFormat = "";
    private ConverterOption? _selectedConverterOption;
    private string _logText = "";
    private string _statusText = "请选择一个文件开始。";
    private bool _isConverting;
    private string _agentInputFilePath = "";
    private string _agentOutputDirectory = "";
    private string _agentSelectedOutputFormat = "";
    private string _agentRequestText = "";
    private string _agentChatInput = "";
    private string _agentChatLog = "";
    private string _agentCommandPreview = "";
    private string _agentRecommendation = "";
    private string _agentRiskText = "";
    private string _agentStatusText = "请选择一个文件并配置 AI 模型。";
    private string _agentTerminalLog = "";
    private string _agentCommandInput = "";
    private string _agentProviderType = AiProviderTypes.OpenAiCompatible;
    private string _agentEndpoint = "";
    private string _agentApiKey = "";
    private string _agentModel = "";
    private bool _isAgentBusy;
    private bool _enableCommandLineExecution;
    private bool _showChatEmptyState = true;
    private bool _isMcpEnabled;
    private bool _mcpRequireToken = true;
    private int _mcpPort = 8765;
    private string _mcpToken = "";
    private string _mcpStatusText = "MCP 未启用。";
    private string _mcpDocsUrl = "";
    private AiHistoryItem? _selectedAgentHistory;

    public AppViewModel()
    {
        AgentMessages.CollectionChanged += (_, _) =>
        {
            ShowChatEmptyState = AgentMessages.Count == 0;
        };
    }

    public ObservableCollection<string> OutputFormats { get; } = [];
    public ObservableCollection<ConverterOption> ConverterOptions { get; } = [];
    public ObservableCollection<ToolStatus> ToolStatuses { get; } = [];
    public ObservableCollection<string> AgentOutputFormats { get; } = [];
    public ObservableCollection<AiHistoryItem> AgentHistory { get; } = [];
    public ObservableCollection<AiChatBubble> AgentMessages { get; } = [];
    public ObservableCollection<string> AiProviderOptions { get; } = new(AiProviderTypes.All);

    public string RootStatusText { get; set; } = "";
    public string AvailableToolSummary { get; set; } = "";

    public string InputFilePath
    {
        get => _inputFilePath;
        set
        {
            if (SetField(ref _inputFilePath, value))
            {
                OnPropertyChanged(nameof(CanStart));
            }
        }
    }

    public string OutputDirectory
    {
        get => _outputDirectory;
        set
        {
            if (SetField(ref _outputDirectory, value))
            {
                OnPropertyChanged(nameof(CanStart));
            }
        }
    }

    public string AdvancedArguments
    {
        get => _advancedArguments;
        set => SetField(ref _advancedArguments, value);
    }

    public string SelectedOutputFormat
    {
        get => _selectedOutputFormat;
        set
        {
            if (SetField(ref _selectedOutputFormat, value))
            {
                OnPropertyChanged(nameof(CanStart));
            }
        }
    }

    public ConverterOption? SelectedConverterOption
    {
        get => _selectedConverterOption;
        set
        {
            if (SetField(ref _selectedConverterOption, value))
            {
                OnPropertyChanged(nameof(CanStart));
            }
        }
    }

    public string LogText
    {
        get => _logText;
        set => SetField(ref _logText, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetField(ref _statusText, value);
    }

    public bool IsConverting
    {
        get => _isConverting;
        set
        {
            if (SetField(ref _isConverting, value))
            {
                OnPropertyChanged(nameof(CanStart));
            }
        }
    }

    public string AgentInputFilePath
    {
        get => _agentInputFilePath;
        set
        {
            if (SetField(ref _agentInputFilePath, value))
            {
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(CanStartAgent));
            }
        }
    }

    public string AgentOutputDirectory
    {
        get => _agentOutputDirectory;
        set
        {
            if (SetField(ref _agentOutputDirectory, value))
            {
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(CanStartAgent));
            }
        }
    }

    public string AgentSelectedOutputFormat
    {
        get => _agentSelectedOutputFormat;
        set
        {
            if (SetField(ref _agentSelectedOutputFormat, value))
            {
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(CanStartAgent));
            }
        }
    }

    public string AgentRequestText
    {
        get => _agentRequestText;
        set => SetField(ref _agentRequestText, value);
    }

    public string AgentChatInput
    {
        get => _agentChatInput;
        set
        {
            if (SetField(ref _agentChatInput, value))
            {
                OnPropertyChanged(nameof(CanSendChat));
            }
        }
    }

    public string AgentChatLog
    {
        get => _agentChatLog;
        set => SetField(ref _agentChatLog, value);
    }

    public string AgentCommandPreview
    {
        get => _agentCommandPreview;
        set => SetField(ref _agentCommandPreview, value);
    }

    public string AgentRecommendation
    {
        get => _agentRecommendation;
        set => SetField(ref _agentRecommendation, value);
    }

    public string AgentRiskText
    {
        get => _agentRiskText;
        set => SetField(ref _agentRiskText, value);
    }

    public string AgentStatusText
    {
        get => _agentStatusText;
        set => SetField(ref _agentStatusText, value);
    }

    public string AgentTerminalLog
    {
        get => _agentTerminalLog;
        set => SetField(ref _agentTerminalLog, value);
    }

    public string AgentCommandInput
    {
        get => _agentCommandInput;
        set => SetField(ref _agentCommandInput, value);
    }

    public string AgentProviderType
    {
        get => _agentProviderType;
        set => SetField(ref _agentProviderType, value);
    }

    public string AgentEndpoint
    {
        get => _agentEndpoint;
        set => SetField(ref _agentEndpoint, value);
    }

    public string AgentApiKey
    {
        get => _agentApiKey;
        set => SetField(ref _agentApiKey, value);
    }

    public string AgentModel
    {
        get => _agentModel;
        set => SetField(ref _agentModel, value);
    }

    public bool IsAgentBusy
    {
        get => _isAgentBusy;
        set
        {
            if (SetField(ref _isAgentBusy, value))
            {
                OnPropertyChanged(nameof(CanStartAgent));
                OnPropertyChanged(nameof(CanSendChat));
            }
        }
    }

    public bool ShowChatEmptyState
    {
        get => _showChatEmptyState;
        private set => SetField(ref _showChatEmptyState, value);
    }

    public bool EnableCommandLineExecution
    {
        get => _enableCommandLineExecution;
        set => SetField(ref _enableCommandLineExecution, value);
    }

    public bool IsMcpEnabled
    {
        get => _isMcpEnabled;
        set => SetField(ref _isMcpEnabled, value);
    }

    public bool McpRequireToken
    {
        get => _mcpRequireToken;
        set => SetField(ref _mcpRequireToken, value);
    }

    public int McpPort
    {
        get => _mcpPort;
        set => SetField(ref _mcpPort, value);
    }

    public string McpToken
    {
        get => _mcpToken;
        set => SetField(ref _mcpToken, value);
    }

    public string McpStatusText
    {
        get => _mcpStatusText;
        set => SetField(ref _mcpStatusText, value);
    }

    public string McpDocsUrl
    {
        get => _mcpDocsUrl;
        set => SetField(ref _mcpDocsUrl, value);
    }

    public AiHistoryItem? SelectedAgentHistory
    {
        get => _selectedAgentHistory;
        set => SetField(ref _selectedAgentHistory, value);
    }

    public bool CanStart =>
        !IsConverting &&
        File.Exists(InputFilePath) &&
        Directory.Exists(OutputDirectory) &&
        !string.IsNullOrWhiteSpace(SelectedOutputFormat) &&
        SelectedConverterOption is { Tool.IsAvailable: true };

    public bool CanStartAgent =>
        !IsAgentBusy &&
        File.Exists(AgentInputFilePath) &&
        Directory.Exists(AgentOutputDirectory) &&
        !string.IsNullOrWhiteSpace(AgentSelectedOutputFormat);

    public bool CanSendChat =>
        !IsAgentBusy &&
        !string.IsNullOrWhiteSpace(AgentChatInput);

    public event PropertyChangedEventHandler? PropertyChanged;

    public void AppendLog(string message)
    {
        LogText += message.EndsWith(Environment.NewLine, StringComparison.Ordinal)
            ? message
            : message + Environment.NewLine;
    }

    public void AppendAgentTerminal(string message)
    {
        AgentTerminalLog += message.EndsWith(Environment.NewLine, StringComparison.Ordinal)
            ? message
            : message + Environment.NewLine;
    }

    public void AppendAgentChat(string message)
    {
        AgentChatLog += message.EndsWith(Environment.NewLine, StringComparison.Ordinal)
            ? message
            : message + Environment.NewLine;

        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        if (message.StartsWith("你: ", StringComparison.Ordinal))
        {
            AgentMessages.Add(new AiChatBubble { Role = "user", Content = message[3..].Trim() });
        }
        else if (message.StartsWith("AI: ", StringComparison.Ordinal))
        {
            AgentMessages.Add(new AiChatBubble { Role = "assistant", Content = message[4..].Trim() });
        }
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
