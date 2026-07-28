using System.IO;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using SpinePet.Models;
using SpinePet.Rendering.Native;

namespace SpinePet.Services;

public sealed class CharacterManager
{
    private readonly ConfigService _configService;
    private readonly CharacterIdentityService _identityService;
    [SuppressMessage(
        "Performance",
        "CA1859:Use concrete types when possible for improved performance",
        Justification = "The renderer backend is intentionally isolated behind this contract.")]
    private readonly ICharacterRenderHost _renderHost;
    private readonly AppConfig _config;
    private bool _isConfigMode;
    private int _configModeVersion;

    public CharacterManager(
        ConfigService configService,
        CharacterIdentityService? identityService = null)
    {
        _configService = configService;
        _identityService = identityService ?? new CharacterIdentityService();
        _config = configService.Load();
        _renderHost = new NativeCharacterRenderHost();
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

    public ICharacterRenderHost RenderHost => _renderHost;

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
        CharacterConfig? character = _config.Characters.FirstOrDefault(character =>
            string.Equals(
                character.SkeletonPath,
                resources.SkeletonPath,
                StringComparison.OrdinalIgnoreCase));
        character ??= _config.Characters.FirstOrDefault(existing =>
            string.Equals(
                GetCharacterIdentity(existing).ResourceName,
                resources.Identity.ResourceName,
                StringComparison.OrdinalIgnoreCase));

        if (character != null)
        {
            bool shouldUpdatePaths =
                !File.Exists(character.SkeletonPath) ||
                string.Equals(
                    character.ResourceType,
                    resources.ResourceType,
                    StringComparison.OrdinalIgnoreCase);
            bool changed = SetIfDifferent(
                character.Name,
                resources.Identity.DisplayName,
                value => character.Name = value);
            if (shouldUpdatePaths)
            {
                changed |= UpdateCharacterResources(character, resources);
            }

            if (changed)
            {
                _configService.Save(_config);
                CharactersChanged?.Invoke();
            }

            return false;
        }

        Rect workArea = SystemParameters.WorkArea;
        character = new CharacterConfig
        {
            Name = resources.Identity.DisplayName,
            SkeletonPath = resources.SkeletonPath,
            AtlasPath = resources.AtlasPath,
            TexturePath = resources.PrimaryTexturePath,
            AdditionalTexturePaths = resources.AdditionalTexturePaths.ToList(),
            ResourceType = resources.ResourceType,
            PositionX = workArea.Left + workArea.Width / 2,
            PositionY = workArea.Bottom - 24
        };

        _config.Characters.Add(character);
        _configService.Save(_config);
        CharactersChanged?.Invoke();
        return true;
    }

    public CharacterIdentity GetCharacterIdentity(CharacterConfig character)
    {
        return _identityService.Resolve(
            character.SkeletonPath,
            character.Name);
    }

    public string GetCharacterThumbnailPath(CharacterConfig character)
    {
        CharacterIdentity identity = GetCharacterIdentity(character);
        return CharacterIconService.GetThumbnailPath(character, identity);
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

    public async Task SwitchCharacterResourcesAsync(
        CharacterConfig character,
        CharacterResourceFiles resources)
    {
        if (!string.Equals(
            GetCharacterIdentity(character).ResourceName,
            resources.Identity.ResourceName,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The selected resources belong to a different character skin.");
        }

        if (string.Equals(
            character.ResourceType,
            resources.ResourceType,
            StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                character.SkeletonPath,
                resources.SkeletonPath,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        bool wasVisible = character.Visible;
        if (wasVisible)
        {
            _renderHost.RemoveCharacter(character.Id);
        }

        character.ConfiguredAnimation = string.Empty;
        UpdateCharacterResources(character, resources);

        try
        {
            if (wasVisible)
            {
                await ShowCharacterCoreAsync(character);
            }
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

    private static bool UpdateCharacterResources(
        CharacterConfig character,
        CharacterResourceFiles resources)
    {
        bool changed = false;
        changed |= SetIfDifferent(
            character.Name,
            resources.Identity.DisplayName,
            value => character.Name = value);
        changed |= SetIfDifferent(
            character.SkeletonPath,
            resources.SkeletonPath,
            value => character.SkeletonPath = value);
        changed |= SetIfDifferent(
            character.AtlasPath,
            resources.AtlasPath,
            value => character.AtlasPath = value);
        changed |= SetIfDifferent(
            character.TexturePath,
            resources.PrimaryTexturePath,
            value => character.TexturePath = value);
        changed |= SetIfDifferent(
            character.ResourceType,
            resources.ResourceType,
            value => character.ResourceType = value);

        if (!character.AdditionalTexturePaths.SequenceEqual(
            resources.AdditionalTexturePaths,
            StringComparer.OrdinalIgnoreCase))
        {
            character.AdditionalTexturePaths =
                resources.AdditionalTexturePaths.ToList();
            changed = true;
        }

        return changed;
    }

    private static bool SetIfDifferent(
        string currentValue,
        string newValue,
        Action<string> setter)
    {
        if (string.Equals(
            currentValue,
            newValue,
            StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        setter(newValue);
        return true;
    }
}
