using System.Diagnostics;
using System.Drawing;
using System.Windows;
using System.Windows.Threading;
using Spine;
using SpinePet.Infrastructure;
using SpinePet.Models;
using SpinePet.Services;

namespace SpinePet.Rendering.Native;

public sealed class NativeCharacterRenderHost :
    ICharacterRenderHost,
    IDisposable
{
    private const int WindowRegionPadding = 12;
    private const double DefaultScale = 0.2;
    private const double MaximumScale = 2.0;
    private const double MinimumScale = 0.05;
    private const double CharacterBottomMargin = 24;
    private const int WmMouseMove = 0x0200;
    private const int WmLeftButtonDown = 0x0201;
    private const int WmLeftButtonUp = 0x0202;
    private const int WmCaptureChanged = 0x0215;

    private readonly Dictionary<string, NativeCharacterState> _states =
        new(StringComparer.Ordinal);
    private readonly NativeCharacterZOrder _zOrder = new();
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _frameTimer;
    private readonly DispatcherTimer _scaleSettleTimer;
    private readonly Stopwatch _frameClock = new();
    private readonly HashSet<string> _scaleShrinkPending =
        new(StringComparer.Ordinal);
    private NativeCompositionWindow? _window;
    private NativeGraphicsDevice? _graphics;
    private Task? _initializationTask;
    private bool _configMode;
    private bool _renderDragEnabled = true;
    private bool _renderingFrame;
    private bool _compositionDirty;
    private bool _closed;
    private string? _pointerCharacterId;
    private NativePoint _pointerStart;
    private NativePoint _pointerLatest;
    private double _pointerStartX;
    private double _pointerStartY;
    private bool _pointerDragging;
    private bool _pointerMovePending;
    private int _explicitMoveDepth;

    public NativeCharacterRenderHost()
    {
        _dispatcher = System.Windows.Application.Current?.Dispatcher ??
            Dispatcher.CurrentDispatcher;
        _frameTimer = new DispatcherTimer(
            DispatcherPriority.Background,
            _dispatcher)
        {
            Interval = TimeSpan.FromSeconds(1.0 / 60.0)
        };
        _frameTimer.Tick += OnFrame;
        _scaleSettleTimer = new DispatcherTimer(
            DispatcherPriority.Background,
            _dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _scaleSettleTimer.Tick += OnScaleSettle;
    }

    public event Action<string, double, double>? CharacterScaleChanged;
    public event Action<string, IReadOnlyList<string>>?
        CharacterAnimationsLoaded;
    public event Action<string>? CharacterLoadFailed;
    public event Action? CharactersStateChanged;
    public event Action<string, double, double>? CharacterPositionCommitted;

    public bool IsCharacterLoading(string characterId) =>
        _states.TryGetValue(characterId, out NativeCharacterState? state) &&
        state.IsLoading;

    public bool IsCharacterVisible(string characterId) =>
        _states.TryGetValue(characterId, out NativeCharacterState? state) &&
        state.IsVisible;

    public IReadOnlyList<string> GetAnimationNames(string characterId) =>
        _states.TryGetValue(characterId, out NativeCharacterState? state) &&
        state.Resource != null
            ? state.Resource.AnimationNames
            : Array.Empty<string>();

    public double GetMaxScale(string characterId) =>
        _states.TryGetValue(characterId, out NativeCharacterState? state)
            ? state.MaxScale
            : MaximumScale;

    public double GetCurrentScale(string characterId) =>
        _states.TryGetValue(characterId, out NativeCharacterState? state)
            ? state.CurrentScale
            : DefaultScale;

    public Task InitializeAsync()
    {
        _initializationTask ??= _dispatcher.CheckAccess()
            ? InitializeOnDispatcher()
            : _dispatcher.InvokeAsync(InitializeOnDispatcher).Task.Unwrap();
        return _initializationTask;
    }

    public async Task ShowCharacterAsync(
        CharacterConfig character,
        bool configMode,
        double speed)
    {
        await InitializeAsync();
        _configMode = configMode;

        NativeCharacterState state = GetOrCreateState(character);
        state.Config = character;
        state.CurrentScale = Math.Clamp(
            character.Scale > 0 ? character.Scale : DefaultScale,
            MinimumScale,
            state.MaxScale);
        state.Config.Scale = state.CurrentScale;
        state.Config.AnimationSpeed = Math.Clamp(speed, 0.1, 2);
        state.IsVisible = true;
        state.Config.Visible = true;

        if (state.Resource != null)
        {
            if (state.Surface?.SetVisible(true) == true)
                _zOrder.MoveToTop(state.Config.Id);
            SelectModeAnimation(state);
            UpdateSurfacePosition(state);
            _compositionDirty = true;
            RenderFrame(0);
            CharactersStateChanged?.Invoke();
            return;
        }

        state.IsLoading = true;
        int loadVersion = ++state.LoadVersion;
        CharactersStateChanged?.Invoke();

        try
        {
            var loaded = await Task.Run(() =>
            {
                NativeSpineResource resource =
                    NativeSpineResource.Load(character);
                try
                {
                    var bounds =
                        NativeSpineEnvelopeCalculator.Calculate(
                            resource.SkeletonData);
                    return (resource, bounds.Setup, bounds.Envelope);
                }
                catch
                {
                    resource.Dispose();
                    throw;
                }
            });

            await _dispatcher.InvokeAsync(() =>
            {
                if (!_states.TryGetValue(
                        character.Id,
                        out NativeCharacterState? current) ||
                    !ReferenceEquals(current, state) ||
                    current.LoadVersion != loadVersion)
                {
                    loaded.resource.Dispose();
                    return;
                }

                current.Resource = loaded.resource;
                current.SetupBounds = loaded.Setup;
                current.Envelope = loaded.Envelope;
                current.Surface = _graphics!.CreateSurface();
                if (current.Surface.SetVisible(true))
                    _zOrder.MoveToTop(current.Config.Id);
                current.IsLoading = false;
                SelectModeAnimation(current);
                UpdateSurfacePosition(current);
                _compositionDirty = true;
                RenderFrame(0);

                if (string.IsNullOrWhiteSpace(
                        current.Config.ConfiguredAnimation) &&
                    current.Resource.AnimationNames.Count > 0)
                {
                    current.Config.ConfiguredAnimation =
                        current.Resource.AnimationNames[0];
                }

                CharacterAnimationsLoaded?.Invoke(
                    character.Id,
                    current.Resource.AnimationNames);
                CharacterScaleChanged?.Invoke(
                    character.Id,
                    current.MaxScale,
                    current.CurrentScale);
                CharactersStateChanged?.Invoke();
            });
        }
        catch (Exception exception)
        {
            await _dispatcher.InvokeAsync(() =>
            {
                if (!_states.TryGetValue(
                        character.Id,
                        out NativeCharacterState? current) ||
                    !ReferenceEquals(current, state) ||
                    current.LoadVersion != loadVersion)
                {
                    return;
                }

                current.IsLoading = false;
                current.IsVisible = false;
                current.Config.Visible = false;
                AppLogger.Write(
                    nameof(NativeCharacterRenderHost),
                    $"character-load-failed id={character.Id} message={exception.Message}");
                CharacterLoadFailed?.Invoke(character.Id);
                CharactersStateChanged?.Invoke();
            });
            throw;
        }
    }

    public void HideCharacter(string characterId)
    {
        if (!_states.TryGetValue(
                characterId,
                out NativeCharacterState? state))
        {
            return;
        }

        CancelPointerIfCharacter(characterId);
        state.IsVisible = false;
        state.IsLoading = false;
        state.Config.Visible = false;
        state.Surface?.SetVisible(false);
        CommitComposition();
        CharactersStateChanged?.Invoke();
    }

    public void RemoveCharacter(string characterId)
    {
        if (!_states.Remove(
                characterId,
                out NativeCharacterState? state))
        {
            return;
        }

        CancelPointerIfCharacter(characterId);
        state.LoadVersion++;
        _scaleShrinkPending.Remove(characterId);
        _zOrder.Remove(characterId);
        state.Dispose();
        PurgeUnusedTextures();
        CommitComposition();
        CharactersStateChanged?.Invoke();
    }

    public void SetCharacterScale(string characterId, double scale)
    {
        if (!_states.TryGetValue(
                characterId,
                out NativeCharacterState? state))
        {
            return;
        }

        state.CurrentScale = Math.Clamp(
            scale,
            MinimumScale,
            state.MaxScale);
        state.Config.Scale = state.CurrentScale;
        _scaleShrinkPending.Add(characterId);
        _scaleSettleTimer.Stop();
        _scaleSettleTimer.Start();
        CharacterScaleChanged?.Invoke(
            characterId,
            state.MaxScale,
            state.CurrentScale);
    }

    public void SetCharacterSpeed(string characterId, double speed)
    {
        if (!_states.TryGetValue(
                characterId,
                out NativeCharacterState? state))
        {
            return;
        }

        state.Config.AnimationSpeed = Math.Clamp(speed, 0.1, 2);
        if (state.Resource != null)
            state.Resource.AnimationState.TimeScale =
                (float)state.Config.AnimationSpeed;
    }

    public void PlayCharacterAnimation(
        string characterId,
        string animation,
        bool repeat)
    {
        if (!_states.TryGetValue(
                characterId,
                out NativeCharacterState? state) ||
            state.Resource == null ||
            state.Resource.SkeletonData.FindAnimation(animation) == null)
        {
            return;
        }

        state.Config.ConfiguredAnimation = animation;
        state.Resource.SetAnimation(animation, repeat);
    }

    public void SetConfigMode(bool configMode)
    {
        _configMode = configMode;
        CancelPointerInteraction(commitPosition: false);
        UpdateWindowInputState();
        foreach (NativeCharacterState state in _states.Values)
            SelectModeAnimation(state);
    }

    public void SetRenderDragEnabled(bool enabled)
    {
        _renderDragEnabled = enabled;
        if (!enabled && _pointerDragging)
            CancelPointerInteraction(commitPosition: true);
    }

    public void MoveCharacter(
        string characterId,
        double left,
        double top)
    {
        if (!_states.TryGetValue(
                characterId,
                out NativeCharacterState? state))
        {
            return;
        }

        state.Config.PositionX = left;
        state.Config.PositionY = top;
        UpdateSurfacePosition(state);
        _compositionDirty = true;
    }

    public void BeginCharacterMove()
    {
        _explicitMoveDepth++;
    }

    public void EndCharacterMove()
    {
        if (_explicitMoveDepth > 0)
            _explicitMoveDepth--;
        CommitComposition();
    }

    public void ResetCharacterPosition(string characterId)
    {
        if (!_states.TryGetValue(
                characterId,
                out NativeCharacterState? state))
        {
            return;
        }

        Rect area = SystemParameters.WorkArea;
        MoveCharacter(
            characterId,
            area.Left + area.Width / 2,
            area.Bottom - CharacterBottomMargin);
        CommitComposition();
        CharacterPositionCommitted?.Invoke(
            characterId,
            state.Config.PositionX,
            state.Config.PositionY);
    }

    public void HideAll()
    {
        CancelPointerInteraction(commitPosition: false);
        foreach (NativeCharacterState state in _states.Values)
        {
            state.IsVisible = false;
            state.IsLoading = false;
            state.Config.Visible = false;
            state.Surface?.SetVisible(false);
        }

        CommitComposition();
        CharactersStateChanged?.Invoke();
    }

    public async Task RestoreVisibleCharactersAsync(
        IEnumerable<CharacterConfig> characters,
        bool configMode)
    {
        foreach (CharacterConfig character in characters.Where(
                     character => character.Visible))
        {
            await ShowCharacterAsync(
                character,
                configMode,
                character.AnimationSpeed);
        }
    }

    public void Close()
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Invoke(Close);
            return;
        }

        if (_closed)
            return;
        _closed = true;

        _frameTimer.Stop();
        _scaleSettleTimer.Stop();
        foreach (NativeCharacterState state in _states.Values)
            state.Dispose();
        _states.Clear();
        _zOrder.Clear();
        _graphics?.Dispose();
        _graphics = null;
        _window?.Dispose();
        _window = null;
    }

    public void Dispose()
    {
        Close();
    }

    private Task InitializeOnDispatcher()
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        if (_graphics != null)
            return Task.CompletedTask;

        _window = new NativeCompositionWindow();
        _window.HitTestScreenPoint = HitTestScreenPoint;
        _window.MouseInput = OnNativeMouseInput;
        _graphics = new NativeGraphicsDevice(_window.Handle);
        _frameClock.Restart();
        _frameTimer.Start();
        AppLogger.Write(
            nameof(NativeCharacterRenderHost),
            $"initialized window={_window.Handle} size={_window.Width}x{_window.Height} dpi={_window.DpiScale:F2}");
        return Task.CompletedTask;
    }

    private NativeCharacterState GetOrCreateState(CharacterConfig character)
    {
        if (_states.TryGetValue(
                character.Id,
                out NativeCharacterState? existing))
        {
            return existing;
        }

        NativeCharacterState state = new()
        {
            Config = character,
            CurrentScale = Math.Clamp(
                character.Scale > 0
                    ? character.Scale
                    : DefaultScale,
                MinimumScale,
                MaximumScale),
            MaxScale = MaximumScale
        };
        _states[character.Id] = state;
        _zOrder.MoveToTop(character.Id);
        return state;
    }

    private void OnFrame(object? sender, EventArgs eventArgs)
    {
        double elapsed = _frameClock.Elapsed.TotalSeconds;
        _frameClock.Restart();
        RenderFrame(Math.Min(elapsed, 0.1));
    }

    private void OnScaleSettle(object? sender, EventArgs eventArgs)
    {
        _scaleSettleTimer.Stop();
        if (_window == null)
            return;

        foreach (string characterId in _scaleShrinkPending)
        {
            if (!_states.TryGetValue(
                    characterId,
                    out NativeCharacterState? state) ||
                !state.IsVisible ||
                state.Surface == null)
            {
                continue;
            }

            state.Surface.ShrinkToScale(
                state.Envelope,
                state.PivotX,
                state.PivotY,
                (float)state.CurrentScale * _window.DpiScale);
        }

        _scaleShrinkPending.Clear();
        RenderFrame(0);
    }

    private void RenderFrame(double elapsedSeconds)
    {
        if (_renderingFrame || _graphics == null || _window == null)
            return;

        _renderingFrame = true;
        try
        {
            FlushPendingPointerMove();
            List<(
                NativeCharacterState State,
                IReadOnlyList<NativeSpineDrawBatch> Batches,
                float PixelScale)> pendingFrames = [];
            foreach (NativeCharacterState state in _states.Values)
            {
                if (!state.IsVisible ||
                    state.Resource == null ||
                    state.Surface == null)
                {
                    continue;
                }

                state.Resource.AnimationState.TimeScale =
                    (float)state.Config.AnimationSpeed;
                state.Resource.Update((float)elapsedSeconds);
                IReadOnlyList<NativeSpineDrawBatch> batches =
                    state.Geometry.Build(state.Resource.Skeleton);
                float pixelScale =
                    (float)state.CurrentScale * _window.DpiScale;
                state.Surface.EnsureSize(
                    state.Envelope,
                    state.PivotX,
                    state.PivotY,
                    pixelScale);
                UpdateSurfacePosition(state);
                UpdateScreenBounds(state, batches, pixelScale);
                state.LastBatches = batches;
                pendingFrames.Add((state, batches, pixelScale));
            }

            UpdateWindowRegions();
            foreach (var frame in pendingFrames)
            {
                _graphics.Render(
                    frame.State.Surface!,
                    frame.Batches,
                    frame.State.Surface!.GetTransform(frame.PixelScale));
            }

            bool rendered = pendingFrames.Count > 0;
            if (rendered || _compositionDirty)
            {
                _graphics.Commit();
                _compositionDirty = false;
            }
        }
        catch (Exception exception)
        {
            AppLogger.Write(
                nameof(NativeCharacterRenderHost),
                $"frame-failed message={exception.Message}");
        }
        finally
        {
            UpdateWindowInputState();
            _renderingFrame = false;
        }
    }

    private void UpdateSurfacePosition(NativeCharacterState state)
    {
        if (_window == null || state.Surface == null)
            return;

        state.Surface.SetAnchorPosition(
            ToClientPixelX(state.Config.PositionX),
            ToClientPixelY(state.Config.PositionY));
    }

    private void UpdateScreenBounds(
        NativeCharacterState state,
        IReadOnlyList<NativeSpineDrawBatch> batches,
        float pixelScale)
    {
        if (_window == null)
            return;

        NativeSpineBounds bounds =
            NativeSpineEnvelopeCalculator.GetBounds(batches);
        float anchorX =
            _window.Left + ToClientPixelX(state.Config.PositionX);
        float anchorY =
            _window.Top + ToClientPixelY(state.Config.PositionY);
        state.PreviousWindowRegionBounds =
            state.WindowRegionBounds;
        state.WindowRegionBounds =
            GetSurfaceScreenBounds(
                state.Surface,
                anchorX,
                anchorY);
        if (bounds.IsEmpty)
        {
            state.ScreenBounds = RectangleF.Empty;
            return;
        }

        state.ScreenBounds = RectangleF.FromLTRB(
            anchorX + (bounds.Left - state.PivotX) * pixelScale,
            anchorY + (bounds.Top - state.PivotY) * pixelScale,
            anchorX + (bounds.Right - state.PivotX) * pixelScale,
            anchorY + (bounds.Bottom - state.PivotY) * pixelScale);
    }

    private float ToClientPixelX(double x) =>
        _window == null
            ? 0
            : (float)((x - SystemParameters.VirtualScreenLeft) *
                      _window.DpiScale);

    private float ToClientPixelY(double y) =>
        _window == null
            ? 0
            : (float)((y - SystemParameters.VirtualScreenTop) *
                      _window.DpiScale);

    private void CommitComposition()
    {
        if (_graphics == null)
            return;
        _graphics.Commit();
        _compositionDirty = false;
    }

    private void SelectModeAnimation(NativeCharacterState state)
    {
        NativeSpineResource? resource = state.Resource;
        if (resource == null)
            return;

        if (_configMode)
        {
            resource.SetAnimation(
                state.Config.ConfiguredAnimation,
                true);
            return;
        }

        string? animation =
            resource.SkeletonData.FindAnimation("idle") != null
                ? "idle"
                : resource.AnimationNames.Count > 0
                    ? resource.AnimationNames[0]
                    : null;
        resource.SetAnimation(animation, true);
    }

    private static void PlayClickAnimation(NativeCharacterState state)
    {
        NativeSpineResource? resource = state.Resource;
        if (resource?.SkeletonData.FindAnimation("action") == null)
            return;

        resource.AnimationState.SetAnimation(0, "action", false);
        if (resource.SkeletonData.FindAnimation("idle") != null)
            resource.AnimationState.AddAnimation(0, "idle", true, 0);
    }

    private static void PlayDragAnimation(NativeCharacterState state)
    {
        NativeSpineResource? resource = state.Resource;
        if (resource == null)
            return;

        string[] candidates = resource.AnimationNames
            .Where(name =>
                !name.Equals("idle", StringComparison.OrdinalIgnoreCase) &&
                !name.Equals("action", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (candidates.Length == 0)
            return;

        resource.SetAnimation(
            candidates[Random.Shared.Next(candidates.Length)],
            true);
    }

    private bool HitTestScreenPoint(int x, int y)
    {
        if (_closed || _configMode)
            return false;

        NativePoint point = new() { X = x, Y = y };
        bool hit = TryHitCharacter(point, out _);
        if (!hit && _pointerCharacterId == null)
            UpdateWindowRegions(point);
        return hit;
    }

    private void UpdateWindowRegions(
        NativePoint? knownTransparentPoint = null)
    {
        if (_window == null)
            return;

        List<Rectangle> regions = [];
        Rectangle[] workingAreas =
            System.Windows.Forms.Screen.AllScreens
                .Select(screen => ToClientPixelRectangle(
                    screen.WorkingArea,
                    _window.DpiScale,
                    _window.Left,
                    _window.Top))
                .ToArray();
        foreach (NativeCharacterState state in _states.Values)
        {
            if (!state.IsVisible)
                continue;

            foreach (RectangleF screenBounds in new[]
                     {
                         state.PreviousWindowRegionBounds,
                         state.WindowRegionBounds
                     })
            {
                Rectangle visibleBounds = GetWindowRegionBounds(
                    screenBounds,
                    _window.Left,
                    _window.Top);
                foreach (Rectangle visibleRegion in ClipToWorkingAreas(
                             visibleBounds,
                             workingAreas))
                {
                    regions.Add(visibleRegion);
                }
            }
        }

        NativePoint? passThroughPoint = knownTransparentPoint;
        if (passThroughPoint == null &&
            !_configMode &&
            _pointerCharacterId == null &&
            NativeCompositionWindow.TryGetCursorPosition(
                out int cursorX,
                out int cursorY))
        {
            NativePoint cursor = new() { X = cursorX, Y = cursorY };
            if (!TryHitCharacter(cursor, out _))
                passThroughPoint = cursor;
        }

        Rectangle? passThroughHole = null;
        if (passThroughPoint is { } screenPoint)
        {
            int clientX = screenPoint.X - _window.Left;
            int clientY = screenPoint.Y - _window.Top;
            if (regions.Any(region => region.Contains(clientX, clientY)))
            {
                passThroughHole =
                    new Rectangle(clientX, clientY, 1, 1);
            }
        }

        _window.SetInteractiveRegions(
            regions,
            passThroughHole);
    }

    internal static RectangleF GetSurfaceScreenBounds(
        NativeCompositionSurface? surface,
        float anchorX,
        float anchorY)
    {
        if (surface == null ||
            surface.PixelWidth <= 0 ||
            surface.PixelHeight <= 0)
        {
            return RectangleF.Empty;
        }

        return new RectangleF(
            anchorX - surface.AnchorPixelX,
            anchorY - surface.AnchorPixelY,
            surface.PixelWidth,
            surface.PixelHeight);
    }

    internal static Rectangle GetWindowRegionBounds(
        RectangleF screenBounds,
        int windowLeft,
        int windowTop)
    {
        if (screenBounds.IsEmpty)
            return Rectangle.Empty;

        return Rectangle.FromLTRB(
            (int)Math.Floor(screenBounds.Left - windowLeft) -
            WindowRegionPadding,
            (int)Math.Floor(screenBounds.Top - windowTop) -
            WindowRegionPadding,
            (int)Math.Ceiling(screenBounds.Right - windowLeft) +
            WindowRegionPadding,
            (int)Math.Ceiling(screenBounds.Bottom - windowTop) +
            WindowRegionPadding);
    }

    internal static IReadOnlyList<Rectangle> ClipToWorkingAreas(
        Rectangle characterRegion,
        IEnumerable<Rectangle> workingAreas)
    {
        return workingAreas
            .Select(area => Rectangle.Intersect(characterRegion, area))
            .Where(region => region.Width > 0 && region.Height > 0)
            .ToArray();
    }

    internal static Rectangle ToClientPixelRectangle(
        Rectangle logicalScreenRectangle,
        float dpiScale,
        int windowLeft,
        int windowTop)
    {
        return Rectangle.FromLTRB(
            (int)Math.Floor(
                logicalScreenRectangle.Left * dpiScale -
                windowLeft),
            (int)Math.Floor(
                logicalScreenRectangle.Top * dpiScale -
                windowTop),
            (int)Math.Ceiling(
                logicalScreenRectangle.Right * dpiScale -
                windowLeft),
            (int)Math.Ceiling(
                logicalScreenRectangle.Bottom * dpiScale -
                windowTop));
    }

    private void UpdateWindowInputState()
    {
        if (_window == null)
            return;

        bool shouldEnable =
            !_configMode &&
            (_pointerCharacterId != null ||
             (NativeCompositionWindow.TryGetCursorPosition(
                  out int x,
                  out int y) &&
              TryHitCharacter(
                  new NativePoint { X = x, Y = y },
                  out _)));
        _window.SetInputEnabled(shouldEnable);
    }

    private void OnNativeMouseInput(
        uint nativeMessage,
        int x,
        int y)
    {
        if (_closed)
            return;

        int message = checked((int)nativeMessage);
        if (message != WmLeftButtonDown &&
            message != WmMouseMove &&
            message != WmLeftButtonUp &&
            message != WmCaptureChanged)
        {
            return;
        }

        NativePoint point = new() { X = x, Y = y };
        if (message == WmLeftButtonDown &&
            !_configMode &&
            TryHitCharacter(point, out NativeCharacterState? state))
        {
            _pointerCharacterId = state.Config.Id;
            _pointerStart = point;
            _pointerLatest = point;
            _pointerStartX = state.Config.PositionX;
            _pointerStartY = state.Config.PositionY;
            _pointerDragging = false;
            _pointerMovePending = false;
            return;
        }

        if (_pointerCharacterId != null && message == WmMouseMove)
        {
            _pointerLatest = point;
            int deltaX = point.X - _pointerStart.X;
            int deltaY = point.Y - _pointerStart.Y;
            if (!_pointerDragging &&
                _renderDragEnabled &&
                (Math.Abs(deltaX) >=
                     SystemParameters.MinimumHorizontalDragDistance *
                     (_window?.DpiScale ?? 1) ||
                 Math.Abs(deltaY) >=
                     SystemParameters.MinimumVerticalDragDistance *
                     (_window?.DpiScale ?? 1)))
            {
                _pointerDragging = true;
                BeginCharacterMove();
                if (_states.TryGetValue(
                        _pointerCharacterId,
                        out NativeCharacterState? dragState))
                {
                    PlayDragAnimation(dragState);
                }
            }

            if (_pointerDragging)
                _pointerMovePending = true;
            return;
        }

        if (_pointerCharacterId != null && message == WmLeftButtonUp)
        {
            _pointerLatest = point;
            _pointerMovePending = _pointerDragging;
            string characterId = _pointerCharacterId;
            FlushPendingPointerMove();
            if (_states.TryGetValue(
                    characterId,
                    out NativeCharacterState? releasedState))
            {
                if (_pointerDragging)
                {
                    EndCharacterMove();
                    SelectModeAnimation(releasedState);
                    CharacterPositionCommitted?.Invoke(
                        characterId,
                        releasedState.Config.PositionX,
                        releasedState.Config.PositionY);
                }
                else
                {
                    PlayClickAnimation(releasedState);
                }
            }

            ResetPointerState();
            CommitComposition();
            return;
        }

        if (_pointerCharacterId != null && message == WmCaptureChanged)
            CancelPointerInteraction(commitPosition: true);
    }

    private bool TryHitCharacter(
        NativePoint point,
        out NativeCharacterState result)
    {
        foreach (string characterId in _zOrder.TopToBottom)
        {
            if (!_states.TryGetValue(
                    characterId,
                    out NativeCharacterState? state))
            {
                continue;
            }

            if (state.IsVisible &&
                !state.ScreenBounds.IsEmpty &&
                state.ScreenBounds.Contains(point.X, point.Y) &&
                HitTestGeometry(state, point))
            {
                result = state;
                return true;
            }
        }

        result = null!;
        return false;
    }

    private bool HitTestGeometry(
        NativeCharacterState state,
        NativePoint point)
    {
        if (_window == null || state.LastBatches.Count == 0)
            return false;

        float pixelScale =
            (float)state.CurrentScale * _window.DpiScale;
        if (pixelScale <= 0)
            return false;

        float anchorX =
            _window.Left + ToClientPixelX(state.Config.PositionX);
        float anchorY =
            _window.Top + ToClientPixelY(state.Config.PositionY);
        float x = state.PivotX + (point.X - anchorX) / pixelScale;
        float y = state.PivotY + (point.Y - anchorY) / pixelScale;

        for (int batchIndex = state.LastBatches.Count - 1;
             batchIndex >= 0;
             batchIndex--)
        {
            NativeSpineDrawBatch batch = state.LastBatches[batchIndex];
            for (int triangle = batch.Indices.Length - 3;
                 triangle >= 0;
                 triangle -= 3)
            {
                NativeSpineVertex first =
                    batch.Vertices[batch.Indices[triangle]];
                NativeSpineVertex second =
                    batch.Vertices[batch.Indices[triangle + 1]];
                NativeSpineVertex third =
                    batch.Vertices[batch.Indices[triangle + 2]];
                if (!TryGetBarycentric(
                        x,
                        y,
                        first.Position,
                        second.Position,
                        third.Position,
                        out float firstWeight,
                        out float secondWeight,
                        out float thirdWeight))
                {
                    continue;
                }

                float u =
                    first.TextureCoordinate.X * firstWeight +
                    second.TextureCoordinate.X * secondWeight +
                    third.TextureCoordinate.X * thirdWeight;
                float v =
                    first.TextureCoordinate.Y * firstWeight +
                    second.TextureCoordinate.Y * secondWeight +
                    third.TextureCoordinate.Y * thirdWeight;
                float opacity =
                    first.LightColor.W * firstWeight +
                    second.LightColor.W * secondWeight +
                    third.LightColor.W * thirdWeight;
                if (batch.Texture.IsVisiblePixel(u, v, opacity))
                    return true;
            }
        }

        return false;
    }

    private static bool TryGetBarycentric(
        float x,
        float y,
        System.Numerics.Vector2 first,
        System.Numerics.Vector2 second,
        System.Numerics.Vector2 third,
        out float firstWeight,
        out float secondWeight,
        out float thirdWeight)
    {
        float denominator =
            (second.Y - third.Y) * (first.X - third.X) +
            (third.X - second.X) * (first.Y - third.Y);
        if (MathF.Abs(denominator) < 0.00001f)
        {
            firstWeight = 0;
            secondWeight = 0;
            thirdWeight = 0;
            return false;
        }

        firstWeight =
            ((second.Y - third.Y) * (x - third.X) +
             (third.X - second.X) * (y - third.Y)) /
            denominator;
        secondWeight =
            ((third.Y - first.Y) * (x - third.X) +
             (first.X - third.X) * (y - third.Y)) /
            denominator;
        thirdWeight = 1 - firstWeight - secondWeight;
        const float tolerance = -0.0001f;
        return firstWeight >= tolerance &&
               secondWeight >= tolerance &&
               thirdWeight >= tolerance;
    }

    private void PurgeUnusedTextures()
    {
        if (_graphics == null)
            return;

        HashSet<string> activePaths = new(
            _states.Values
                .Where(state => state.Resource != null)
                .SelectMany(state =>
                    state.Resource!.TextureLoader.Textures)
                .Select(texture => texture.Path),
            StringComparer.OrdinalIgnoreCase);
        _graphics.PurgeTextures(activePaths);
    }

    private void FlushPendingPointerMove()
    {
        if (!_pointerMovePending ||
            !_pointerDragging ||
            _pointerCharacterId == null ||
            _window == null)
        {
            return;
        }

        _pointerMovePending = false;
        MoveCharacter(
            _pointerCharacterId,
            _pointerStartX +
            (_pointerLatest.X - _pointerStart.X) / _window.DpiScale,
            _pointerStartY +
            (_pointerLatest.Y - _pointerStart.Y) / _window.DpiScale);
    }

    private void CancelPointerIfCharacter(string characterId)
    {
        if (string.Equals(
                _pointerCharacterId,
                characterId,
                StringComparison.Ordinal))
        {
            CancelPointerInteraction(commitPosition: false);
        }
    }

    private void CancelPointerInteraction(bool commitPosition)
    {
        if (_pointerCharacterId != null &&
            _states.TryGetValue(
                _pointerCharacterId,
                out NativeCharacterState? state))
        {
            FlushPendingPointerMove();
            if (_pointerDragging)
            {
                EndCharacterMove();
                SelectModeAnimation(state);
                if (commitPosition)
                {
                    CharacterPositionCommitted?.Invoke(
                        state.Config.Id,
                        state.Config.PositionX,
                        state.Config.PositionY);
                }
            }
        }

        ResetPointerState();
    }

    private void ResetPointerState()
    {
        _pointerCharacterId = null;
        _pointerDragging = false;
        _pointerMovePending = false;
    }

    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}
