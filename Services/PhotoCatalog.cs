using System.Text.Json;
using AiBrandContentAgent.Models;

namespace AiBrandContentAgent.Services;

public sealed class PhotoCatalog
{
    private readonly string _root;
    private readonly string _photosDirectory;
    private readonly List<PhotoAsset> _photos;

    public PhotoCatalog(string projectRoot)
    {
        _root = Path.GetFullPath(projectRoot);
        _photosDirectory = Path.GetFullPath(Path.Combine(_root, "assets", "photos"));
        var metadataPath = Path.Combine(_photosDirectory, "photos.json");

        if (!File.Exists(metadataPath))
        {
            _photos = [];
            return;
        }

        _photos = JsonSerializer.Deserialize<List<PhotoAsset>>(
            File.ReadAllText(metadataPath), JsonDefaults.Options) ?? [];
    }

    public List<PhotoAsset> GetAvailablePhotos()
    {
        return _photos
            .Where(photo => !string.IsNullOrWhiteSpace(photo.Id) &&
                            !string.IsNullOrWhiteSpace(photo.File) &&
                            File.Exists(ResolvePhotoPath(photo.File)))
            .ToList();
    }

    public PhotoAsset GetRequiredPhoto(string photoId)
    {
        var photo = GetAvailablePhotos().FirstOrDefault(
            item => item.Id.Equals(photoId, StringComparison.OrdinalIgnoreCase));

        return photo ?? throw new InvalidOperationException(
            $"Фотография с ID '{photoId}' не найдена или её файл отсутствует в assets/photos.");
    }

    public string ResolvePhotoPath(string fileName)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_photosDirectory, fileName));
        var relative = Path.GetRelativePath(_photosDirectory, fullPath);

        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Путь к фотографии выходит за пределы assets/photos.");
        }

        return fullPath;
    }
}
