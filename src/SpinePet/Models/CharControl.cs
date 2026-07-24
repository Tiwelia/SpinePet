using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SpinePet.Models;

public class CharControl : INotifyPropertyChanged
{
    public string Id { get; set; } = "";
    public string TexturePath { get; set; } = "";

    private string _name = "";
    public string Name { get => _name; set { _name = value; OnChanged(); } }

    private double _scale = 0.2;
    public double Scale { get => _scale; set { _scale = value; OnChanged(); } }

    private double _maxScale = 1.35;
    public double MaxScale { get => _maxScale; set { _maxScale = value; OnChanged(); } }

    private int _posX = 200;
    public int PosX { get => _posX; set { _posX = value; OnChanged(); } }

    private int _posY = 200;
    public int PosY { get => _posY; set { _posY = value; OnChanged(); } }

    private bool _isVisible;
    public bool IsVisible { get => _isVisible; set { _isVisible = value; OnChanged(); OnChanged(nameof(ShowHideLabel)); } }

    private bool _isLoading;
    public bool IsLoading { get => _isLoading; set { _isLoading = value; OnChanged(); OnChanged(nameof(ShowHideLabel)); OnChanged(nameof(CanToggleVisibility)); } }

    public bool CanToggleVisibility => !_isLoading;

    public string ShowHideLabel => _isLoading ? "Loading..." : _isVisible ? "Hide" : "Show";

    private string _currentAnimation = "";
    public string CurrentAnimation
    {
        get => _currentAnimation;
        set { _currentAnimation = value; OnChanged(); }
    }

    public ObservableCollection<string> AnimationNames { get; set; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
