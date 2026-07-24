using System.IO;
using SpinePet.Models;
using SpinePet.Views;

namespace SpinePet.Services;

public class PetManager
{
    private readonly ConfigService _configService;
    private readonly RenderHostWindow _renderHost;
    private AppConfig _config;

    public PetManager(ConfigService configService)
    {
        _configService = configService;
        _config = configService.Load();
        _renderHost = new RenderHostWindow();
        _renderHost.CharacterScaleChanged += (id, maxScale, currentScale) =>
        {
            var c = _config.Characters.FirstOrDefault(c => c.Id == id);
            if (c != null)
                c.Scale = currentScale;
            CharacterScaleChanged?.Invoke(id, maxScale, currentScale);
        };
        _renderHost.CharacterAnimationsLoaded += (id, animations) =>
        {
            var c = _config.Characters.FirstOrDefault(c => c.Id == id);
            if (c != null && string.IsNullOrWhiteSpace(c.CurrentAnimation) && animations.Count > 0)
                c.CurrentAnimation = animations[0];
            CharactersChanged?.Invoke();
        };
        _renderHost.CharacterLoadFailed += id =>
        {
            var c = _config.Characters.FirstOrDefault(c => c.Id == id);
            if (c != null)
                c.Visible = false;
            CharactersChanged?.Invoke();
        };
        _renderHost.CharactersStateChanged += () => CharactersChanged?.Invoke();
    }

    public IReadOnlyList<CharacterConfig> Characters => _config.Characters;

    public event Action? CharactersChanged;
    public event Action<string, double, double>? CharacterScaleChanged;

    public RenderHostWindow RenderHost => _renderHost;

    public void AddCharacter(string skelPath, string atlasPath, string texturePath)
    {
        var name = Path.GetFileNameWithoutExtension(skelPath);

        var existing = _config.Characters.FirstOrDefault(c => c.SkelPath == skelPath);
        if (existing != null) return;

        var character = new CharacterConfig
        {
            Name = name,
            SkelPath = skelPath,
            AtlasPath = atlasPath,
            TexturePath = texturePath,
            PositionX = 300 + new Random().Next(400),
            PositionY = 100 + new Random().Next(300),
        };
        _config.Characters.Add(character);
        _configService.Save(_config);
        CharactersChanged?.Invoke();
    }

    public async void ShowCharacter(CharacterConfig character)
    {
        character.Visible = true;
        await _renderHost.ShowCharacterAsync(character, configMode: true, speed: 0.5);
        CharactersChanged?.Invoke();
    }

    public void HideCharacter(CharacterConfig character)
    {
        character.Visible = false;
        _renderHost.HideCharacter(character.Id);
        CharactersChanged?.Invoke();
    }

    public void RemoveCharacter(CharacterConfig character)
    {
        _renderHost.RemoveCharacter(character.Id);
        _config.Characters.Remove(character);
        _configService.Save(_config);
        CharactersChanged?.Invoke();
    }

    public void HideAll()
    {
        foreach (var character in _config.Characters)
            character.Visible = false;
        _renderHost.HideAll();
        CharactersChanged?.Invoke();
    }

    public void ShowAll()
    {
        foreach (var character in _config.Characters)
            ShowCharacter(character);

        CharactersChanged?.Invoke();
    }

    public void SaveAllState()
    {
        _configService.Save(_config);
    }

    public async Task RestoreAllAsync(bool configMode)
    {
        await _renderHost.RestoreVisibleCharactersAsync(_config.Characters, configMode);
        CharactersChanged?.Invoke();
    }

    public Task PrimeHiddenWindowsAsync()
    {
        return Task.CompletedTask;
    }

    public bool IsCharacterLoading(string characterId)
    {
        return _renderHost.IsCharacterLoading(characterId);
    }

    public void RemoveAllWindows()
    {
        _renderHost.Close();
    }
}
