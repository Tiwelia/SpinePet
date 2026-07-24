using System.Windows;
using System.Windows.Input;
using SpinePet.Services;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Cursors = System.Windows.Input.Cursors;
using Point = System.Windows.Point;

namespace SpinePet.Views;

public partial class OverlayWindow : Window
{
    private readonly PetManager _petManager;
    private bool _isDragArmed;
    private bool _isDragging;
    private Point _dragStartScreen;
    private Point _windowStartPosition;

    public string? SelectedCharacterId { get; set; }
    public bool MoveSelectedCharacter { get; set; }

    public event Action<string, double, double>? CharacterMoved;

    public OverlayWindow(PetManager petManager)
    {
        InitializeComponent();
        _petManager = petManager;

        Deactivated += (_, _) => StopDragging();
    }

    public void SetPreviewBounds(Rect bounds)
    {
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;
    }

    public void ShowOverlay()
    {
        if (Width <= 0 || Height <= 0)
            return;

        if (!IsVisible)
            Show();
    }

    public void HideOverlay()
    {
        if (IsVisible)
            Hide();
        StopDragging();
    }

    public void CancelDrag()
    {
        StopDragging();
    }

    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!MoveSelectedCharacter || string.IsNullOrEmpty(SelectedCharacterId))
            return;

        var character = _petManager.Characters.FirstOrDefault(c => c.Id == SelectedCharacterId);
        if (character == null || !_petManager.RenderHost.IsCharacterVisible(SelectedCharacterId))
            return;

        _isDragArmed = true;
        _isDragging = false;
        _dragStartScreen = PointToScreen(e.GetPosition(this));
        _windowStartPosition = new Point(character.PositionX, character.PositionY);
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if ((!_isDragArmed && !_isDragging) || string.IsNullOrEmpty(SelectedCharacterId))
            return;

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            StopDragging();
            return;
        }

        var character = _petManager.Characters.FirstOrDefault(c => c.Id == SelectedCharacterId);
        if (character == null)
        {
            StopDragging();
            return;
        }

        var currentScreen = PointToScreen(e.GetPosition(this));
        var delta = currentScreen - _dragStartScreen;

        if (!_isDragging)
        {
            var horizontalMoved = Math.Abs(delta.X) >= SystemParameters.MinimumHorizontalDragDistance;
            var verticalMoved = Math.Abs(delta.Y) >= SystemParameters.MinimumVerticalDragDistance;

            if (!horizontalMoved && !verticalMoved)
                return;

            _isDragging = true;
            Cursor = Cursors.SizeAll;
        }

        var nextLeft = _windowStartPosition.X + delta.X;
        var nextTop = _windowStartPosition.Y + delta.Y;
        _petManager.RenderHost.MoveCharacter(character.Id, nextLeft, nextTop);
        CharacterMoved?.Invoke(character.Id, nextLeft, nextTop);
        e.Handled = true;
    }

    private void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        StopDragging();
    }

    private void OnPreviewMouseLeave(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
            StopDragging();
    }

    private void StopDragging()
    {
        _isDragArmed = false;
        _isDragging = false;
        Cursor = Cursors.Arrow;
    }
}
