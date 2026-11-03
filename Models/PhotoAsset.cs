namespace AiBrandContentAgent.Models;

public sealed class PhotoAsset
{
    public string Id { get; set; } = "";
    public string File { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    public List<string> Tags { get; set; } = [];
}
