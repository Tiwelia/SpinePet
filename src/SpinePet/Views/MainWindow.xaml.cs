using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using Button = System.Windows.Controls.Button;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ComboBox = System.Windows.Controls.ComboBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using MessageBox = System.Windows.MessageBox;
using SpinePet.Models;
using SpinePet.Services;

namespace SpinePet.Views;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private const double DefaultMaxScale = 2.0;
    private readonly PetManager _petManager;
    private OverlayWindow? _overlayWindow;
    private bool _isRefreshingSelection;
    public ObservableCollection<CharControl> Characters { get; } = new();
    public ObservableCollection<string> SelectedAnimationNames { get; } = new();
    private bool _isConfigMode = true;
    private CharControl? _selectedCharacter;
    private string _selectedAnimation = "";
    private double _selectedScale = 0.2;
    private double _selectedScaleMax = DefaultMaxScale;
    private double _selectedScalePercent = 0;
    private double _selectedSpeed = 100;

    public CharControl? SelectedCharacter
    {
        get => _selectedCharacter;
        set
        {
            if (_selectedCharacter == value) return;
            _selectedCharacter = value;
            OnChanged();
            OnChanged(nameof(HasSelectedCharacter));
        }
    }

    public bool HasSelectedCharacter => SelectedCharacter != null;

    public string SelectedAnimation
    {
        get => _selectedAnimation;
        set
        {
            if (_selectedAnimation == value) return;
            _selectedAnimation = value;
            OnChanged();
        }
    }

    public double SelectedScale
    {
        get => _selectedScale;
        set
        {
            if (Math.Abs(_selectedScale - value) < 0.0001) return;
            _selectedScale = value;
            OnChanged();
        }
    }

    public double SelectedScaleMax
    {
        get => _selectedScaleMax;
        set
        {
            var normalized = Math.Clamp(value, 0.20, DefaultMaxScale);
            if (Math.Abs(_selectedScaleMax - normalized) < 0.0001) return;
            _selectedScaleMax = normalized;
            OnChanged();
        }
    }

    public double SelectedScalePercent
    {
        get => _selectedScalePercent;
        set
        {
            var normalized = Math.Clamp(value, 0, 100);
            if (Math.Abs(_selectedScalePercent - normalized) < 0.0001) return;
            _selectedScalePercent = normalized;
            OnChanged();
            OnChanged(nameof(SelectedScaleDisplay));
        }
    }

    public string SelectedScaleDisplay => $"{SelectedScalePercent:F0}%";

    public double SelectedSpeed
    {
        get => _selectedSpeed;
        set
        {
            var normalized = Math.Clamp(value, 10, 200);
            if (Math.Abs(_selectedSpeed - normalized) < 0.0001) return;
            _selectedSpeed = normalized;
            OnChanged();
            OnChanged(nameof(SelectedSpeedDisplay));
        }
    }

    public string SelectedSpeedDisplay => $"{(SelectedSpeed / 100.0):F2}x";

    public MainWindow(PetManager petManager)
    {
        InitializeComponent();
        _petManager = petManager;
        DataContext = this;

        RefreshCharacterList();
        _petManager.CharactersChanged += RefreshCharacterList;
        _petManager.CharacterScaleChanged += OnCharacterScaleChanged;

        LocationChanged += (_, _) => UpdateOverlayBounds();
        SizeChanged += (_, _) => UpdateOverlayBounds();
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        var area = SystemParameters.WorkArea;
        Width = 468;
        Left = area.Right - Width;
        Top = area.Top;
        Height = area.Height;
        EnsureOverlayWindow();
        ApplyConfigMode();
    }

    public void SwitchToConfigMode()
    {
        _isConfigMode = true;
        ApplyConfigMode();
        Show();
        Activate();
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

        foreach (var ctrl in Characters)
        {
            _petManager.RenderHost.SetCharacterConfigMode(ctrl.Id, _isConfigMode);
        }
    }

    private void ClearSelection()
    {
        CharacterCards.SelectedItem = null;
        SelectedCharacter = null;
        SelectedAnimationNames.Clear();
        SelectedAnimation = "";
        SelectedScale = 0.2;
        SelectedScaleMax = DefaultMaxScale;
        SelectedScalePercent = 0;
        SelectedSpeed = 100;
        UpdateOverlayState();
    }

    private void EnsureOverlayWindow()
    {
        if (_overlayWindow != null) return;

        _overlayWindow = new OverlayWindow(_petManager);
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
        if (_overlayWindow == null) return;

        var area = SystemParameters.WorkArea;
        var previewWidth = Math.Max(0, Left - area.Left);
        _overlayWindow.SetPreviewBounds(new Rect(area.Left, area.Top, previewWidth, area.Height));
    }

    private void UpdateOverlayState()
    {
        if (_overlayWindow == null) return;

        _overlayWindow.SelectedCharacterId = SelectedCharacter?.Id;
        _overlayWindow.MoveSelectedCharacter = MoveSelectedCheckBox?.IsChecked == true;
        if (_overlayWindow.MoveSelectedCharacter == false)
            _overlayWindow.CancelDrag();
    }

    private void SyncSelectedCharacterState()
    {
        _isRefreshingSelection = true;
        SelectedAnimationNames.Clear();

        if (SelectedCharacter == null)
        {
            SelectedAnimation = "";
            SelectedScale = 0.2;
            SelectedScaleMax = DefaultMaxScale;
            SelectedScalePercent = 0;
            _isRefreshingSelection = false;
            UpdateOverlayState();
            return;
        }

        foreach (var animation in SelectedCharacter.AnimationNames)
        {
            SelectedAnimationNames.Add(animation);
        }

        SelectedScaleMax = Math.Clamp(SelectedCharacter.MaxScale > 0 ? SelectedCharacter.MaxScale : DefaultMaxScale, 0.20, DefaultMaxScale);
        SelectedScale = Math.Clamp(SelectedCharacter.Scale > 0 ? SelectedCharacter.Scale : 0.2, 0.05, SelectedScaleMax);
        SelectedScalePercent = ConvertScaleToPercent(SelectedScale, SelectedScaleMax);
        SelectedAnimation = !string.IsNullOrEmpty(SelectedCharacter.CurrentAnimation)
            ? SelectedCharacter.CurrentAnimation
            : SelectedAnimationNames.FirstOrDefault() ?? "";

        _isRefreshingSelection = false;
        UpdateOverlayState();
    }

    private void RefreshCharacterList()
    {
        var selectedId = SelectedCharacter?.Id;

        // Build lookup of existing controls by Id
        var existing = new Dictionary<string, CharControl>();
        foreach (var ctrl in Characters)
            existing[ctrl.Id] = ctrl;

        // Track current IDs to detect removals
        var currentIds = new HashSet<string>();

        foreach (var c in _petManager.Characters)
        {
            currentIds.Add(c.Id);
            var animNames = _petManager.RenderHost.GetAnimationNames(c.Id).ToList();
            var maxScale = Math.Clamp(_petManager.RenderHost.GetMaxScale(c.Id), 0.20, DefaultMaxScale);
            var scale = Math.Clamp(c.Scale > 0 ? c.Scale : 0.2, 0.05, maxScale);
            if (animNames.Count == 0 && !string.IsNullOrEmpty(c.CurrentAnimation))
                animNames = new List<string> { c.CurrentAnimation! };

            if (existing.TryGetValue(c.Id, out var ctrl))
            {
                // Update in-place — no UI flash
                ctrl.Name = c.Name;
                ctrl.TexturePath = c.TexturePath;
                ctrl.Scale = scale;
                ctrl.MaxScale = maxScale;
                ctrl.PosX = (int)c.PositionX;
                ctrl.PosY = (int)c.PositionY;
                ctrl.IsVisible = c.Visible;
                ctrl.IsLoading = _petManager.IsCharacterLoading(c.Id);
                ctrl.AnimationNames = new ObservableCollection<string>(animNames);
                ctrl.CurrentAnimation = c.CurrentAnimation ?? "";
            }
            else
            {
                Characters.Add(new CharControl
                {
                    Id = c.Id,
                    Name = c.Name,
                    TexturePath = c.TexturePath,
                    Scale = scale,
                    MaxScale = maxScale,
                    PosX = (int)c.PositionX,
                    PosY = (int)c.PositionY,
                    IsVisible = c.Visible,
                    IsLoading = _petManager.IsCharacterLoading(c.Id),
                    AnimationNames = new ObservableCollection<string>(animNames),
                    CurrentAnimation = c.CurrentAnimation ?? "",
                });
            }
        }

        // Remove characters no longer in the list (iterate backwards)
        for (int i = Characters.Count - 1; i >= 0; i--)
        {
            if (!currentIds.Contains(Characters[i].Id))
                Characters.RemoveAt(i);
        }

        SelectedCharacter = Characters.FirstOrDefault(c => c.Id == selectedId) ?? Characters.FirstOrDefault();
        CharacterCards.SelectedItem = SelectedCharacter;
        SyncSelectedCharacterState();
    }

    private void OnAddCharacter(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Spine .skel file",
            Filter = "Spine Skeleton|*.skel|All Files|*.*",
            Multiselect = false
        };
        if (dialog.ShowDialog() == true)
            ImportFromSkel(dialog.FileName);
    }

    private void OnScanRes(object sender, RoutedEventArgs e)
    {
        var resPath = FindResPath();
        if (!Directory.Exists(resPath)) return;
        int added = 0;
        foreach (var dir in Directory.GetDirectories(resPath))
        {
            var skelFiles = Directory.GetFiles(dir, "*.skel");
            if (skelFiles.Length == 0) continue;
            var atlasFiles = Directory.GetFiles(dir, "*.atlas");
            var pngFiles = Directory.GetFiles(dir, "*.png");
            if (atlasFiles.Length > 0 && pngFiles.Length > 0)
            {
                if (!_petManager.Characters.Any(c => c.SkelPath == skelFiles[0]))
                {
                    _petManager.AddCharacter(skelFiles[0], atlasFiles[0], pngFiles[0]);
                    added++;
                }
            }
        }
    }

    private static string FindResPath() => SpineWebViewService.FindResPath();

    private void ImportFromSkel(string skelPath)
    {
        var dir = Path.GetDirectoryName(skelPath) ?? "";
        var atlasPath = Directory.GetFiles(dir, "*.atlas").FirstOrDefault() ?? "";
        var pngPath = Directory.GetFiles(dir, "*.png").FirstOrDefault() ?? "";
        if (string.IsNullOrEmpty(atlasPath) || string.IsNullOrEmpty(pngPath))
        {
            MessageBox.Show("Need .skel + .atlas + .png together.", "Missing Files",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _petManager.AddCharacter(skelPath, atlasPath, pngPath);
    }

    private void OnCharacterSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SelectedCharacter = CharacterCards.SelectedItem as CharControl;
        SyncSelectedCharacterState();
    }

    private void OnMoveSelectionChanged(object sender, RoutedEventArgs e)
    {
        UpdateOverlayState();
    }

    private void OnToggleCharacter(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is CharControl ctrl)
        {
            if (ctrl.IsLoading)
                return;

            var character = _petManager.Characters.FirstOrDefault(c => c.Id == ctrl.Id);
            if (character == null) return;

            if (ctrl.IsVisible)
                _petManager.HideCharacter(character);
            else
                _petManager.ShowCharacter(character);

            UpdateOverlayState();

            if (character.Visible)
            {
                _petManager.RenderHost.SetCharacterConfigMode(character.Id, _isConfigMode);
                _petManager.RenderHost.SetCharacterSpeed(character.Id, SelectedSpeed / 200.0);
                if (_isConfigMode)
                {
                    ShowOverlay();
                    Activate();
                }
            }
        }
    }

    private void OnCharacterCardsPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var innerScrollViewer = FindVisualChild<ScrollViewer>(CharacterCards);
        if (innerScrollViewer == null)
            return;

        if (CanScroll(innerScrollViewer, e.Delta))
        {
            ScrollByDelta(innerScrollViewer, e.Delta);
            e.Handled = true;
            return;
        }

        ScrollByDelta(MainScrollViewer, e.Delta);
        e.Handled = true;
    }

    private void OnCharacterScaleChanged(string characterId, double maxScale, double currentScale)
    {
        // Update the CharControl in-place without a full list rebuild
        var ctrl = Characters.FirstOrDefault(c => c.Id == characterId);
        if (ctrl != null)
        {
            ctrl.MaxScale = Math.Clamp(maxScale, 0.20, DefaultMaxScale);
            ctrl.Scale = Math.Clamp(currentScale, 0.05, ctrl.MaxScale);
        }

        // If this is the selected character, sync the slider state
        if (SelectedCharacter?.Id == characterId)
        {
            _isRefreshingSelection = true;
            SelectedScaleMax = Math.Clamp(maxScale, 0.20, DefaultMaxScale);
            SelectedScale = Math.Clamp(currentScale, 0.05, SelectedScaleMax);
            SelectedScalePercent = ConvertScaleToPercent(SelectedScale, SelectedScaleMax);
            if (SelectedCharacter != null)
            {
                SelectedCharacter.MaxScale = SelectedScaleMax;
                SelectedCharacter.Scale = SelectedScale;
            }
            _isRefreshingSelection = false;
        }
    }

    private void OnScaleChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isRefreshingSelection || SelectedCharacter == null)
            return;

        var character = _petManager.Characters.FirstOrDefault(c => c.Id == SelectedCharacter.Id);
        if (character == null) return;

        var effectiveMaxScale = _petManager.RenderHost.IsCharacterVisible(character.Id)
            ? _petManager.RenderHost.GetMaxScale(character.Id)
            : SelectedScaleMax;

        // If the window's actual max scale differs from our tracked max,
        // sync everything so the slider maps 0-100% to the real available range.
        if (Math.Abs(SelectedScaleMax - effectiveMaxScale) > 0.0001)
        {
            _isRefreshingSelection = true;
            SelectedScaleMax = effectiveMaxScale;
            var currentActualScale = Math.Clamp(
                _petManager.RenderHost.GetCurrentScale(character.Id) > 0
                    ? _petManager.RenderHost.GetCurrentScale(character.Id)
                    : SelectedScale,
                0.05,
                effectiveMaxScale);
            SelectedScale = currentActualScale;
            SelectedScalePercent = ConvertScaleToPercent(currentActualScale, effectiveMaxScale);
            SelectedCharacter.Scale = currentActualScale;
            character.Scale = currentActualScale;
            _isRefreshingSelection = false;
            return;
        }

        var clampedPercent = Math.Clamp(SelectedScalePercent, 0, 100);
        var clampedScale = Math.Clamp(ConvertPercentToScale(clampedPercent, SelectedScaleMax), 0.05, effectiveMaxScale);

        character.Scale = clampedScale;
        SelectedCharacter.Scale = clampedScale;
        SelectedScale = clampedScale;
        _petManager.RenderHost.SetCharacterScale(character.Id, clampedScale);
    }

    private void OnSpeedChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isRefreshingSelection || SelectedCharacter == null)
            return;

        var character = _petManager.Characters.FirstOrDefault(c => c.Id == SelectedCharacter.Id);
        if (character == null) return;

        var speed = SelectedSpeed / 200.0;
        _petManager.RenderHost.SetCharacterSpeed(character.Id, speed);
    }

    private void OnResetScale(object sender, RoutedEventArgs e)
    {
        if (SelectedCharacter == null) return;
        SelectedScalePercent = ConvertScaleToPercent(0.2, SelectedScaleMax);
    }

    private void OnResetSpeed(object sender, RoutedEventArgs e)
    {
        if (SelectedCharacter == null) return;
        SelectedSpeed = 100;
    }

    private void OnResetSelectedPosition(object sender, RoutedEventArgs e)
    {
        if (SelectedCharacter == null)
            return;

        var character = _petManager.Characters.FirstOrDefault(c => c.Id == SelectedCharacter.Id);
        if (character == null)
            return;

        _petManager.RenderHost.ResetCharacterPosition(character.Id);
        SelectedCharacter.PosX = (int)character.PositionX;
        SelectedCharacter.PosY = (int)character.PositionY;
    }

    private static double ConvertScaleToPercent(double scale, double maxScale)
    {
        if (maxScale <= 0.05)
            return 100;

        var normalized = (scale - 0.05) / (maxScale - 0.05);
        return Math.Clamp(normalized * 100, 0, 100);
    }

    private static double ConvertPercentToScale(double percent, double maxScale)
    {
        var normalized = Math.Clamp(percent, 0, 100) / 100.0;
        return 0.05 + ((maxScale - 0.05) * normalized);
    }

    private static bool CanScroll(ScrollViewer scrollViewer, int wheelDelta)
    {
        if (scrollViewer.ScrollableHeight <= 0)
            return false;

        return wheelDelta < 0
            ? scrollViewer.VerticalOffset < scrollViewer.ScrollableHeight
            : scrollViewer.VerticalOffset > 0;
    }

    private static void ScrollByDelta(ScrollViewer scrollViewer, int wheelDelta)
    {
        var step = Math.Max(48, SystemParameters.WheelScrollLines * 16);
        var nextOffset = scrollViewer.VerticalOffset - (wheelDelta / 120.0 * step);
        var clampedOffset = Math.Clamp(nextOffset, 0, scrollViewer.ScrollableHeight);
        scrollViewer.ScrollToVerticalOffset(clampedOffset);
    }

    private static T? FindVisualChild<T>(DependencyObject? parent) where T : DependencyObject
    {
        if (parent == null) return null;

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
                return typedChild;

            var descendant = FindVisualChild<T>(child);
            if (descendant != null)
                return descendant;
        }

        return null;
    }

    private void OnAnimationChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshingSelection || sender is not ComboBox)
            return;

        if (!string.IsNullOrEmpty(SelectedAnimation) && SelectedCharacter != null)
        {
            var character = _petManager.Characters.FirstOrDefault(c => c.Id == SelectedCharacter.Id);
            if (character == null) return;
            character.CurrentAnimation = SelectedAnimation;
            SelectedCharacter.CurrentAnimation = SelectedAnimation;
            _petManager.RenderHost.PlayCharacterAnimation(character.Id, SelectedAnimation, true);
        }
    }

    private void OnRemoveSelectedCharacter(object sender, RoutedEventArgs e)
    {
        if (SelectedCharacter == null) return;
        var character = _petManager.Characters.FirstOrDefault(c => c.Id == SelectedCharacter.Id);
        if (character == null) return;

        _petManager.RemoveCharacter(character);
        if (SelectedCharacter?.Id == character.Id)
            ClearSelection();
    }

    private void OnOverlayCharacterMoved(string characterId, double left, double top)
    {
        var character = _petManager.Characters.FirstOrDefault(c => c.Id == characterId);
        if (character == null) return;

        character.PositionX = left;
        character.PositionY = top;

        var ctrl = Characters.FirstOrDefault(c => c.Id == characterId);
        if (ctrl != null)
        {
            ctrl.PosX = (int)left;
            ctrl.PosY = (int)top;
        }
    }

    private void OnExitConfiguration(object sender, RoutedEventArgs e)
    {
        _petManager.SaveAllState();
        _isConfigMode = false;
        ApplyConfigMode();
    }

    protected override void OnClosed(EventArgs e)
    {
        _overlayWindow?.Close();
        _overlayWindow = null;
        base.OnClosed(e);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}


