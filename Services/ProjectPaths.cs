namespace AiBrandContentAgent.Services;

public static class ProjectPaths
{
    public static string FindProjectRoot()
    {
        var starts = new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory };

        foreach (var start in starts)
        {
            var current = new DirectoryInfo(start);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "config", "brand.json")))
                    return current.FullName;

                current = current.Parent;
            }
        }

        throw new DirectoryNotFoundException(
            "Не удалось найти папку проекта. Запусти программу из корня ai-brand-content-agent.");
    }
}
