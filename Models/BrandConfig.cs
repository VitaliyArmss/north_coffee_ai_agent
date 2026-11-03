namespace AiBrandContentAgent.Models;

public sealed class BrandConfig
{
    public string BrandName { get; set; } = "NORTH Coffee";
    public string Description { get; set; } = "Городская кофейня с авторскими напитками и уютной атмосферой.";
    public string ToneOfVoice { get; set; } = "Дружелюбный, тёплый, современный, без навязчивой рекламы.";
    public List<string> StyleRules { get; set; } = [];
    public List<string> ForbiddenPhrases { get; set; } = [];
    public string FontFamily { get; set; } = "Arial";
    public string LogoPath { get; set; } = "assets/logo.png";
}
