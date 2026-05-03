using System.Text.Json.Serialization;

namespace ConvertXPortable.Models;

public sealed class McpSettings
{
    public bool IsEnabled { get; set; }
    public int Port { get; set; } = 8765;
    public bool RequireToken { get; set; } = true;
    public string Token { get; set; } = "";
}

public sealed class McpConvertRequest
{
    public string InputPath { get; set; } = "";
    public string OutputDirectory { get; set; } = "";
    public string OutputFormat { get; set; } = "";
    public string Converter { get; set; } = "";
    public string AdvancedArguments { get; set; } = "";
}

public sealed class McpJob
{
    private readonly object _syncRoot = new();
    private readonly List<string> _logLines = [];

    public string JobId { get; init; } = Guid.NewGuid().ToString("N");
    public string Status { get; set; } = "queued";
    public string InputPath { get; init; } = "";
    public string OutputPath { get; init; } = "";
    public string OutputDirectory { get; init; } = "";
    public string OutputFormat { get; init; } = "";
    public string Converter { get; init; } = "";
    public int? ExitCode { get; set; }
    public string ErrorMessage { get; set; } = "";
    public DateTime CreatedAt { get; init; } = DateTime.Now;
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }

    [JsonIgnore]
    public CancellationTokenSource Cancellation { get; } = new();

    public void AppendLog(string message)
    {
        lock (_syncRoot)
        {
            _logLines.Add(message);
        }
    }

    public string GetLog()
    {
        lock (_syncRoot)
        {
            return string.Join(Environment.NewLine, _logLines);
        }
    }

    public string GetLogSummary()
    {
        lock (_syncRoot)
        {
            return string.Join(Environment.NewLine, _logLines.TakeLast(24));
        }
    }
}
