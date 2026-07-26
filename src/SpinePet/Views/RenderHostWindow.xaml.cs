using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using SpinePet.Infrastructure;
using SpinePet.Models;
using SpinePet.Services;

namespace SpinePet.Views;

public partial class RenderHostWindow : Window
{
    private const double DefaultMaxScale = 2.0;
    private const double LegacyViewportWidth = 1200;
    private const double LegacyViewportHeight = 1000;
    private const double LegacyBottomMargin = 24;
    private const double MinimumVisibleCharacterExtent = 48;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_NCHITTEST = 0x0084;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WH_MOUSE_LL = 14;
    private const int HTCLIENT = 1;
    private const int HTTRANSPARENT = -1;
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int WS_EX_LAYERED = 0x80000;
    private readonly Dictionary<string, CharacterRuntimeState> _characterStates = new();
    private readonly Dictionary<string, Rect> _hitRegions = new();
    private readonly Dictionary<int, TaskCompletionSource> _pendingFrameClears = new();
    private readonly LowLevelMouseProc _mouseHookProc;
    private IntPtr _mouseHook;
    private bool _isConfigMode = true;
    private bool _renderDragEnabled = true;
    private bool _pointerArmed;
    private bool _pointerDragging;
    private bool _pointerReleased;
    private int _pointerSessionId;
    private string? _pointerCharacterId;
    private NativePoint _pointerLatestScreen;
    private System.Windows.Point _pointerStartClient;
    private System.Windows.Point _pointerStartCharacterPosition;
    private Rect _pointerStartHitRegion = Rect.Empty;
    private bool _pointerMovePending;
    private bool _dragRenderingSubscribed;
    private bool _pageReady;
    private Task? _initializationTask;
    private readonly TaskCompletionSource _pageReadyCompletion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _hostVisibilityVersion;
    private int _nextClearFrameRequestId;

    public event Action<string, double, double>? CharacterScaleChanged;
    public event Action<string, IReadOnlyList<string>>? CharacterAnimationsLoaded;
    public event Action<string>? CharacterLoadFailed;
    public event Action? CharactersStateChanged;
    public event Action<string, double, double>? CharacterPositionCommitted;

