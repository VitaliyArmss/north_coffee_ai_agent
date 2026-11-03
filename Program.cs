using System.Text;
using System.Text.Json;
using AiBrandContentAgent.Agent;
using AiBrandContentAgent.Models;
using AiBrandContentAgent.Services;
using AiBrandContentAgent.Tools;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

var projectRoot = ProjectPaths.FindProjectRoot();
DotEnv.Load(Path.Combine(projectRoot, ".env"));

var apiKey = Environment.GetEnvironmentVariable("GROQ_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.WriteLine("Не найден GROQ_API_KEY.");
    Console.WriteLine("Создай файл .env по примеру .env.example и добавь в него свой ключ Groq.");
    return;
}

var brand = LoadJson<BrandConfig>(Path.Combine(projectRoot, "config", "brand.json"));
var topics = LoadJson<TopicsConfig>(Path.Combine(projectRoot, "config", "topics.json"));
var photoCatalog = new PhotoCatalog(projectRoot);

Console.WriteLine("========================================");
Console.WriteLine($"   {brand.BrandName} — AI Content Agent");
Console.WriteLine("========================================");
Console.WriteLine($"Доступно фотографий: {photoCatalog.GetAvailablePhotos().Count}");
Console.WriteLine("Напиши, какой пост подготовить. Для выхода введи: выход");
Console.WriteLine();

using var httpClient = new HttpClient
{
    Timeout = TimeSpan.FromSeconds(120)
};

var model = Environment.GetEnvironmentVariable("GROQ_MODEL") ?? "openai/gpt-oss-120b";
var groqClient = new GroqApiClient(httpClient, apiKey, model);

while (true)
{
    Console.Write("Тема поста > ");
    var request = Console.ReadLine()?.Trim();

    if (string.IsNullOrWhiteSpace(request))
        continue;

    if (request.Equals("выход", StringComparison.OrdinalIgnoreCase) ||
        request.Equals("exit", StringComparison.OrdinalIgnoreCase))
    {
        break;
    }

    var tools = new AgentTools(projectRoot, brand, photoCatalog);
    var agent = new ContentAgent(groqClient, tools, brand, topics);

    try
    {
        var result = await agent.RunAsync(request, CancellationToken.None);
        Console.WriteLine();
        Console.WriteLine(result);
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine("Операция отменена.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Ошибка: {ex.Message}");
    }

    Console.WriteLine();
}

static T LoadJson<T>(string path)
{
    if (!File.Exists(path))
        throw new FileNotFoundException($"Не найден файл конфигурации: {path}");

    var json = File.ReadAllText(path, Encoding.UTF8);
    return JsonSerializer.Deserialize<T>(json, JsonDefaults.Options)
           ?? throw new InvalidOperationException($"Не удалось прочитать JSON: {path}");
}
