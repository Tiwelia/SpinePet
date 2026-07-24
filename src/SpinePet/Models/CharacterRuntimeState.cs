namespace SpinePet.Models;

internal sealed class CharacterRuntimeState
{
    public required CharacterConfig Config { get; set; }
    public List<string> AnimationNames { get; } = new();
    public double CurrentScale { get; set; }
    public double MaxScale { get; set; }
    public double Speed { get; set; }
    public bool IsVisible { get; set; }
    public bool IsLoading { get; set; }
}
