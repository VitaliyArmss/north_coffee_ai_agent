using AiBrandContentAgent.Models;
using SkiaSharp;

namespace AiBrandContentAgent.Services;

public sealed class BrandImageRenderer
{
    private const int CanvasSize = 1080;
    private readonly string _projectRoot;
    private readonly BrandConfig _brand;

    public BrandImageRenderer(string projectRoot, BrandConfig brand)
    {
        _projectRoot = projectRoot;
        _brand = brand;
    }

    public string Render(string photoPath, string title, string subtitle)
    {
        using var photo = SKBitmap.Decode(photoPath)
                          ?? throw new InvalidOperationException("Не удалось открыть исходную фотографию.");

        using var surface = SKSurface.Create(new SKImageInfo(
            CanvasSize, CanvasSize, SKColorType.Rgba8888, SKAlphaType.Premul));

        if (surface is null)
            throw new InvalidOperationException("Не удалось создать холст для изображения.");

        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Black);
        DrawCenterCrop(canvas, photo);
        DrawBottomOverlay(canvas);
        DrawLogo(canvas);
        DrawCaptions(canvas, title, subtitle);

        var outputDirectory = Path.Combine(_projectRoot, "drafts", "images");
        Directory.CreateDirectory(outputDirectory);

        var fileName = $"post_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..6]}.png";
        var outputPath = Path.Combine(outputDirectory, fileName);

        using var image = surface.Snapshot();
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100)
                            ?? throw new InvalidOperationException("Не удалось закодировать итоговое изображение.");
        using (var stream = File.Create(outputPath))
        {
            encoded.SaveTo(stream);
        }

        return Path.GetRelativePath(_projectRoot, outputPath).Replace('\\', '/');
    }

    private static void DrawCenterCrop(SKCanvas canvas, SKBitmap photo)
    {
        SKRect source;
        if (photo.Width > photo.Height)
        {
            var cropWidth = photo.Height;
            var left = (photo.Width - cropWidth) / 2f;
            source = new SKRect(left, 0, left + cropWidth, photo.Height);
        }
        else
        {
            var cropHeight = photo.Width;
            var top = (photo.Height - cropHeight) / 2f;
            source = new SKRect(0, top, photo.Width, top + cropHeight);
        }

        var destination = new SKRect(0, 0, CanvasSize, CanvasSize);
        using var paint = new SKPaint { IsAntialias = true };
        canvas.DrawBitmap(photo, source, destination, paint);
    }

    private static void DrawBottomOverlay(SKCanvas canvas)
    {
        using var paint = new SKPaint
        {
            Color = new SKColor(12, 18, 17, 190),
            IsAntialias = true
        };
        canvas.DrawRect(new SKRect(0, 690, CanvasSize, CanvasSize), paint);
    }

    private void DrawLogo(SKCanvas canvas)
    {
        var logoPath = Path.GetFullPath(Path.Combine(_projectRoot, _brand.LogoPath));
        if (!File.Exists(logoPath))
            return;

        using var logo = SKBitmap.Decode(logoPath);
        if (logo is null || logo.Width <= 0 || logo.Height <= 0)
            return;

        const float maxWidth = 250;
        const float maxHeight = 105;
        var scale = Math.Min(maxWidth / logo.Width, maxHeight / logo.Height);
        var width = logo.Width * scale;
        var height = logo.Height * scale;
        var left = 55f;
        var top = 45f;

        using var badgePaint = new SKPaint
        {
            Color = new SKColor(255, 255, 255, 225),
            IsAntialias = true
        };
        canvas.DrawRoundRect(
            new SKRect(left - 18, top - 14, left + width + 18, top + height + 14),
            18, 18, badgePaint);

        using var imagePaint = new SKPaint { IsAntialias = true };
        canvas.DrawBitmap(logo, new SKRect(left, top, left + width, top + height), imagePaint);
    }

    private void DrawCaptions(SKCanvas canvas, string title, string subtitle)
    {
        using var typeface = SKTypeface.FromFamilyName(_brand.FontFamily);
        using var titleFont = new SKFont(typeface, 62) { Embolden = true };
        using var titlePaint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true
        };

        var titleLines = WrapText(title, titleFont, titlePaint, 950);
        while (titleLines.Count > 2 && titleFont.Size > 42)
        {
            titleFont.Size -= 4;
            titleLines = WrapText(title, titleFont, titlePaint, 950);
        }

        var y = 825f;
        foreach (var line in titleLines.Take(2))
        {
            canvas.DrawText(line, 65, y, SKTextAlign.Left, titleFont, titlePaint);
            y += titleFont.Size * 1.18f;
        }

        if (string.IsNullOrWhiteSpace(subtitle))
            return;

        using var subtitleFont = new SKFont(typeface, 31);
        using var subtitlePaint = new SKPaint
        {
            Color = new SKColor(245, 239, 229),
            IsAntialias = true
        };

        var subtitleLines = WrapText(subtitle, subtitleFont, subtitlePaint, 950);
        var subtitleY = Math.Min(y + 12, 985);
        foreach (var line in subtitleLines.Take(2))
        {
            canvas.DrawText(line, 67, subtitleY, SKTextAlign.Left, subtitleFont, subtitlePaint);
            subtitleY += 39;
        }
    }

    private static List<string> WrapText(string text, SKFont font, SKPaint paint, float maxWidth)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var current = "";

        foreach (var word in words)
        {
            var candidate = string.IsNullOrEmpty(current) ? word : $"{current} {word}";
            if (font.MeasureText(candidate, paint) <= maxWidth || string.IsNullOrEmpty(current))
            {
                current = candidate;
            }
            else
            {
                lines.Add(current);
                current = word;
            }
        }

        if (!string.IsNullOrEmpty(current))
            lines.Add(current);

        return lines;
    }
}
