using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace AiBrandContentAgent.Services;

public sealed class GroqApiClient
{
    private readonly HttpClient _httpClient;
    private readonly string _model;

    public GroqApiClient(HttpClient httpClient, string apiKey, string model)
    {
        _httpClient = httpClient;
        _model = model;

        _httpClient.BaseAddress ??= new Uri("https://api.groq.com/openai/v1/");
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public async Task<JsonObject> CreateChatCompletionAsync(
        JsonArray messages,
        JsonArray tools,
        CancellationToken cancellationToken)
    {
        var requestBody = new JsonObject
        {
            ["model"] = _model,
            ["messages"] = messages.DeepClone(),
            ["tools"] = tools.DeepClone(),
            ["tool_choice"] = "auto",
            ["parallel_tool_calls"] = false,
            ["temperature"] = 0.7,
            ["max_completion_tokens"] = 1800
        };

        using var content = new StringContent(
            requestBody.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(
            "chat/completions", content, cancellationToken);

        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            //Console.WriteLine($"Ошибка при вызове Groq API: HTTP {(int)response.StatusCode}: {responseText}");
            //await Task.Delay(61 * 1000);
            //using var response1 = await _httpClient.PostAsync(
            //"chat/completions", content, cancellationToken);
            //var responseText1 = await response1.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Groq API вернул HTTP {(int)response.StatusCode}: {responseText}");
        }

        return JsonNode.Parse(responseText) as JsonObject
               ?? throw new InvalidOperationException("Groq вернул ответ в неожиданном формате.");
    }
}