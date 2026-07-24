using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using SpinePet.Models;
using SpinePet.Services;

namespace SpinePet.Views;

public partial class RenderHostWindow : Window
{
    private const double DefaultMaxScale = 2.0;
    private const double LegacyViewportWidth = 1200;
    private const double LegacyViewportHeight = 1000;
    private const double LegacyBottomMargin = 24;
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int WS_EX_LAYERED = 0x80000;
    private readonly Dictionary<string, CharacterRuntimeState> _characterStates = new();
    private readonly HashSet<string> _pendingLoads = new();
    private bool _webViewReady;
    private bool _pageReady;

    public event Action<string, double, double>? CharacterScaleChanged;
    public event Action<string, IReadOnlyList<string>>? CharacterAnimationsLoaded;
    public event Action<string>? CharacterLoadFailed;
    public event Action? CharactersStateChanged;

    public RenderHostWindow()
    {
        InitializeComponent();
        var area = SystemParameters.WorkArea;
        Left = area.Left;
        Top = area.Top;
        Width = area.Width;
        Height = area.Height;
        Loaded += OnLoaded;
        SourceInitialized += OnSourceInitialized;
        SizeChanged += (_, _) => SendToJs("resize");
        IsVisibleChanged += (_, _) => SendToJs("resize");
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    public IReadOnlyCollection<string> VisibleCharacterIds =>
        _characterStates.Values.Where(s => s.IsVisible).Select(s => s.Config.Id).ToList();

    public bool IsCharacterLoading(string characterId) =>
        _pendingLoads.Contains(characterId) ||
        (_characterStates.TryGetValue(characterId, out var state) && state.IsLoading);

    public bool IsCharacterVisible(string characterId) =>
        _characterStates.TryGetValue(characterId, out var state) && state.IsVisible;

    public IReadOnlyList<string> GetAnimationNames(string characterId) =>
        _characterStates.TryGetValue(characterId, out var state) ? state.AnimationNames : Array.Empty<string>();

    public double GetMaxScale(string characterId) =>
        _characterStates.TryGetValue(characterId, out var state) ? state.MaxScale : DefaultMaxScale;

    public double GetCurrentScale(string characterId) =>
        _characterStates.TryGetValue(characterId, out var state) ? state.CurrentScale : 0.2;

    public async Task InitializeAsync()
    {
        if (_webViewReady)
            return;

        await SpineWebViewService.InitializeAsync(WebView, OnWebMessage);
        _webViewReady = true;
    }

    public async Task ShowCharacterAsync(CharacterConfig character, bool configMode, double speed)
    {
        var state = GetOrCreateState(character);
        state.IsVisible = true;
        state.Config.Visible = true;
        state.Speed = speed;

        await EnsureWindowAndPageAsync();

        _pendingLoads.Add(character.Id);
        state.IsLoading = true;
        CharactersStateChanged?.Invoke();

        SendToJs("showCharacter", new
        {
            id = character.Id,
            skel = SpineWebViewService.ToVirtualUrl(character.SkelPath),
            atlas = SpineWebViewService.ToVirtualUrl(character.AtlasPath),
            x = ToSceneX(character.PositionX),
            y = ToSceneY(character.PositionY),
            scale = character.Scale > 0 ? character.Scale : 0.2,
            speed,
            animation = character.CurrentAnimation ?? string.Empty,
            timeoutMs = 15000,
            configMode
        });
    }

    public void HideCharacter(string characterId)
    {
        if (!_characterStates.TryGetValue(characterId, out var state))
            return;

        state.IsVisible = false;
        state.Config.Visible = false;
        state.IsLoading = false;
        _pendingLoads.Remove(characterId);
        SendToJs("hideCharacter", new { id = characterId });
        SendToJs("clearFrame");
        UpdateHostVisibility();
        CharactersStateChanged?.Invoke();
    }

    public void RemoveCharacter(string characterId)
    {
        _pendingLoads.Remove(characterId);
        _characterStates.Remove(characterId);
        SendToJs("removeCharacter", new { id = characterId });
        UpdateHostVisibility();
        CharactersStateChanged?.Invoke();
    }

    public void SetCharacterScale(string characterId, double scale)
    {
        if (!_characterStates.TryGetValue(characterId, out var state))
            return;

        state.CurrentScale = Math.Clamp(scale, 0.05, state.MaxScale);
        state.Config.Scale = state.CurrentScale;
        SendToJs("setCharacterScale", new { id = characterId, scale = state.CurrentScale });
    }

    public void SetCharacterSpeed(string characterId, double speed)
    {
        if (!_characterStates.TryGetValue(characterId, out var state))
            return;

        state.Speed = speed;
        SendToJs("setCharacterSpeed", new { id = characterId, speed });
    }

    public void PlayCharacterAnimation(string characterId, string animation, bool loop)
    {
        if (!_characterStates.TryGetValue(characterId, out var state))
            return;

        state.Config.CurrentAnimation = animation;
        SendToJs("playCharacterAnimation", new { id = characterId, name = animation, loop });
    }

    public void SetCharacterConfigMode(string characterId, bool configMode)
    {
        if (!_characterStates.ContainsKey(characterId))
            return;

        SendToJs("setCharacterConfigMode", new { id = characterId, configMode });
    }

    public void MoveCharacter(string characterId, double left, double top)
    {
        if (!_characterStates.TryGetValue(characterId, out var state))
            return;

        state.Config.PositionX = left;
        state.Config.PositionY = top;
        SendToJs("moveCharacter", new { id = characterId, x = ToSceneX(left), y = ToSceneY(top) });
    }

    public void ResetCharacterPosition(string characterId)
    {
        if (!_characterStates.TryGetValue(characterId, out var state))
            return;

        var area = SystemParameters.WorkArea;
        var targetLeft = area.Left + Math.Max(0, (area.Width - LegacyViewportWidth) / 2.0);
        var targetTop = area.Top + Math.Max(0, (area.Height - LegacyViewportHeight) / 2.0);

        state.Config.PositionX = targetLeft;
        state.Config.PositionY = targetTop;
        SendToJs("moveCharacter", new
        {
            id = characterId,
            x = ToSceneX(targetLeft),
            y = ToSceneY(targetTop)
        });
    }

    public void HideAll()
    {
        foreach (var state in _characterStates.Values)
        {
            state.IsVisible = false;
            state.Config.Visible = false;
            state.IsLoading = false;
        }

        _pendingLoads.Clear();
        SendToJs("hideAllCharacters");
        UpdateHostVisibility();
        CharactersStateChanged?.Invoke();
    }

    public async Task RestoreVisibleCharactersAsync(IEnumerable<CharacterConfig> characters, bool configMode)
    {
        foreach (var character in characters.Where(c => c.Visible))
        {
            await ShowCharacterAsync(character, configMode, 0.5);
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await EnsureWindowAndPageAsync();
    }

    private async Task EnsureWindowAndPageAsync()
    {
        if (!IsVisible)
        {
            Show();
            UpdateLayout();
        }

        await InitializeAsync();

        if (_pageReady)
        {
            SendToJs("resize");
            return;
        }

        var waitUntil = DateTime.UtcNow.AddSeconds(10);
        while (!_pageReady && DateTime.UtcNow < waitUntil)
            await Task.Delay(50);

        if (_pageReady)
            SendToJs("resize");
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED);
    }

    private CharacterRuntimeState GetOrCreateState(CharacterConfig character)
    {
        if (_characterStates.TryGetValue(character.Id, out var existing))
        {
            existing.Config = character;
            return existing;
        }

        var state = new CharacterRuntimeState
        {
            Config = character,
            CurrentScale = character.Scale > 0 ? character.Scale : 0.2,
            MaxScale = DefaultMaxScale,
            Speed = 0.5
        };
        _characterStates[character.Id] = state;
        return state;
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var json = e.TryGetWebMessageAsString();
            if (string.IsNullOrWhiteSpace(json))
                return;

            var msg = JsonDocument.Parse(json).RootElement;
            var type = msg.GetProperty("type").GetString();
            var data = msg.TryGetProperty("data", out var dataElement) ? dataElement : default;

            Dispatcher.Invoke(() =>
            {
                switch (type)
                {
                    case "ready":
                        _pageReady = true;
                        UpdateHostVisibility();
                        break;
                    case "characterLoaded":
                        HandleCharacterLoaded(data);
                        break;
                    case "characterScaleChanged":
                        HandleCharacterScaleChanged(data);
                        break;
                    case "characterError":
                        HandleCharacterError(data);
                        break;
                    case "log":
                        break;
                }
            });
        }
        catch
        {
        }
    }