    public RenderHostWindow()
    {
        InitializeComponent();
        _mouseHookProc = MouseHookCallback;
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
        Loaded += OnLoaded;
        SourceInitialized += OnSourceInitialized;
        SizeChanged += (_, _) => SendToJs("resize");
        IsVisibleChanged += (_, _) => SendToJs("resize");
        Closed += (_, _) =>
        {
            ResetPointerInteraction();
            RemoveMouseHook();
        };
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int idHook,
        LowLevelMouseProc callback,
        IntPtr moduleHandle,
        uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(
        IntPtr hook,
        int code,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport(
        "kernel32.dll",
        EntryPoint = "GetModuleHandleW",
        CharSet = CharSet.Unicode,
        ExactSpelling = true,
        SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    private delegate IntPtr LowLevelMouseProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LowLevelMouseData
    {
        public NativePoint Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    public bool IsCharacterLoading(string characterId) =>
        _characterStates.TryGetValue(characterId, out var state) &&
        state.IsLoading;

    public bool IsCharacterVisible(string characterId) =>
        _characterStates.TryGetValue(characterId, out var state) && state.IsVisible;

    public IReadOnlyList<string> GetAnimationNames(string characterId) =>
        _characterStates.TryGetValue(characterId, out var state) ? state.AnimationNames : Array.Empty<string>();

    public double GetMaxScale(string characterId) =>
        _characterStates.TryGetValue(characterId, out var state) ? state.MaxScale : DefaultMaxScale;

    public double GetCurrentScale(string characterId) =>
        _characterStates.TryGetValue(characterId, out var state) ? state.CurrentScale : 0.2;

    public Task InitializeAsync()
    {
        _initializationTask ??= InitializeCoreAsync();
        return _initializationTask;
    }

    private async Task InitializeCoreAsync()
    {
        await SpineWebViewService.InitializeAsync(WebView, OnWebMessage);
    }

    public async Task ShowCharacterAsync(CharacterConfig character, bool configMode, double speed)
    {
        _hostVisibilityVersion++;
        _isConfigMode = configMode;
        var state = GetOrCreateState(character);
        state.IsVisible = true;
        state.Config.Visible = true;

        await EnsureWindowAndPageAsync();

        state.IsLoading = true;
        CharactersStateChanged?.Invoke();

        SendToJs("showCharacter", new
        {
            id = character.Id,
            skel = SpineWebViewService.ToVirtualUrl(character.SkeletonPath),
            atlas = SpineWebViewService.ToVirtualUrl(character.AtlasPath),
            x = ToSceneX(character.PositionX),
            y = ToSceneY(character.PositionY),
            scale = character.Scale > 0 ? character.Scale : 0.2,
            speed,
            animation = character.ConfiguredAnimation,
            timeoutMs = 15000,
            configMode
        });
    }

    public void HideCharacter(string characterId)
    {
        if (!_characterStates.TryGetValue(characterId, out var state))
            return;

        if (string.Equals(_pointerCharacterId, characterId, StringComparison.Ordinal))
            CancelPointerInteraction();

        state.IsVisible = false;
        state.Config.Visible = false;
        state.IsLoading = false;
        SendToJs("hideCharacter", new { id = characterId });
        UpdateHostVisibility();
        CharactersStateChanged?.Invoke();
    }

    public void RemoveCharacter(string characterId)
    {
        if (string.Equals(_pointerCharacterId, characterId, StringComparison.Ordinal))
            CancelPointerInteraction();

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

        state.Config.AnimationSpeed = Math.Clamp(speed, 0.1, 2.0);
        SendToJs(
            "setCharacterSpeed",
            new { id = characterId, speed = state.Config.AnimationSpeed });
    }

    public void PlayCharacterAnimation(string characterId, string animation, bool loop)
    {
        if (!_characterStates.TryGetValue(characterId, out var state))
            return;

        state.Config.ConfiguredAnimation = animation;
        SendToJs("playCharacterAnimation", new { id = characterId, name = animation, loop });
    }

    public void SetConfigMode(bool configMode)
    {
        _isConfigMode = configMode;
        CancelPointerInteraction();

        SendToJs("setConfigMode", new
        {
            configMode,
            characters = _characterStates.Values.Select(state => new
            {
                id = state.Config.Id,
                animation = state.Config.ConfiguredAnimation
            }).ToArray()
        });
    }

    public void SetRenderDragEnabled(bool enabled)
    {
        _renderDragEnabled = enabled;
        if (!enabled && _pointerDragging)
            CancelPointerInteraction();
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
        CancelPointerInteraction();
        foreach (var state in _characterStates.Values)
        {
            state.IsVisible = false;
            state.Config.Visible = false;
            state.IsLoading = false;
        }

        SendToJs("hideAllCharacters");
        UpdateHostVisibility();
        CharactersStateChanged?.Invoke();
    }

    public async Task RestoreVisibleCharactersAsync(IEnumerable<CharacterConfig> characters, bool configMode)
    {
        foreach (var character in characters.Where(c => c.Visible))
        {
            await ShowCharacterAsync(character, configMode, character.AnimationSpeed);
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await EnsureWindowAndPageAsync();
        }
        catch (Exception exception)
        {
            AppLogger.Write(
                nameof(RenderHostWindow),
                $"initialization-failed message={exception.Message}");
        }
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

        Task completedTask = await Task.WhenAny(
            _pageReadyCompletion.Task,
            Task.Delay(TimeSpan.FromSeconds(10)));

        if (_pageReady)
        {
            SendToJs("resize");
        }
        else if (completedTask != _pageReadyCompletion.Task)
        {
            AppLogger.Write(
                nameof(RenderHostWindow),
                "renderer-page-ready-timeout");
            throw new TimeoutException(
                "The renderer page did not become ready within 10 seconds.");
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        Marshal.SetLastPInvokeError(0);
        int previousStyle = SetWindowLong(
            hwnd,
            GWL_EXSTYLE,
            (exStyle | WS_EX_LAYERED) & ~WS_EX_TRANSPARENT);
        int styleError = Marshal.GetLastPInvokeError();
        if (previousStyle == 0 && styleError != 0)
        {
            AppLogger.Write(
                "RenderHost",
                $"window-style-update-failed error={styleError}");
        }
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
        _mouseHook = SetWindowsHookEx(
            WH_MOUSE_LL,
            _mouseHookProc,
            GetModuleHandle(null),
            0);

        if (_mouseHook == IntPtr.Zero)
        {
            AppLogger.Write(
                "RenderHost",
                $"mouse-hook-install-failed error={Marshal.GetLastWin32Error()}");
        }
        else
        {
            AppLogger.Write("RenderHost", "mouse-hook-installed");
        }
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
            MaxScale = DefaultMaxScale
        };
        _characterStates[character.Id] = state;
        return state;
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            string json = e.TryGetWebMessageAsString();
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement message = document.RootElement;
            if (!message.TryGetProperty("type", out JsonElement typeElement))
            {
                return;
            }

            string? type = typeElement.GetString();
            JsonElement data = message.TryGetProperty(
                "data",
                out JsonElement dataElement)
                ? dataElement
                : default;

            if (Dispatcher.CheckAccess())
            {
                HandleWebMessage(type, data);
            }
            else
            {
                Dispatcher.Invoke(() => HandleWebMessage(type, data));
            }
        }
        catch (Exception exception)
        {
            AppLogger.Write(
                "RenderHost",
                $"web-message-failed message={exception.Message}");
        }
    }

    private void HandleWebMessage(string? type, JsonElement data)
    {
        switch (type)
        {
            case "ready":
                _pageReady = true;
                _pageReadyCompletion.TrySetResult();
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
            case "frameCleared":
                HandleFrameCleared(data);
                break;
            case "hitRegionsChanged":
                HandleHitRegionsChanged(data);
                break;
            case "characterPointerTarget":
                HandleCharacterPointerTarget(data);
                break;
        }
    }

    private void HandleCharacterLoaded(JsonElement data)
    {
        if (!TryGetCharacterId(data, out var characterId) ||
            !_characterStates.TryGetValue(characterId, out var state))
            return;

        state.IsLoading = false;
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

        if (string.IsNullOrWhiteSpace(state.Config.ConfiguredAnimation) &&
            state.AnimationNames.Count > 0)
        {
            state.Config.ConfiguredAnimation = state.AnimationNames[0];
        }

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
        if (HasVisibleOrLoadingCharacters())
        {
            _hostVisibilityVersion++;
            Visibility = Visibility.Visible;
            SendToJs("resize");
            return;
        }

        _ = HideHostWhenClearAsync(++_hostVisibilityVersion);
    }

    private bool HasVisibleOrLoadingCharacters() =>
        _characterStates.Values.Any(s => s.IsVisible || s.IsLoading);

    private async Task HideHostWhenClearAsync(int visibilityVersion)
    {
        if (WebView.CoreWebView2 != null && _pageReady)
            await RequestClearFrameAsync();

        await Task.Delay(50);

        if (visibilityVersion == _hostVisibilityVersion && !HasVisibleOrLoadingCharacters())
            Visibility = Visibility.Hidden;
    }

    private async Task RequestClearFrameAsync()
    {
        var requestId = ++_nextClearFrameRequestId;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingFrameClears[requestId] = completion;

        SendToJs("clearFrame", new { requestId });

        await Task.WhenAny(completion.Task, Task.Delay(500));
        _pendingFrameClears.Remove(requestId);
    }

    private void HandleFrameCleared(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("requestId", out var requestIdElement) ||
            !requestIdElement.TryGetInt32(out var requestId) ||
            !_pendingFrameClears.TryGetValue(requestId, out var completion))
        {
            return;
        }

        completion.TrySetResult();
    }

    private void HandleHitRegionsChanged(JsonElement data)
    {
        _hitRegions.Clear();

        if (data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("regions", out var regionsElement) ||
            regionsElement.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var region in regionsElement.EnumerateArray())
        {
            if (region.ValueKind != JsonValueKind.Object ||
                !region.TryGetProperty("id", out var idElement) ||
                !region.TryGetProperty("x", out var xElement) ||
                !region.TryGetProperty("y", out var yElement) ||
                !region.TryGetProperty("width", out var widthElement) ||
                !region.TryGetProperty("height", out var heightElement) ||
                !xElement.TryGetDouble(out var x) ||
                !yElement.TryGetDouble(out var y) ||
                !widthElement.TryGetDouble(out var width) ||
                !heightElement.TryGetDouble(out var height))
            {
                continue;
            }

            var id = idElement.GetString();
            if (string.IsNullOrWhiteSpace(id) || width <= 0 || height <= 0)
                continue;

            var rect = new Rect(x, y, width, height);
            rect.Inflate(2, 2);
            _hitRegions[id] = rect;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_NCHITTEST)
        {
            if (ShouldReceiveMouseAt(lParam))
            {
                handled = true;
                return new IntPtr(HTCLIENT);
            }

            handled = true;
            return new IntPtr(HTTRANSPARENT);
        }

        return IntPtr.Zero;
    }

    private bool ShouldReceiveMouseAt(IntPtr lParam)
    {
        return ShouldBlockScreenPoint(GetSignedLoWord(lParam), GetSignedHiWord(lParam));
    }

    private IntPtr MouseHookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0 || Dispatcher.HasShutdownStarted)
            return CallNextHookEx(_mouseHook, code, wParam, lParam);

        var message = wParam.ToInt32();
        if (message != WM_LBUTTONDOWN &&
            message != WM_MOUSEMOVE &&
            message != WM_LBUTTONUP)
        {
            return CallNextHookEx(_mouseHook, code, wParam, lParam);
        }

        var mouseData = Marshal.PtrToStructure<LowLevelMouseData>(lParam);

        if (message == WM_LBUTTONDOWN)
        {
            if (!_isConfigMode &&
                HasVisibleOrLoadingCharacters() &&
                ShouldBlockScreenPoint(mouseData.Point.X, mouseData.Point.Y))
            {
                BeginPointerInteraction(mouseData.Point);
                return new IntPtr(1);
            }
        }
        else if (_pointerArmed && message == WM_MOUSEMOVE)
        {
            UpdatePointerInteraction(mouseData.Point, force: false);
            // Blocking a low-level WM_MOUSEMOVE also prevents Windows from
            // advancing the cursor, so later drag deltas remain only a few
            // pixels. Button messages still stay blocked to prevent click-
            // through, but movement must continue through the hook chain.
            return CallNextHookEx(_mouseHook, code, wParam, lParam);
        }
        else if (_pointerArmed && message == WM_LBUTTONUP)
        {
            EndPointerInteraction(mouseData.Point);
            return new IntPtr(1);
        }

        return CallNextHookEx(_mouseHook, code, wParam, lParam);
    }

