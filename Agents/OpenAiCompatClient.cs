using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PokerApp;

public sealed class OpenAiCompatClient
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly double _temperature;

    public OpenAiCompatClient(string baseUrl, string apiKey, string model, double temperature)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _apiKey = apiKey;
        _model = model;
        _temperature = temperature;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    }

    public async Task<string?> CompleteChatAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        var payload = new ChatPayload
        {
            Model = _model,
            Temperature = _temperature,
            Stream = false,
            ReasoningEffort = "none",
            Messages =
            [
                new MessageDto { Role = "system", Content = systemPrompt },
                new MessageDto { Role = "user", Content = userPrompt }
            ]
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/chat/completions");
        if (!string.IsNullOrWhiteSpace(_apiKey))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req, cancellationToken);
        if (!resp.IsSuccessStatusCode)
            return null;

        using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            return null;

        var msg = choices[0].GetProperty("message");
        return msg.TryGetProperty("content", out var content) ? content.GetString() : null;
    }

    private sealed class ChatPayload
    {
        [JsonPropertyName("model")]
        public string Model { get; init; } = "";

        [JsonPropertyName("temperature")]
        public double Temperature { get; init; }

        [JsonPropertyName("stream")]
        public bool Stream { get; init; }

        [JsonPropertyName("reasoning_effort")]
        public string ReasoningEffort { get; init; } = "none";

        [JsonPropertyName("messages")]
        public MessageDto[] Messages { get; init; } = [];
    }

    private sealed class MessageDto
    {
        [JsonPropertyName("role")]
        public string Role { get; init; } = "";

        [JsonPropertyName("content")]
        public string Content { get; init; } = "";
    }
}
