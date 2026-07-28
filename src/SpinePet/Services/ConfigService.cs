using System.IO;
using System.Text.Json;
using System.Windows;
using SpinePet.Infrastructure;
using SpinePet.Models;

namespace SpinePet.Services;

public sealed class ConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _configPath;

    public ConfigService(string? configPath = null)
    {
        _configPath = configPath ?? AppPaths.ConfigFile;
    }

    public AppConfig Load()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                string json = File.ReadAllText(_configPath);
                AppConfig config =
                    JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
                Normalize(config);
                return config;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Write(nameof(ConfigService), $"load-failed message={ex.Message}");
        }

        return new AppConfig();
    }

    internal static void Normalize(AppConfig config)
    {
        bool migrateLegacyPositions =
            !string.Equals(
                config.Version,
                AppConfig.CurrentVersion,
                StringComparison.Ordinal);
        config.Version = AppConfig.CurrentVersion;
        config.Global ??= new GlobalConfig();
        config.Characters ??= new List<CharacterConfig>();

        foreach (var character in config.Characters)
        {
            character.ConfiguredAnimation ??= string.Empty;
            character.AdditionalTexturePaths ??= new List<string>();
            character.ResourceType =
                CharacterResourceTypes.Normalize(character.ResourceType);
            if (!double.IsFinite(character.AnimationSpeed) || character.AnimationSpeed <= 0)
            {
                character.AnimationSpeed = 1.0;
            }

            character.AnimationSpeed = Math.Clamp(character.AnimationSpeed, 0.1, 2.0);
        }

        if (migrateLegacyPositions)
        {
            Rect workArea = SystemParameters.WorkArea;
            double centerX = workArea.Left + workArea.Width / 2;
            double feetY = workArea.Bottom - 24;
            CharacterConfig[] visibleCharacters = config.Characters
                .Where(character => character.Visible)
                .ToArray();
            for (int index = 0;
                 index < visibleCharacters.Length;
                 index++)
            {
                visibleCharacters[index].PositionX =
                    workArea.Left +
                    workArea.Width *
                    (index + 1) /
                    (visibleCharacters.Length + 1);
                visibleCharacters[index].PositionY = feetY;
            }

            foreach (CharacterConfig character in config.Characters)
            {
                if (!character.Visible)
                {
                    character.PositionX = centerX;
                    character.PositionY = feetY;
                }
            }
        }
    }

    public void Save(AppConfig config)
    {
        string temporaryPath = $"{_configPath}.tmp";
        try
        {
            Normalize(config);
            string? directory = Path.GetDirectoryName(_configPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json = JsonSerializer.Serialize(config, JsonOptions);
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, _configPath, overwrite: true);
        }
        catch (Exception ex)
        {
            AppLogger.Write(nameof(ConfigService), $"save-failed message={ex.Message}");
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    private static void TryDeleteTemporaryFile(string temporaryPath)
    {
        try
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
        catch (Exception exception)
        {
            AppLogger.Write(
                nameof(ConfigService),
                $"temporary-file-cleanup-failed message={exception.Message}");
        }
    }
}
