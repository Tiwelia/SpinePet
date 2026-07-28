namespace SpinePet.Models;

public static class CharacterResourceTypes
{
    public const string Standing = "standing";
    public const string Aim = "aim";
    public const string Cover = "cover";
    public const string Icons = "icons";

    public static IReadOnlyList<string> Renderable { get; } =
        [Standing, Aim, Cover];

    public static bool IsRenderable(string? resourceType) =>
        Renderable.Contains(resourceType, StringComparer.OrdinalIgnoreCase);

    public static string Normalize(string? resourceType) =>
        Renderable.FirstOrDefault(type => string.Equals(
            type,
            resourceType,
            StringComparison.OrdinalIgnoreCase)) ?? Standing;

    public static string GetDisplayName(string? resourceType)
    {
        string normalized = Normalize(resourceType);
        return char.ToUpperInvariant(normalized[0]) + normalized[1..];
    }
}
