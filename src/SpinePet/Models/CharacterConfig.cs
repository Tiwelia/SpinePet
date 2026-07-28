using System.Text.Json.Serialization;

namespace SpinePet.Models;

public sealed class CharacterConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("D");
    public string Name { get; set; } = string.Empty;
    [JsonPropertyName("SkelPath")]
    public string SkeletonPath { get; set; } = string.Empty;
    public string AtlasPath { get; set; } = string.Empty;
    public string TexturePath { get; set; } = string.Empty;

    // Multi-page atlas characters can reference additional PNG files.
    [JsonPropertyName("ExtraTexturePaths")]
    public List<string> AdditionalTexturePaths { get; set; } = new();

    public string ResourceType { get; set; } =
        CharacterResourceTypes.Standing;

    public double PositionX { get; set; } = 200;
    public double PositionY { get; set; } = 200;
    public double Scale { get; set; } = 1.0;

    // Keep the existing JSON key so installed configurations migrate without data loss.
    [JsonPropertyName("CurrentAnimation")]
    public string ConfiguredAnimation { get; set; } = string.Empty;

    public double AnimationSpeed { get; set; } = 1.0;
    public bool Visible { get; set; } = true;
}
