using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ConvertXPortable.Models;

namespace ConvertXPortable.Services;

public sealed class AiChatService
{
    private readonly HttpClient _httpClient = new();
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<string> SendAsync(
        AiSettings settings,
        IReadOnlyList<AiChatMessage> messages,
        CancellationToken cancellationToken)
    {
        if (!settings.IsConfigured)
        {
            throw new InvalidOperationException("请先在设置中配置 AI 模型。");
        }

        return settings.ProviderType == AiProviderTypes.AnthropicCompatible
            ? await SendAnthropicCompatibleAsync(settings, messages, cancellationToken)
            : await SendOpenAiCompatibleAsync(settings, messages, cancellationToken);
    }

    public async Task TestConnectionAsync(AiSettings settings, CancellationToken cancellationToken)
    {
        var response = await SendAsync(settings, [new AiChatMessage
        {
            Role = "user",
            Content = "请只回复 ok"
        }], cancellationToken);

        if (string.IsNullOrWhiteSpace(response))
        {
            throw new InvalidOperationException("模型返回为空。");
        }
    }

    private async Task<string> SendOpenAiCompatibleAsync(
        AiSettings settings,
        IReadOnlyList<AiChatMessage> messages,
        CancellationToken cancellationToken)
    {
        var url = BuildEndpoint(settings.Endpoint, "chat/completions");
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);

        var body = new
        {
            model = settings.Model,
            messages = messages.Select(message => new { role = message.Role, content = message.Content }).ToArray(),
            temperature = 0.2
        };
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"AI 请求失败: {(int)response.StatusCode} {response.ReasonPhrase}\n{responseBody}");
        }

        using var json = JsonDocument.Parse(responseBody);
        var content = json.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();
        return content ?? "";
    }

    private async Task<string> SendAnthropicCompatibleAsync(
        AiSettings settings,
        IReadOnlyList<AiChatMessage> messages,
        CancellationToken cancellationToken)
    {
        var url = BuildEndpoint(settings.Endpoint, "messages");
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("x-api-key", settings.ApiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");

        var system = string.Join("\n\n", messages.Where(message => message.Role == "system").Select(message => message.Content));
        var userMessages = messages
            .Where(message => message.Role != "system")
            .Select(message => new { role = message.Role == "assistant" ? "assistant" : "user", content = message.Content })
            .ToArray();

        var body = new
        {
            model = settings.Model,
            max_tokens = 1600,
            temperature = 0.2,
            system,
            messages = userMessages
        };
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"AI 请求失败: {(int)response.StatusCode} {response.ReasonPhrase}\n{responseBody}");
        }

        using var json = JsonDocument.Parse(responseBody);
        var builder = new StringBuilder();
        foreach (var item in json.RootElement.GetProperty("content").EnumerateArray())
        {
            if (item.TryGetProperty("text", out var text))
            {
                builder.Append(text.GetString());
            }
        }

        return builder.ToString();
    }

    private static string BuildEndpoint(string endpoint, string suffix)
    {
        var trimmed = endpoint.Trim().TrimEnd('/');
        if (trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase) ||
            trimmed.EndsWith("/messages", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        if (trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            return $"{trimmed}/{suffix}";
        }

        return $"{trimmed}/v1/{suffix}";
    }
}
