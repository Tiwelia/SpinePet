namespace SpinePet.Models;

public class AppConfig
{
    public string Version { get; set; } = "1.0";
    public GlobalConfig Global { get; set; } = new();
    public List<CharacterConfig> Characters { get; set; } = new();
}

public class GlobalConfig
{
    public string WindowLevel { get; set; } = "normal_top";
    public bool AutoStart { get; set; } = false;
    public string Language { get; set; } = "zh-CN";
}