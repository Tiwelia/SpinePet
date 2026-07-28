using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using SpinePet.Models;

namespace SpinePet.ViewModels;

public sealed class CharacterViewModel : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _skinLabel = string.Empty;
    private string _resourceType = CharacterResourceTypes.Standing;
    private string _thumbnailPath = string.Empty;
    private bool _hasStandingResources;
    private bool _hasAimResources;
    private bool _hasCoverResources;
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

    public string SkinLabel
    {
        get => _skinLabel;
        set => SetProperty(ref _skinLabel, value);
    }

    public string ResourceType
    {
        get => _resourceType;
        private set
        {
            if (!SetProperty(ref _resourceType, value))
            {
                return;
            }

            OnPropertyChanged(nameof(ResourceTypeLabel));
            OnPropertyChanged(nameof(IsStandingResources));
            OnPropertyChanged(nameof(IsAimResources));
            OnPropertyChanged(nameof(IsCoverResources));
        }
    }

    public string ResourceTypeLabel =>
        CharacterResourceTypes.GetDisplayName(ResourceType);

    public bool HasStandingResources
    {
        get => _hasStandingResources;
        private set => SetProperty(ref _hasStandingResources, value);
    }

    public bool HasAimResources
    {
        get => _hasAimResources;
        private set => SetProperty(ref _hasAimResources, value);
    }

    public bool HasCoverResources
    {
        get => _hasCoverResources;
        private set => SetProperty(ref _hasCoverResources, value);
    }

    public bool IsStandingResources =>
        string.Equals(
            ResourceType,
            CharacterResourceTypes.Standing,
            StringComparison.OrdinalIgnoreCase);

    public bool IsAimResources =>
        string.Equals(
            ResourceType,
            CharacterResourceTypes.Aim,
            StringComparison.OrdinalIgnoreCase);

    public bool IsCoverResources =>
        string.Equals(
            ResourceType,
            CharacterResourceTypes.Cover,
            StringComparison.OrdinalIgnoreCase);

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

    public void UpdateResourceTypes(
        string currentResourceType,
        IEnumerable<string> availableResourceTypes)
    {
        HashSet<string> available = new(
            availableResourceTypes,
            StringComparer.OrdinalIgnoreCase);
        ResourceType =
            CharacterResourceTypes.Normalize(currentResourceType);
        HasStandingResources =
            available.Contains(CharacterResourceTypes.Standing);
        HasAimResources =
            available.Contains(CharacterResourceTypes.Aim);
        HasCoverResources =
            available.Contains(CharacterResourceTypes.Cover);
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
