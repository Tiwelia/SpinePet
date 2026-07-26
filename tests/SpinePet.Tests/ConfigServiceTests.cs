using System.IO;
using SpinePet.Models;
using SpinePet.Services;

namespace SpinePet.Tests;

public sealed class ConfigServiceTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SpinePet.Tests",
        Guid.NewGuid().ToString("N"));

    public ConfigServiceTests()
    {
        Directory.CreateDirectory(_temporaryDirectory);
    }

    [Fact]
    public void SaveAndLoadRoundTripsConfiguration()
    {
        string configPath = Path.Combine(_temporaryDirectory, "config.json");
        ConfigService service = new(configPath);
        AppConfig expected = new()
        {
            Global = new GlobalConfig
            {
                AllowRenderDrag = false
            },
            Characters =
            [
                new CharacterConfig
                {
                    Id = "character-1",
                    Name = "Test Character",
                    AnimationSpeed = 1.25,
                    ConfiguredAnimation = "idle"
                }
            ]
        };

        service.Save(expected);
        AppConfig actual = service.Load();

        Assert.False(actual.Global.AllowRenderDrag);
        CharacterConfig character = Assert.Single(actual.Characters);
        Assert.Equal("character-1", character.Id);
        Assert.Equal("Test Character", character.Name);
        Assert.Equal(1.25, character.AnimationSpeed);
        Assert.Equal("idle", character.ConfiguredAnimation);
        Assert.False(File.Exists($"{configPath}.tmp"));
    }

    [Fact]
    public void LoadNormalizesLegacyAndInvalidValues()
    {
        string configPath = Path.Combine(_temporaryDirectory, "legacy.json");
        File.WriteAllText(
            configPath,
            """
            {
              "Version": "1.0",
              "Global": {
                "AllowRenderDrag": true
              },
              "Characters": [
                {
                  "Id": "legacy",
                  "CurrentAnimation": null,
                  "AnimationSpeed": 0,
                  "ExtraTexturePaths": null
                }
              ]
            }
            """);

        ConfigService service = new(configPath);
        AppConfig config = service.Load();

        Assert.Equal(AppConfig.CurrentVersion, config.Version);
        CharacterConfig character = Assert.Single(config.Characters);
        Assert.Equal(string.Empty, character.ConfiguredAnimation);
        Assert.Equal(1.0, character.AnimationSpeed);
        Assert.Empty(character.AdditionalTexturePaths);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }
}
