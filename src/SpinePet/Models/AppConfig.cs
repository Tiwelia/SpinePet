namespace SpinePet.Models;

public sealed class AppConfig
{
    public const string CurrentVersion = "1.1";

    public string Version { get; set; } = CurrentVersion;
    public GlobalConfig Global { get; set; } = new();
    public List<CharacterConfig> Characters { get; set; } = new();
}

public sealed class GlobalConfig
{
    public string WindowLevel { get; set; } = "normal_top";
    public bool AutoStart { get; set; }
    public string Language { get; set; } = "zh-CN";
    public bool AllowRenderDrag { get; set; } = true;
}
