namespace SpinePet.Models;

public sealed class AppConfig
{
    public const string CurrentVersion = "1.4";

    public string Version { get; set; } = CurrentVersion;
    public GlobalConfig Global { get; set; } = new();
    public List<CharacterConfig> Characters { get; set; } = new();
}

public sealed class GlobalConfig
{
    public bool AllowRenderDrag { get; set; } = true;
}
