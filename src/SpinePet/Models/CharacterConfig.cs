namespace SpinePet.Models;

public class CharacterConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string SkelPath { get; set; } = "";
    public string AtlasPath { get; set; } = "";
    public string TexturePath { get; set; } = "";
    // 多页纹理：Nayuta 等角色有多张 PNG (c223_01.png, c223_01_2.png, ...)
    public List<string> ExtraTexturePaths { get; set; } = new();
    public double PositionX { get; set; } = 200;
    public double PositionY { get; set; } = 200;
    public double Scale { get; set; } = 1.0;
    public string CurrentAnimation { get; set; } = "";
    public bool Visible { get; set; } = true;
}
