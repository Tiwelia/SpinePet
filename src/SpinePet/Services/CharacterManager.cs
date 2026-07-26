using System.IO;
using SpinePet.Models;
using SpinePet.Views;

namespace SpinePet.Services;

public sealed class CharacterManager
{
    private readonly ConfigService _configService;
    private readonly RenderHostWindow _renderHost;
    private readonly AppConfig _config;
    private bool _isConfigMode;
    private int _configModeVersion;

    public CharacterManager(ConfigService configService)
    {
        _configService = configService;
        _config = configService.Load();
        _renderHost = new RenderHostWindow();
        _renderHost.SetRenderDragEnabled(_config.Global.AllowRenderDrag);
        _renderHost.CharacterScaleChanged += OnCharacterScaleChanged;
        _renderHost.CharacterAnimationsLoaded += OnCharacterAnimationsLoaded;
        _renderHost.CharacterLoadFailed += OnCharacterLoadFailed;
        _renderHost.CharactersStateChanged += OnCharactersStateChanged;
        _renderHost.CharacterPositionCommitted += OnCharacterPositionCommitted;
    }

    public IReadOnlyList<CharacterConfig> Characters => _config.Characters;

    public bool AllowRenderDrag => _config.Global.AllowRenderDrag;

    public event Action? CharactersChanged;

    public event Action<string, double, double>? CharacterScaleChanged;

    public RenderHostWindow RenderHost => _renderHost;

    public void SetConfigMode(bool configMode)
    {
        _isConfigMode = configMode;
        _configModeVersion++;
        _renderHost.SetConfigMode(configMode);
    }

    public void SetAllowRenderDrag(bool allow)
    {
        if (_config.Global.AllowRenderDrag == allow)
        {
            return;
        }

        _config.Global.AllowRenderDrag = allow;
        _renderHost.SetRenderDragEnabled(allow);
        _configService.Save(_config);
    }

    public bool AddCharacter(CharacterResourceFiles resources)
    {
        bool alreadyExists = _config.Characters.Any(character =>
            string.Equals(
                character.SkeletonPath,
                resources.SkeletonPath,
                StringComparison.OrdinalIgnoreCase));
        if (alreadyExists)
        {
            return false;
        }

        CharacterConfig character = new()
        {
            Name = Path.GetFileNameWithoutExtension(resources.SkeletonPath),
            SkeletonPath = resources.SkeletonPath,
            AtlasPath = resources.AtlasPath,
            TexturePath = resources.PrimaryTexturePath,
            AdditionalTexturePaths = resources.AdditionalTexturePaths.ToList(),
            PositionX = 300 + Random.Shared.Next(400),
            PositionY = 100 + Random.Shared.Next(300)
        };

        _config.Characters.Add(character);
        _configService.Save(_config);
        CharactersChanged?.Invoke();
        return true;
    }

    public async Task ShowCharacterAsync(CharacterConfig character)
    {
        try
        {
            await ShowCharacterCoreAsync(character);
        }
        finally
        {
            _configService.Save(_config);
            CharactersChanged?.Invoke();
        }
    }

    private async Task ShowCharacterCoreAsync(CharacterConfig character)
    {
        character.Visible = true;
        try
        {
            await _renderHost.ShowCharacterAsync(
                character,
                _isConfigMode,
                character.AnimationSpeed);
        }
        catch
        {
            character.Visible = false;
            _renderHost.HideCharacter(character.Id);
            throw;
        }
    }

    public void HideCharacter(CharacterConfig character)
    {
        character.Visible = false;
        _renderHost.HideCharacter(character.Id);
        _configService.Save(_config);
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
        foreach (CharacterConfig character in _config.Characters)
        {
            character.Visible = false;
        }

        _renderHost.HideAll();
        _configService.Save(_config);
        CharactersChanged?.Invoke();
    }

    public async Task ShowAllAsync()
    {
        try
        {
            await Task.WhenAll(
                _config.Characters.Select(ShowCharacterCoreAsync));
        }
        finally
        {
            _configService.Save(_config);
            CharactersChanged?.Invoke();
        }
    }

    public void SaveAllState()
    {
        _configService.Save(_config);
    }

    public async Task RestoreAllAsync(bool configMode)
    {
        int modeVersion = _configModeVersion;
        if (modeVersion == 0)
        {
            _isConfigMode = configMode;
        }

        await _renderHost.RestoreVisibleCharactersAsync(_config.Characters, configMode);
        _renderHost.SetConfigMode(
            modeVersion == _configModeVersion ? configMode : _isConfigMode);
        CharactersChanged?.Invoke();
    }

    public bool IsCharacterLoading(string characterId) =>
        _renderHost.IsCharacterLoading(characterId);

    public void Close()
    {
        _renderHost.Close();
    }

    private void OnCharacterScaleChanged(
        string characterId,
        double maximumScale,
        double currentScale)
    {
        CharacterConfig? character =
            _config.Characters.FirstOrDefault(item => item.Id == characterId);
        if (character != null)
        {
            character.Scale = currentScale;
        }

        CharacterScaleChanged?.Invoke(characterId, maximumScale, currentScale);
    }

    private void OnCharacterAnimationsLoaded(
        string characterId,
        IReadOnlyList<string> animations)
    {
        CharacterConfig? character =
            _config.Characters.FirstOrDefault(item => item.Id == characterId);
        if (character != null &&
            string.IsNullOrWhiteSpace(character.ConfiguredAnimation) &&
            animations.Count > 0)
        {
            character.ConfiguredAnimation = animations[0];
        }

        CharactersChanged?.Invoke();
    }

    private void OnCharacterLoadFailed(string characterId)
    {
        CharacterConfig? character =
            _config.Characters.FirstOrDefault(item => item.Id == characterId);
        if (character != null)
        {
            character.Visible = false;
        }

        CharactersChanged?.Invoke();
    }

    private void OnCharactersStateChanged()
    {
        CharactersChanged?.Invoke();
    }

    private void OnCharacterPositionCommitted(
        string characterId,
        double left,
        double top)
    {
        CharacterConfig? character =
            _config.Characters.FirstOrDefault(item => item.Id == characterId);
        if (character == null)
        {
            return;
        }

        character.PositionX = left;
        character.PositionY = top;
        _configService.Save(_config);
        CharactersChanged?.Invoke();
    }
}
