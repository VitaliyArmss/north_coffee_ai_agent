using System.Text.Json;
using System.Text.Json.Nodes;
using AiBrandContentAgent.Models;
using AiBrandContentAgent.Services;
using AiBrandContentAgent.Tools;

namespace AiBrandContentAgent.Agent;

public sealed class ContentAgent
{
    private const int MaxToolRounds = 10;
    private readonly GroqApiClient _groq;
    private readonly AgentTools _tools;
    private readonly JsonArray _toolDefinitions;
    private readonly string _systemPrompt;

    public ContentAgent(
        GroqApiClient groq,
        AgentTools tools,
        BrandConfig brand,
        TopicsConfig topics)
    {
        _groq = groq;
        _tools = tools;
        _toolDefinitions = CreateToolDefinitions();
        _systemPrompt = BuildSystemPrompt(brand, topics);
    }

    public async Task<string> RunAsync(string userRequest, CancellationToken cancellationToken)
    {
        var messages = new JsonArray
        {
            new JsonObject
            {
                ["role"] = "system",
                ["content"] = _systemPrompt
            },
            new JsonObject
            {
                ["role"] = "user",
                ["content"] = userRequest
            }
        };

        for (var round = 0; round < MaxToolRounds; round++)
        {
            //await Task.Delay(60 * 1000);
            var response = await _groq.CreateChatCompletionAsync(
                messages, _toolDefinitions, cancellationToken);

            var assistantMessage = response["choices"]?[0]?["message"] as JsonObject
                                   ?? throw new InvalidOperationException("В ответе Groq отсутствует message.");

            var toolCalls = assistantMessage["tool_calls"] as JsonArray;
            if (toolCalls is { Count: > 0 })
            {
                messages.Add(assistantMessage.DeepClone());

                foreach (var toolCallNode in toolCalls)
                {
                    if (toolCallNode is not JsonObject toolCall)
                        continue;

                    var toolCallId = toolCall["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N");
                    var function = toolCall["function"] as JsonObject;
                    var toolName = function?["name"]?.GetValue<string>() ?? "";
                    var arguments = function?["arguments"]?.GetValue<string>() ?? "{}";

                    Console.WriteLine($"[tool] {toolName}");
                    var toolResult = _tools.Execute(toolName, arguments);
                    messages.Add(new JsonObject
                    {
                        ["role"] = "tool",
                        ["tool_call_id"] = toolCallId,
                        ["name"] = toolName,
                        ["content"] = toolResult
                    });
                }

                if (_tools.PhotoListWasRequested && _tools.LastAvailablePhotoCount == 0)
                {
                    return "В папке assets/photos пока нет фотографий.\n" +
                           "Добавь туда изображения JPG или PNG и заполни assets/photos/photos.json. " +
                           "После этого повтори запрос.";
                }

                continue;
            }

            var content = assistantMessage["content"]?.GetValue<string>() ?? "";
            if (!string.IsNullOrWhiteSpace(_tools.SavedDraftPath))
            {
                return BuildUserResult(content);
            }

            // Просим модель завершить процесс, если она преждевременно ответила обычным текстом.
            messages.Add(assistantMessage.DeepClone());
            messages.Add(new JsonObject
            {
                ["role"] = "user",
                ["content"] = "Ты ещё не завершил задачу: нужно вызвать инструменты list_available_photos, " +
                              "render_brand_image и save_draft в правильном порядке. Продолжи работу через tools."
            });
        }

        throw new InvalidOperationException(
            "Агент не завершил работу за допустимое число шагов. Попробуй уточнить запрос или упростить тему.");
    }

    private string BuildUserResult(string modelText)
    {
        var fullDraftPath = Path.Combine(
            _tools.ProjectRoot,
            (_tools.SavedDraftPath ?? "").Replace('/', Path.DirectorySeparatorChar));

        // Сохраняемый JSON — источник истины: выводим именно те данные, которые прошли проверку.
        var draft = File.Exists(fullDraftPath)
            ? JsonSerializer.Deserialize<PostDraft>(File.ReadAllText(fullDraftPath), JsonDefaults.Options)
            : null;

        if (draft is null)
        {
            var fallback = string.IsNullOrWhiteSpace(modelText) ? "Пост подготовлен." : modelText.Trim();
            return fallback + Environment.NewLine + Environment.NewLine +
                   $"Черновик: {_tools.SavedDraftPath}" + Environment.NewLine +
                   $"Изображение: {_tools.LastRenderedImagePath}";
        }

        return $"Пост подготовлен: {draft.Topic}" + Environment.NewLine + Environment.NewLine +
               draft.Text + Environment.NewLine + Environment.NewLine +
               string.Join(" ", draft.Hashtags) + Environment.NewLine + Environment.NewLine +
               $"Заголовок на изображении: {draft.ImageTitle}" + Environment.NewLine +
               $"Черновик: {_tools.SavedDraftPath}" + Environment.NewLine +
               $"Изображение: {_tools.LastRenderedImagePath}";
    }

