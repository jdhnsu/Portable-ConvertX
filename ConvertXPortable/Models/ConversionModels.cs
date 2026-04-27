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

    public ObservableCollection<string> OutputFormats { get; } = [];
    public ObservableCollection<ConverterOption> ConverterOptions { get; } = [];
    public ObservableCollection<ToolStatus> ToolStatuses { get; } = [];

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

    public bool CanStart =>
        !IsConverting &&
        File.Exists(InputFilePath) &&
        Directory.Exists(OutputDirectory) &&
        !string.IsNullOrWhiteSpace(SelectedOutputFormat) &&
        SelectedConverterOption is { Tool.IsAvailable: true };

    public event PropertyChangedEventHandler? PropertyChanged;

    public void AppendLog(string message)
    {
        LogText += message.EndsWith(Environment.NewLine, StringComparison.Ordinal)
            ? message
            : message + Environment.NewLine;
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
