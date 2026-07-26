using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using SpinePet.Services;
using SpinePet.ViewModels;

namespace SpinePet.Views;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private const double DefaultMaxScale = 2.0;
    private const double DefaultScale = 0.2;
    private const double MinimumScale = 0.05;
    private const double MinimumMaximumScale = 0.2;

    private readonly CharacterManager _characterManager;
    private readonly CharacterResourceDiscoveryService _resourceDiscovery;
    private OverlayWindow? _overlayWindow;
    private bool _isRefreshingSelection;
    private bool _isConfigMode = true;
    private CharacterViewModel? _selectedCharacter;
    private string _selectedAnimation = string.Empty;
    private double _selectedScale = DefaultScale;
    private double _selectedScaleMax = DefaultMaxScale;
    private double _selectedScalePercent;
    private double _selectedSpeed = 100;
    private bool _allowRenderDrag;

    public MainWindow(
        CharacterManager characterManager,
        CharacterResourceDiscoveryService resourceDiscovery)
    {
        InitializeComponent();
        _characterManager = characterManager;
        _resourceDiscovery = resourceDiscovery;
        _allowRenderDrag = characterManager.AllowRenderDrag;
        DataContext = this;

        RefreshCharacterList();
        _characterManager.CharactersChanged += RefreshCharacterList;
        _characterManager.CharacterScaleChanged += OnCharacterScaleChanged;

        LocationChanged += (_, _) => UpdateOverlayBounds();
        SizeChanged += (_, _) => UpdateOverlayBounds();
    }

    public ObservableCollection<CharacterViewModel> Characters { get; } = new();

    public ObservableCollection<string> SelectedAnimationNames { get; } = new();

    public CharacterViewModel? SelectedCharacter
    {
        get => _selectedCharacter;
        set
        {
            if (ReferenceEquals(_selectedCharacter, value))
            {
                return;
            }

            _selectedCharacter = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedCharacter));
        }
    }

    public bool HasSelectedCharacter => SelectedCharacter != null;

    public string SelectedAnimation
    {
        get => _selectedAnimation;
        set
        {
            if (_selectedAnimation == value)
            {
                return;
            }

            _selectedAnimation = value;
            OnPropertyChanged();
        }
    }

    public double SelectedScale
    {
        get => _selectedScale;
        set
        {
            if (NearlyEquals(_selectedScale, value))
            {
                return;
            }

            _selectedScale = value;
            OnPropertyChanged();
        }
    }

    public double SelectedScaleMax
    {
        get => _selectedScaleMax;
        set
        {
            double normalized = Math.Clamp(
                value,
                MinimumMaximumScale,
                DefaultMaxScale);
            if (NearlyEquals(_selectedScaleMax, normalized))
            {
                return;
            }

            _selectedScaleMax = normalized;
            OnPropertyChanged();
        }
    }

    public double SelectedScalePercent
    {
        get => _selectedScalePercent;
        set
        {
            double normalized = Math.Clamp(value, 0, 100);
            if (NearlyEquals(_selectedScalePercent, normalized))
            {
                return;
            }

            _selectedScalePercent = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedScaleDisplay));
        }
    }

    public string SelectedScaleDisplay => $"{SelectedScalePercent:F0}%";

    public double SelectedSpeed
    {
        get => _selectedSpeed;
        set
        {
            double normalized = Math.Clamp(value, 10, 200);
            if (NearlyEquals(_selectedSpeed, normalized))
            {
                return;
            }

            _selectedSpeed = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedSpeedDisplay));
        }
    }

    public string SelectedSpeedDisplay => $"{SelectedSpeed / 100.0:F2}x";

    public bool AllowRenderDrag
    {
        get => _allowRenderDrag;
        set
        {
            if (_allowRenderDrag == value)
            {
                return;
            }

            _allowRenderDrag = value;
            OnPropertyChanged();
            _characterManager.SetAllowRenderDrag(value);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void SwitchToConfigMode()
    {
        _isConfigMode = true;
        ApplyConfigMode();
        Show();
        Activate();
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        Rect workArea = SystemParameters.WorkArea;
        Width = 468;
        Left = workArea.Right - Width;
        Top = workArea.Top;
        Height = workArea.Height;
        EnsureOverlayWindow();
        ApplyConfigMode();
    }

    private void OnWindowChromeMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // The button can be released between the event and DragMove.
        }
    }

    private void ApplyConfigMode()
    {
        if (_isConfigMode)
        {
            ModeLabel.Text = "Configuration Mode";
            ShowOverlay();
            Topmost = true;
            Show();
            Activate();
        }
        else
        {
            HideOverlay();
            Hide();
        }

        _characterManager.SetConfigMode(_isConfigMode);
    }

    private void ClearSelection()
    {
        CharacterCards.SelectedItem = null;
        SelectedCharacter = null;
        SelectedAnimationNames.Clear();
        SelectedAnimation = string.Empty;
        SelectedScale = DefaultScale;
        SelectedScaleMax = DefaultMaxScale;
        SelectedScalePercent = 0;
        SelectedSpeed = 100;
        UpdateOverlayState();
    }

    private void EnsureOverlayWindow()
    {
        if (_overlayWindow != null)
        {
            return;
        }

        _overlayWindow = new OverlayWindow(_characterManager);
        _overlayWindow.CharacterMoved += OnOverlayCharacterMoved;
        UpdateOverlayBounds();
        UpdateOverlayState();
    }

    private void ShowOverlay()
    {
        EnsureOverlayWindow();
        UpdateOverlayBounds();
        UpdateOverlayState();
        _overlayWindow?.ShowOverlay();
    }

    private void HideOverlay()
    {
        _overlayWindow?.HideOverlay();
    }

    private void UpdateOverlayBounds()
    {
        if (_overlayWindow == null)
        {
            return;
        }

        Rect workArea = SystemParameters.WorkArea;
        double previewWidth = Math.Max(0, Left - workArea.Left);
        _overlayWindow.SetPreviewBounds(
            new Rect(workArea.Left, workArea.Top, previewWidth, workArea.Height));
    }

    private void UpdateOverlayState()
    {
        if (_overlayWindow == null)
        {
            return;
        }

        _overlayWindow.SelectedCharacterId = SelectedCharacter?.Id;
        _overlayWindow.MoveSelectedCharacter =
            MoveSelectedCheckBox?.IsChecked == true;
        if (!_overlayWindow.MoveSelectedCharacter)
        {
            _overlayWindow.CancelDrag();
        }
    }

    private void SyncSelectedCharacterState()
    {
        _isRefreshingSelection = true;
        SelectedAnimationNames.Clear();

        if (SelectedCharacter == null)
        {
            SelectedAnimation = string.Empty;
            SelectedScale = DefaultScale;
            SelectedScaleMax = DefaultMaxScale;
            SelectedScalePercent = 0;
            SelectedSpeed = 100;
            _isRefreshingSelection = false;
            UpdateOverlayState();
            return;
        }

        foreach (string animation in SelectedCharacter.AnimationNames)
        {
            SelectedAnimationNames.Add(animation);
        }

        SelectedScaleMax = Math.Clamp(
            SelectedCharacter.MaxScale > 0
                ? SelectedCharacter.MaxScale
                : DefaultMaxScale,
            MinimumMaximumScale,
            DefaultMaxScale);
        SelectedScale = Math.Clamp(
            SelectedCharacter.Scale > 0
                ? SelectedCharacter.Scale
                : DefaultScale,
            MinimumScale,
            SelectedScaleMax);
        SelectedScalePercent = ConvertScaleToPercent(
            SelectedScale,
            SelectedScaleMax);
        SelectedAnimation =
            !string.IsNullOrEmpty(SelectedCharacter.ConfiguredAnimation)
                ? SelectedCharacter.ConfiguredAnimation
                : SelectedAnimationNames.FirstOrDefault() ?? string.Empty;
        SelectedSpeed = Math.Clamp(
            SelectedCharacter.AnimationSpeed * 100.0,
            10,
            200);

        _isRefreshingSelection = false;
        UpdateOverlayState();
    }

    protected override void OnClosed(EventArgs e)
    {
        _characterManager.CharactersChanged -= RefreshCharacterList;
        _characterManager.CharacterScaleChanged -= OnCharacterScaleChanged;

        if (_overlayWindow != null)
        {
            _overlayWindow.CharacterMoved -= OnOverlayCharacterMoved;
            _overlayWindow.Close();
            _overlayWindow = null;
        }

        base.OnClosed(e);
    }

    private static bool NearlyEquals(double left, double right) =>
        Math.Abs(left - right) < 0.0001;

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