    private static string BuildSystemPrompt(BrandConfig brand, TopicsConfig topics)
    {
        var brandJson = JsonSerializer.Serialize(brand, JsonDefaults.Options);
        var topicsJson = JsonSerializer.Serialize(topics, JsonDefaults.Options);

        return $$"""
        Ты — AI-агент по подготовке публикаций для социальных сетей бренда {{brand.BrandName}}.
        Отвечай и пиши публикации только на русском языке.

        Данные о бренде (соблюдай их):
        {{brandJson}}

        Доступные рубрики и примеры тем:
        {{topicsJson}}

        Твоя задача по запросу пользователя:
        1. Определи тему и подходящую рубрику.
        2. ОБЯЗАТЕЛЬНО вызови list_available_photos и изучи описания, категории и теги.
        3. Сам выбери наиболее подходящую фотографию по смыслу запроса.
        4. Напиши текст публикации в Tone of Voice бренда. Не выдумывай цены, акции, адреса и факты.
        5. Подготовь короткий заголовок для изображения и, при необходимости, короткий подзаголовок.
        6. ОБЯЗАТЕЛЬНО вызови render_brand_image с выбранным photo_id, title и subtitle.
        7. После результата render_brand_image ОБЯЗАТЕЛЬНО вызови save_draft. Передай в image_path ТОЧНО путь, который вернул render_brand_image.
        8. Для save_draft подготовь от 3 до 5 релевантных хештегов.

        Правила работы с инструментами:
        - Не придумывай photo_id и пути к файлам. Используй только данные list_available_photos.
        - Вызывай инструменты последовательно: сначала список фото, потом создание картинки, потом сохранение.
        - Если инструмент вернул error, исправь аргументы и попробуй снова.
        - Не говори, что пост сохранён, пока save_draft не вернул success=true.
        - Не возвращай пользователю длинные рассуждения. После сохранения кратко покажи готовый текст поста и хештеги.
        """;
    }

    private static JsonArray CreateToolDefinitions()
    {
        const string json = """
        [
          {
            "type": "function",
            "function": {
              "name": "list_available_photos",
              "description": "Получить список фотографий, которые реально существуют в assets/photos, вместе с их описаниями и тегами.",
              "parameters": {
                "type": "object",
                "properties": {},
                "additionalProperties": false
              }
            }
          },
          {
            "type": "function",
            "function": {
              "name": "render_brand_image",
              "description": "Создать квадратную брендированную картинку: обрезать выбранную фотографию, разместить логотип и наложить заголовок с подзаголовком.",
              "parameters": {
                "type": "object",
                "properties": {
                  "photo_id": { "type": "string", "description": "ID фотографии из list_available_photos" },
                  "title": { "type": "string", "maxLength": 80 },
                  "subtitle": { "type": "string", "maxLength": 100 }
                },
                "required": ["photo_id", "title"],
                "additionalProperties": false
              }
            }
          },
          {
            "type": "function",
            "function": {
              "name": "save_draft",
              "description": "Проверить и сохранить готовый текст поста и путь к уже созданному изображению в JSON-файл.",
              "parameters": {
                "type": "object",
                "properties": {
                  "topic": { "type": "string", "maxLength": 120 },
                  "text": { "type": "string", "maxLength": 2500 },
                  "hashtags": { "type": "array", "items": { "type": "string" }, "minItems": 3, "maxItems": 5 },
                  "photo_id": { "type": "string" },
                  "image_title": { "type": "string", "maxLength": 80 },
                  "image_subtitle": { "type": "string", "maxLength": 100 },
                  "image_path": { "type": "string" }
                },
                "required": ["topic", "text", "hashtags", "photo_id", "image_title", "image_path"],
                "additionalProperties": false
              }
            }
          }
        ]
        """;

        return JsonNode.Parse(json)!.AsArray();
    }
}