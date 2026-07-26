using System.IO;
using SpinePet.Models;

namespace SpinePet.Services;

public sealed class CharacterResourceDiscoveryService
{
    private readonly EnumerationOptions _enumerationOptions = new()
    {
        IgnoreInaccessible = true,
        MatchCasing = MatchCasing.CaseInsensitive,
        RecurseSubdirectories = false
    };

    public IReadOnlyList<CharacterResourceFiles> Discover(string resourceDirectory)
    {
        if (!Directory.Exists(resourceDirectory))
        {
            return [];
        }

        return Directory
            .EnumerateDirectories(resourceDirectory, "*", _enumerationOptions)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(directory => TryCreateFromDirectory(directory))
            .Where(resource => resource != null)
            .Cast<CharacterResourceFiles>()
            .ToArray();
    }

    public CharacterResourceFiles? DiscoverForSkeleton(string skeletonPath)
    {
        if (!File.Exists(skeletonPath) ||
            !string.Equals(
                Path.GetExtension(skeletonPath),
                ".skel",
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string? directory = Path.GetDirectoryName(skeletonPath);
        return string.IsNullOrWhiteSpace(directory)
            ? null
            : TryCreateFromDirectory(directory, skeletonPath);
    }

    private CharacterResourceFiles? TryCreateFromDirectory(
        string directory,
        string? preferredSkeletonPath = null)
    {
        string? skeletonPath = preferredSkeletonPath ??
            FindFirstFile(directory, "*.skel");
        string? atlasPath = FindFirstFile(directory, "*.atlas");
        string[] texturePaths = Directory
            .EnumerateFiles(directory, "*.png", _enumerationOptions)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (string.IsNullOrWhiteSpace(skeletonPath) ||
            string.IsNullOrWhiteSpace(atlasPath) ||
            texturePaths.Length == 0)
        {
            return null;
        }

        return new CharacterResourceFiles(
            skeletonPath,
            atlasPath,
            texturePaths[0],
            texturePaths.Skip(1).ToArray());
    }

    private string? FindFirstFile(string directory, string searchPattern)
    {
        return Directory
            .EnumerateFiles(directory, searchPattern, _enumerationOptions)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }
}