    private void HandleCharacterLoaded(JsonElement data)
    {
        if (!TryGetCharacterId(data, out var characterId) ||
            !_characterStates.TryGetValue(characterId, out var state))
            return;

        state.IsLoading = false;
        _pendingLoads.Remove(characterId);
        state.AnimationNames.Clear();

        if (data.TryGetProperty("animations", out var anims) && anims.ValueKind == JsonValueKind.Array)
        {
            foreach (var animation in anims.EnumerateArray())
            {
                var name = animation.GetString();
                if (!string.IsNullOrWhiteSpace(name))
                    state.AnimationNames.Add(name);
            }
        }

        if (data.TryGetProperty("maxScale", out var maxScaleElement) &&
            maxScaleElement.TryGetDouble(out var parsedMaxScale))
        {
            state.MaxScale = Math.Clamp(parsedMaxScale, 0.05, DefaultMaxScale);
        }

        if (data.TryGetProperty("appliedScale", out var appliedScaleElement) &&
            appliedScaleElement.TryGetDouble(out var parsedScale))
        {
            state.CurrentScale = Math.Clamp(parsedScale, 0.05, state.MaxScale);
            state.Config.Scale = state.CurrentScale;
        }

        if (string.IsNullOrWhiteSpace(state.Config.CurrentAnimation) && state.AnimationNames.Count > 0)
            state.Config.CurrentAnimation = state.AnimationNames[0];

        CharacterAnimationsLoaded?.Invoke(characterId, state.AnimationNames);
        CharacterScaleChanged?.Invoke(characterId, state.MaxScale, state.CurrentScale);
        UpdateHostVisibility();
        CharactersStateChanged?.Invoke();
    }

