namespace AiBrandContentAgent.Models;

public sealed class PostDraft
{
    public string Topic { get; set; } = "";
    public string Text { get; set; } = "";
    public List<string> Hashtags { get; set; } = [];
    public string PhotoId { get; set; } = "";
    public string ImageTitle { get; set; } = "";
    public string ImageSubtitle { get; set; } = "";
    public string ImagePath { get; set; } = "";
    public string CreatedAt { get; set; } = "";
}