    private void BeginPointerInteraction(NativePoint screenPoint)
    {
        CancelPointerInteraction();
        _pointerArmed = true;
        _pointerReleased = false;
        _pointerDragging = false;
        _pointerCharacterId = null;
        _pointerLatestScreen = screenPoint;
        _pointerStartClient = PointFromScreen(
            new System.Windows.Point(screenPoint.X, screenPoint.Y));
        _pointerStartHitRegion = Rect.Empty;
        _pointerMovePending = false;
        var sessionId = ++_pointerSessionId;

        SendToJs("characterPointerDown", new
        {
            sessionId,
            x = _pointerStartClient.X,
            y = _pointerStartClient.Y
        });
    }

    private void HandleCharacterPointerTarget(JsonElement data)
    {
        if (!_pointerArmed ||
            data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("sessionId", out var sessionElement) ||
            !sessionElement.TryGetInt32(out var sessionId) ||
            sessionId != _pointerSessionId)
        {
            return;
        }

        var characterId = data.TryGetProperty("id", out var idElement)
            ? idElement.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(characterId) ||
            !_characterStates.TryGetValue(characterId, out var state) ||
            !state.IsVisible)
        {
            if (_pointerReleased)
                ResetPointerInteraction();
            return;
        }

        _pointerCharacterId = characterId;
        _pointerStartCharacterPosition = new System.Windows.Point(
            state.Config.PositionX,
            state.Config.PositionY);
        _pointerStartHitRegion = _hitRegions.TryGetValue(characterId, out var hitRegion)
            ? hitRegion
            : Rect.Empty;

