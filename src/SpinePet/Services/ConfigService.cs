using System.IO;
using System.Text.Json;
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
        config.Version = AppConfig.CurrentVersion;
        config.Global ??= new GlobalConfig();
        config.Characters ??= new List<CharacterConfig>();

        foreach (var character in config.Characters)
        {
            character.ConfiguredAnimation ??= string.Empty;
            character.AdditionalTexturePaths ??= new List<string>();
            if (!double.IsFinite(character.AnimationSpeed) || character.AnimationSpeed <= 0)
            {
                character.AnimationSpeed = 1.0;
            }

            character.AnimationSpeed = Math.Clamp(character.AnimationSpeed, 0.1, 2.0);
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
