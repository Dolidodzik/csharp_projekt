using System.Net.Http.Headers;
using System.Net;
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

        var maxAttempts = 10;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/chat/completions");
                if (!string.IsNullOrWhiteSpace(_apiKey))
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
                req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                using var resp = await _http.SendAsync(req, cancellationToken);
                if (!resp.IsSuccessStatusCode)
                {
                    var body = await SafeReadBodyAsync(resp, cancellationToken);
                    Log($"LLM API attempt {attempt}/{maxAttempts} failed: HTTP {(int)resp.StatusCode} {resp.ReasonPhrase ?? ""} body={TrimOneLine(body, 380)}");
                    if (attempt < maxAttempts)
                    {
                        if (resp.StatusCode == HttpStatusCode.TooManyRequests)
                        {
                            Log("HTTP 429 received, sleeping 6000ms before retry.");
                            await Task.Delay(6000, cancellationToken);
                        }
                        else
                        {
                            await Task.Delay(BackoffDelayMs(attempt), cancellationToken);
                        }
                    }
                    continue;
                }

                using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                {
                    Log($"LLM API attempt {attempt}/{maxAttempts} returned no choices.");
                    if (attempt < maxAttempts)
                        await Task.Delay(BackoffDelayMs(attempt), cancellationToken);
                    continue;
                }

                var msg = choices[0].GetProperty("message");
                var content = msg.TryGetProperty("content", out var c) ? c.GetString() : null;
                if (string.IsNullOrWhiteSpace(content))
                {
                    Log($"LLM API attempt {attempt}/{maxAttempts} returned empty content.");
                    if (attempt < maxAttempts)
                        await Task.Delay(BackoffDelayMs(attempt), cancellationToken);
                    continue;
                }

                return content;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log($"LLM API attempt {attempt}/{maxAttempts} threw {ex.GetType().Name}: {TrimOneLine(ex.Message, 380)}");
                if (attempt < maxAttempts)
                    await Task.Delay(BackoffDelayMs(attempt), cancellationToken);
            }
        }

        Log("LLM API giving up after 5 attempts (no content).");
        return null;
    }

    private static int BackoffDelayMs(int attempt) =>
        attempt switch
        {
            1 => 400,
            2 => 900,
            _ => 1600
        };

    private static void Log(string message)
    {
        try
        {
            Console.Error.WriteLine($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] {message}");
        }
        catch
        {
        }
    }

    private static async Task<string> SafeReadBodyAsync(HttpResponseMessage resp, CancellationToken cancellationToken)
    {
        try
        {
            return await resp.Content.ReadAsStringAsync(cancellationToken);
        }
        catch
        {
            return "";
        }
    }

    private static string TrimOneLine(string? text, int maxChars)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        var oneLine = text.Replace("\r", " ").Replace("\n", " ").Trim();
        if (oneLine.Length <= maxChars)
            return oneLine;
        return oneLine[..maxChars] + "...";
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