        if (_pointerReleased)
        {
            UpdatePointerInteraction(_pointerLatestScreen, force: true);
            FinalizePointerInteraction();
        }
        else
            UpdatePointerInteraction(_pointerLatestScreen, force: true);
    }

    private void UpdatePointerInteraction(NativePoint screenPoint, bool force)
    {
        _pointerLatestScreen = screenPoint;
        if (!_pointerArmed ||
            string.IsNullOrWhiteSpace(_pointerCharacterId) ||
            !_characterStates.ContainsKey(_pointerCharacterId))
        {
            return;
        }

        var delta = GetPointerDelta(screenPoint);
        if (!_pointerDragging)
        {
            if (!_renderDragEnabled)
                return;

            var horizontalMoved = Math.Abs(delta.X) >= SystemParameters.MinimumHorizontalDragDistance;
            var verticalMoved = Math.Abs(delta.Y) >= SystemParameters.MinimumVerticalDragDistance;
            if (!horizontalMoved && !verticalMoved)
                return;

            _pointerDragging = true;
            StartDragRendering();
            SendToJs("beginCharacterDrag", new { id = _pointerCharacterId });
        }

        _pointerMovePending = true;
        if (force)
            FlushPendingDragPosition();
    }

    private void OnDragRendering(object? sender, EventArgs e)
    {
        FlushPendingDragPosition();
    }

    private void StartDragRendering()
    {
        if (_dragRenderingSubscribed)
            return;

        CompositionTarget.Rendering += OnDragRendering;
        _dragRenderingSubscribed = true;
    }

    private void StopDragRendering()
    {
        if (!_dragRenderingSubscribed)
            return;

        CompositionTarget.Rendering -= OnDragRendering;
        _dragRenderingSubscribed = false;
    }

    private void FlushPendingDragPosition()
    {
        if (!_pointerMovePending ||
            !_pointerDragging ||
            string.IsNullOrWhiteSpace(_pointerCharacterId))
        {
            return;
        }

        _pointerMovePending = false;
        var delta = ConstrainPointerDelta(
            _pointerLatestScreen,
            GetPointerDelta(_pointerLatestScreen));
        MoveCharacter(
            _pointerCharacterId,
            _pointerStartCharacterPosition.X + delta.X,
            _pointerStartCharacterPosition.Y + delta.Y);

        if (!_pointerStartHitRegion.IsEmpty)
        {
            _hitRegions[_pointerCharacterId] = new Rect(
                _pointerStartHitRegion.X + delta.X,
                _pointerStartHitRegion.Y + delta.Y,
                _pointerStartHitRegion.Width,
                _pointerStartHitRegion.Height);
        }
    }

    private void EndPointerInteraction(NativePoint screenPoint)
    {
        _pointerLatestScreen = screenPoint;
        _pointerReleased = true;

        if (!string.IsNullOrWhiteSpace(_pointerCharacterId))
        {
            UpdatePointerInteraction(screenPoint, force: true);
            FinalizePointerInteraction();
            return;
        }

        _ = ResetReleasedPointerAfterDelayAsync(_pointerSessionId);
    }

    private async Task ResetReleasedPointerAfterDelayAsync(int sessionId)
    {
        await Task.Delay(250);
        if (_pointerArmed && _pointerReleased && sessionId == _pointerSessionId)
            ResetPointerInteraction();
    }

    private void FinalizePointerInteraction()
    {
        var characterId = _pointerCharacterId;
        if (string.IsNullOrWhiteSpace(characterId) ||
            !_characterStates.TryGetValue(characterId, out var state))
        {
            ResetPointerInteraction();
            return;
        }

        if (_pointerDragging)
        {
            FlushPendingDragPosition();
            SendToJs("endCharacterDrag", new { id = characterId });
            CharacterPositionCommitted?.Invoke(
                characterId,
                state.Config.PositionX,
                state.Config.PositionY);
        }
        else
        {
            SendToJs("characterClick", new { id = characterId });
        }

        ResetPointerInteraction();
    }

    private System.Windows.Vector GetPointerDelta(NativePoint screenPoint)
    {
        var current = PointFromScreen(new System.Windows.Point(screenPoint.X, screenPoint.Y));
        return current - _pointerStartClient;
    }

    private System.Windows.Vector ConstrainPointerDelta(
        NativePoint screenPoint,
        System.Windows.Vector delta)
    {
        if (_pointerStartHitRegion.IsEmpty)
            return delta;

        var screen = System.Windows.Forms.Screen.FromPoint(
            new System.Drawing.Point(screenPoint.X, screenPoint.Y));
        var workingArea = screen.WorkingArea;
        var topLeft = PointFromScreen(
            new System.Windows.Point(workingArea.Left, workingArea.Top));
        var bottomRight = PointFromScreen(
            new System.Windows.Point(workingArea.Right, workingArea.Bottom));
        var clientWorkingArea = new Rect(topLeft, bottomRight);

        var visibleWidth = Math.Min(
            MinimumVisibleCharacterExtent,
            Math.Min(_pointerStartHitRegion.Width, clientWorkingArea.Width / 2.0));
        var visibleHeight = Math.Min(
            MinimumVisibleCharacterExtent,
            Math.Min(_pointerStartHitRegion.Height, clientWorkingArea.Height / 2.0));
        var minimumX = clientWorkingArea.Left + visibleWidth - _pointerStartHitRegion.Right;
        var maximumX = clientWorkingArea.Right - visibleWidth - _pointerStartHitRegion.Left;
        var minimumY = clientWorkingArea.Top + visibleHeight - _pointerStartHitRegion.Bottom;
        var maximumY = clientWorkingArea.Bottom - visibleHeight - _pointerStartHitRegion.Top;

        return new System.Windows.Vector(
            Math.Clamp(delta.X, minimumX, maximumX),
            Math.Clamp(delta.Y, minimumY, maximumY));
    }

    private void CancelPointerInteraction()
    {
        if (_pointerDragging &&
            !string.IsNullOrWhiteSpace(_pointerCharacterId) &&
            _characterStates.TryGetValue(_pointerCharacterId, out var state))
        {
            FlushPendingDragPosition();
            SendToJs("endCharacterDrag", new { id = _pointerCharacterId });
            CharacterPositionCommitted?.Invoke(
                _pointerCharacterId,
                state.Config.PositionX,
                state.Config.PositionY);
        }

        ResetPointerInteraction();
    }

    private void ResetPointerInteraction()
    {
        StopDragRendering();
        _pointerArmed = false;
        _pointerDragging = false;
        _pointerReleased = false;
        _pointerCharacterId = null;
        _pointerStartHitRegion = Rect.Empty;
        _pointerMovePending = false;
    }

    private bool ShouldBlockScreenPoint(int screenX, int screenY)
    {
        if (_isConfigMode ||
            !IsVisible ||
            !HasVisibleOrLoadingCharacters() ||
            _hitRegions.Count == 0)
        {
            return false;
        }

        var clientPoint = PointFromScreen(new System.Windows.Point(screenX, screenY));
        return _hitRegions.Values.Any(region => region.Contains(clientPoint));
    }

    private void RemoveMouseHook()
    {
        if (_mouseHook == IntPtr.Zero)
            return;

        UnhookWindowsHookEx(_mouseHook);
        _mouseHook = IntPtr.Zero;
    }

    private static int GetSignedLoWord(IntPtr value) =>
        unchecked((short)((long)value & 0xffff));

    private static int GetSignedHiWord(IntPtr value) =>
        unchecked((short)(((long)value >> 16) & 0xffff));

    private void SendToJs(string type, object? data = null)
    {
        if (WebView.CoreWebView2 == null)
            return;

        WebView.CoreWebView2.PostWebMessageAsString(JsonSerializer.Serialize(new { type, data }));
    }

    private double ToSceneX(double left)
    {
        return left + (LegacyViewportWidth / 2.0) - Left;
    }

    private double ToSceneY(double top)
    {
        return top + LegacyViewportHeight - LegacyBottomMargin - Top;
    }
}
