using System.Text.Json;
using System.Text.Json.Nodes;
using AiBrandContentAgent.Models;
using AiBrandContentAgent.Services;

namespace AiBrandContentAgent.Tools;

public sealed class AgentTools
{
    private readonly string _projectRoot;
    private readonly BrandConfig _brand;
    private readonly PhotoCatalog _photoCatalog;
    private readonly BrandImageRenderer _imageRenderer;

    public string ProjectRoot => _projectRoot;
    public bool PhotoListWasRequested { get; private set; }
    public int LastAvailablePhotoCount { get; private set; }
    public string? LastRenderedImagePath { get; private set; }
    public string? LastRenderedPhotoId { get; private set; }
    public string? SavedDraftPath { get; private set; }

    public AgentTools(string projectRoot, BrandConfig brand, PhotoCatalog photoCatalog)
    {
        _projectRoot = projectRoot;
        _brand = brand;
        _photoCatalog = photoCatalog;
        _imageRenderer = new BrandImageRenderer(projectRoot, brand);
    }

    public string Execute(string toolName, string argumentsJson)
    {
        try
        {
            var args = JsonNode.Parse(argumentsJson) as JsonObject
                       ?? throw new InvalidOperationException("Аргументы инструмента должны быть JSON-объектом.");

            return toolName switch
            {
                "list_available_photos" => ListAvailablePhotos(),
                "render_brand_image" => RenderBrandImage(args),
                "save_draft" => SaveDraft(args),
                _ => throw new InvalidOperationException($"Неизвестный инструмент: {toolName}")
            };
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message }, JsonDefaults.Options);
        }
    }

    private string ListAvailablePhotos()
    {
        PhotoListWasRequested = true;
        var photos = _photoCatalog.GetAvailablePhotos();
        LastAvailablePhotoCount = photos.Count;

        var result = photos.Select(photo => new
        {
            photo.Id,
            photo.File,
            photo.Description,
            photo.Category,
            photo.Tags
        });

        return JsonSerializer.Serialize(new
        {
            count = photos.Count,
            photos = result,
            note = photos.Count == 0
                ? "Нет доступных фото. Пользователь должен добавить файлы в assets/photos и описать их в photos.json."
                : null
        }, JsonDefaults.Options);
    }

    private string RenderBrandImage(JsonObject args)
    {
        if (!PhotoListWasRequested)
            throw new InvalidOperationException("Сначала вызови list_available_photos.");

        var photoId = RequiredString(args, "photo_id");
        var title = RequiredString(args, "title");
        var subtitle = OptionalString(args, "subtitle") ?? "";

        if (title.Length > 80 || subtitle.Length > 100)
            throw new InvalidOperationException("Заголовок должен быть не длиннее 80 символов, подзаголовок — 100.");

        var photo = _photoCatalog.GetRequiredPhoto(photoId);
        var photoPath = _photoCatalog.ResolvePhotoPath(photo.File);
        var relativeImagePath = _imageRenderer.Render(photoPath, title, subtitle);

        LastRenderedImagePath = relativeImagePath;
        LastRenderedPhotoId = photo.Id;

        return JsonSerializer.Serialize(new
        {
            success = true,
            image_path = relativeImagePath,
            photo_id = photo.Id,
            message = "Изображение создано. Передай тот же image_path в save_draft."
        }, JsonDefaults.Options);
    }

    private string SaveDraft(JsonObject args)
    {
        if (string.IsNullOrWhiteSpace(LastRenderedImagePath) ||
            string.IsNullOrWhiteSpace(LastRenderedPhotoId))
        {
            throw new InvalidOperationException("Сначала создай изображение через render_brand_image.");
        }

        var topic = RequiredString(args, "topic");
        var text = RequiredString(args, "text");
        var photoId = RequiredString(args, "photo_id");
        var imageTitle = RequiredString(args, "image_title");
        var imageSubtitle = OptionalString(args, "image_subtitle") ?? "";
        var imagePath = RequiredString(args, "image_path");

        if (topic.Length > 120 || text.Length > 2500 || imageTitle.Length > 80 || imageSubtitle.Length > 100)
            throw new InvalidOperationException("Превышена допустимая длина одного из текстовых полей.");

        if (!photoId.Equals(LastRenderedPhotoId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("photo_id не совпадает с фотографией, использованной при создании картинки.");

        if (!imagePath.Equals(LastRenderedImagePath, StringComparison.Ordinal))
            throw new InvalidOperationException("image_path должен в точности совпадать с путём, возвращённым render_brand_image.");

        if (!File.Exists(Path.Combine(_projectRoot, imagePath.Replace('/', Path.DirectorySeparatorChar))))
            throw new InvalidOperationException("Файл готового изображения не найден на диске.");

        var hashtagsNode = args["hashtags"] as JsonArray
                           ?? throw new InvalidOperationException("Поле hashtags должно быть массивом строк.");
        var hashtags = hashtagsNode
            .Select(item => item?.GetValue<string>()?.Trim() ?? "")
            .Where(item => item.Length > 0)
            .Select(item => item.StartsWith('#') ? item : "#" + item)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (hashtags.Count is < 3 or > 5)
            throw new InvalidOperationException("Нужно указать от 3 до 5 уникальных хештегов.");

        var draft = new PostDraft
        {
            Topic = topic,
            Text = text,
            Hashtags = hashtags,
            PhotoId = photoId,
            ImageTitle = imageTitle,
            ImageSubtitle = imageSubtitle,
            ImagePath = imagePath,
            CreatedAt = DateTimeOffset.Now.ToString("O")
        };

        var draftsDirectory = Path.Combine(_projectRoot, "drafts");
        Directory.CreateDirectory(draftsDirectory);
        var slug = MakeSafeFileName(topic);
        var fileName = $"{DateTime.Now:yyyyMMdd_HHmmss}_{slug}.json";
        var fullPath = Path.Combine(draftsDirectory, fileName);
        File.WriteAllText(fullPath, JsonSerializer.Serialize(draft, JsonDefaults.Options));

        SavedDraftPath = Path.GetRelativePath(_projectRoot, fullPath).Replace('\\', '/');
        return JsonSerializer.Serialize(new
        {
            success = true,
            draft_path = SavedDraftPath,
            image_path = LastRenderedImagePath,
            message = "Черновик успешно сохранён."
        }, JsonDefaults.Options);
    }

    private static string RequiredString(JsonObject args, string key)
    {
        var value = OptionalString(args, key);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Не заполнено обязательное поле '{key}'.");
        return value.Trim();
    }

    private static string? OptionalString(JsonObject args, string key)
    {
        try
        {
            return args[key]?.GetValue<string>();
        }
        catch (InvalidOperationException)
        {
            throw new InvalidOperationException($"Поле '{key}' должно быть строкой.");
        }
    }

    private static string MakeSafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value
            .Select(ch => invalid.Contains(ch) || char.IsWhiteSpace(ch) ? '_' : ch)
            .Where(ch => !char.IsControl(ch))
            .ToArray());

        return cleaned.Length > 40 ? cleaned[..40] : cleaned;
    }
}
