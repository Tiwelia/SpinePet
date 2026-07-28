using System.IO;
using SpinePet.Models;

namespace SpinePet.Services;

public sealed class CharacterResourceDiscoveryService
{
    private readonly CharacterIdentityService _identityService;

    private readonly EnumerationOptions _directoryEnumerationOptions = new()
    {
        IgnoreInaccessible = true,
        MatchCasing = MatchCasing.CaseInsensitive,
        RecurseSubdirectories = false
    };

    public CharacterResourceDiscoveryService(
        CharacterIdentityService? identityService = null)
    {
        _identityService = identityService ?? new CharacterIdentityService();
    }

    public IReadOnlyList<CharacterResourceFiles> Discover(
        string resourceDirectory)
    {
        return DiscoverAll(resourceDirectory)
            .Where(resource => string.Equals(
                resource.ResourceType,
                CharacterResourceTypes.Standing,
                StringComparison.OrdinalIgnoreCase))
            .GroupBy(
                resource => resource.Identity.ResourceName,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(
                resource => resource.Identity.DisplayName,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                resource => resource.Identity.SkinCode,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<CharacterResourceFiles> DiscoverAll(
        string resourceDirectory)
    {
        if (!Directory.Exists(resourceDirectory))
        {
            return [];
        }

        List<CharacterResourceFiles> resources = [];
        foreach (string characterDirectory in Directory
                     .EnumerateDirectories(
                         resourceDirectory,
                         "*",
                         _directoryEnumerationOptions)
                     .OrderBy(
                         path => path,
                         StringComparer.OrdinalIgnoreCase))
        {
            AddResourcesFromDirectory(
                resources,
                characterDirectory,
                CharacterResourceTypes.Standing);

            foreach (string resourceType in CharacterResourceTypes.Renderable)
            {
                string typeDirectory =
                    Path.Combine(characterDirectory, resourceType);
                AddResourcesFromDirectory(
                    resources,
                    typeDirectory,
                    resourceType);
            }
        }

        return resources
            .OrderBy(
                resource => resource.Identity.DisplayName,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                resource => resource.Identity.SkinCode,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                resource => resource.ResourceType,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public CharacterResourceFiles? DiscoverForSkeleton(
        string skeletonPath,
        string? resourceType = null)
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
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        string resolvedType = resourceType ??
            ResolveResourceTypeFromDirectory(directory);
        return TryCreateForSkeleton(
            skeletonPath,
            CharacterResourceTypes.Normalize(resolvedType));
    }

    private void AddResourcesFromDirectory(
        List<CharacterResourceFiles> resources,
        string directory,
        string resourceType)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (string skeletonPath in Directory
                     .EnumerateFiles(
                         directory,
                         "*.skel",
                         _directoryEnumerationOptions)
                     .OrderBy(
                         path => path,
                         StringComparer.OrdinalIgnoreCase))
        {
            CharacterResourceFiles? resource =
                TryCreateForSkeleton(skeletonPath, resourceType);
            if (resource != null)
            {
                resources.Add(resource);
            }
        }
    }

    private CharacterResourceFiles? TryCreateForSkeleton(
        string skeletonPath,
        string resourceType)
    {
        string? directory = Path.GetDirectoryName(skeletonPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        string skeletonStem = Path.GetFileNameWithoutExtension(skeletonPath);
        CharacterIdentity identity =
            _identityService.Resolve(skeletonPath, string.Empty);
        string atlasPath = Path.Combine(directory, $"{skeletonStem}.atlas");
        if (!File.Exists(atlasPath))
        {
            return null;
        }

        string[] texturePaths = Directory
            .EnumerateFiles(
                directory,
                $"{identity.ResourceName}*.png",
                _directoryEnumerationOptions)
            .Where(path => !Path
                .GetFileNameWithoutExtension(path)
                .EndsWith("_icon", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (texturePaths.Length == 0)
        {
            return null;
        }

        string fallbackName = GetCharacterDirectoryName(directory);
        identity = _identityService.Resolve(skeletonPath, fallbackName);

        return new CharacterResourceFiles(
            skeletonPath,
            atlasPath,
            texturePaths[0],
            texturePaths.Skip(1).ToArray(),
            CharacterResourceTypes.Normalize(resourceType),
            identity);
    }

    private static string ResolveResourceTypeFromDirectory(string directory)
    {
        string directoryName =
            Path.GetFileName(Path.TrimEndingDirectorySeparator(directory));
        return CharacterResourceTypes.IsRenderable(directoryName)
            ? directoryName
            : CharacterResourceTypes.Standing;
    }

    private static string GetCharacterDirectoryName(string directory)
    {
        DirectoryInfo directoryInfo = new(directory);
        if (CharacterResourceTypes.IsRenderable(directoryInfo.Name) &&
            directoryInfo.Parent != null)
        {
            return directoryInfo.Parent.Name;
        }

        return directoryInfo.Name;
    }
}
