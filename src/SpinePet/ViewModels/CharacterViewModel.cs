using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SpinePet.ViewModels;

public sealed class CharacterViewModel : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _thumbnailPath = string.Empty;
    private double _scale = 0.2;
    private double _maxScale = 1.35;
    private int _positionX = 200;
    private int _positionY = 200;
    private bool _isVisible;
    private bool _isLoading;
    private string _configAnimation = string.Empty;
    private double _animationSpeed = 1.0;

    public required string Id { get; init; }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string ThumbnailPath
    {
        get => _thumbnailPath;
        set => SetProperty(ref _thumbnailPath, value);
    }

    public double Scale
    {
        get => _scale;
        set => SetProperty(ref _scale, value);
    }

    public double MaxScale
    {
        get => _maxScale;
        set => SetProperty(ref _maxScale, value);
    }

    public int PositionX
    {
        get => _positionX;
        set => SetProperty(ref _positionX, value);
    }

    public int PositionY
    {
        get => _positionY;
        set => SetProperty(ref _positionY, value);
    }

    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (SetProperty(ref _isVisible, value))
            {
                OnPropertyChanged(nameof(VisibilityActionLabel));
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            if (SetProperty(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(VisibilityActionLabel));
                OnPropertyChanged(nameof(CanToggleVisibility));
            }
        }
    }

    public bool CanToggleVisibility => !IsLoading;

    public string VisibilityActionLabel =>
        IsLoading ? "Loading..." : IsVisible ? "Hide" : "Show";

    public string ConfiguredAnimation
    {
        get => _configAnimation;
        set => SetProperty(ref _configAnimation, value);
    }

    public double AnimationSpeed
    {
        get => _animationSpeed;
        set => SetProperty(ref _animationSpeed, value);
    }

    public ObservableCollection<string> AnimationNames { get; } = new();

    public void UpdateAnimationNames(IEnumerable<string> animationNames)
    {
        AnimationNames.Clear();
        foreach (string animationName in animationNames)
        {
            AnimationNames.Add(animationName);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetProperty<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