    private void HandleCharacterScaleChanged(JsonElement data)
    {
        if (!TryGetCharacterId(data, out var characterId) ||
            !_characterStates.TryGetValue(characterId, out var state))
            return;

        if (data.TryGetProperty("maxScale", out var maxScaleElement) &&
            maxScaleElement.TryGetDouble(out var parsedMaxScale))
        {
            state.MaxScale = Math.Clamp(parsedMaxScale, 0.05, DefaultMaxScale);
        }

        if (data.TryGetProperty("scale", out var scaleElement) &&
            scaleElement.TryGetDouble(out var parsedScale))
        {
            state.CurrentScale = Math.Clamp(parsedScale, 0.05, state.MaxScale);
            state.Config.Scale = state.CurrentScale;
        }

        CharacterScaleChanged?.Invoke(characterId, state.MaxScale, state.CurrentScale);
    }

    private void HandleCharacterError(JsonElement data)
    {
        if (!TryGetCharacterId(data, out var characterId) ||
            !_characterStates.TryGetValue(characterId, out var state))
            return;

        state.IsLoading = false;
        state.IsVisible = false;
        state.Config.Visible = false;
        _pendingLoads.Remove(characterId);
        CharacterLoadFailed?.Invoke(characterId);
        UpdateHostVisibility();
        CharactersStateChanged?.Invoke();
    }

    private static bool TryGetCharacterId(JsonElement data, out string characterId)
    {
        characterId = string.Empty;
        if (data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("id", out var idElement))
            return false;

        characterId = idElement.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(characterId);
    }

    private void UpdateHostVisibility()
    {
        var anyVisible = _characterStates.Values.Any(s => s.IsVisible || s.IsLoading);
        Visibility = anyVisible ? Visibility.Visible : Visibility.Hidden;
        if (anyVisible)
            SendToJs("resize");
    }

    private void SendToJs(string type, object? data = null)
    {
        if (WebView.CoreWebView2 == null)
            return;

        WebView.CoreWebView2.PostWebMessageAsString(JsonSerializer.Serialize(new { type, data }));
    }

    private static double ToSceneX(double left)
    {
        return left + (LegacyViewportWidth / 2.0);
    }

    private static double ToSceneY(double top)
    {
        return top + LegacyViewportHeight - LegacyBottomMargin;
    }
}
