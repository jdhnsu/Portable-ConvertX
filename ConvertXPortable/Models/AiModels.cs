using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace ConvertXPortable.Models;

public static class AiProviderTypes
{
    public const string OpenAiCompatible = "OpenAI Compatible";
    public const string AnthropicCompatible = "Anthropic Compatible";

    public static readonly string[] All = [OpenAiCompatible, AnthropicCompatible];
}

public sealed class AiSettings
{
    public string ProviderType { get; set; } = AiProviderTypes.OpenAiCompatible;
    public string Endpoint { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "";
    public bool EnableCommandLineExecution { get; set; }

    [JsonIgnore]
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Endpoint) &&
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(Model);
}

public sealed class AiChatMessage
{
    public string Role { get; set; } = "";
    public string Content { get; set; } = "";
}

public sealed class AiChatBubble
{
    public string Role { get; set; } = "";
    public string Content { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public bool IsUser => string.Equals(Role, "user", StringComparison.OrdinalIgnoreCase);
    public string DisplayTime => CreatedAt.ToString("HH:mm");
}

public sealed class AiRecommendation
{
    public string Converter { get; set; } = "";
    public string AdvancedArguments { get; set; } = "";
    public string PowerShellCommand { get; set; } = "";
    public bool IsReadOnlyCommand { get; set; }
    public string Explanation { get; set; } = "";
    public string Risk { get; set; } = "";
}

public sealed class CommandPreview
{
    public string Converter { get; init; } = "";
    public string ExecutablePath { get; init; } = "";
    public IReadOnlyList<string> Arguments { get; init; } = [];
    public string DisplayCommand { get; init; } = "";
    public string OutputPath { get; init; } = "";
    public string AdvancedArguments { get; init; } = "";
}

public sealed class AiPlanResult
{
    public required ConverterOption Option { get; init; }
    public required CommandPreview Preview { get; init; }
    public string Explanation { get; init; } = "";
    public string Risk { get; init; } = "";
    public bool IsReadOnlyCommand { get; init; }
    public string RawResponse { get; init; } = "";
}

public sealed class AiHistoryItem
{
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string Title { get; set; } = "";
    public string UserRequest { get; set; } = "";
    public string ResponseSummary { get; set; } = "";
    public string Command { get; set; } = "";
    public string Status { get; set; } = "";

    [JsonIgnore]
    public string DisplayLabel
    {
        get
        {
            var title = string.IsNullOrWhiteSpace(Title) ? "未命名" : Title;
            var status = string.IsNullOrWhiteSpace(Status) ? "" : "  ·  " + Status;
            return $"{CreatedAt:MM-dd HH:mm}    {title}{status}";
        }
    }
}

public sealed class AiHistoryDocument
{
    public ObservableCollection<AiHistoryItem> Items { get; set; } = [];
}
