using System.IO;
using SpinePet.Models;
using SpinePet.Services;

namespace SpinePet.Tests;

public sealed class CharacterResourceDiscoveryServiceTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SpinePet.Tests",
        Guid.NewGuid().ToString("N"));

    public CharacterResourceDiscoveryServiceTests()
    {
        Directory.CreateDirectory(_temporaryDirectory);
    }

    [Fact]
    public void DiscoverReturnsCompleteCharactersInDeterministicOrder()
    {
        CreateCharacter("Zulu", "z");
        CreateCharacter(
            "Alpha",
            "a",
            includeAdditionalTexture: true,
            includeIcon: true);
        Directory.CreateDirectory(Path.Combine(_temporaryDirectory, "Incomplete"));

        CharacterResourceDiscoveryService service = new();
        IReadOnlyList<CharacterResourceFiles> resources =
            service.Discover(_temporaryDirectory);

        Assert.Equal(2, resources.Count);
        Assert.EndsWith(
            Path.Combine("Alpha", "a.skel"),
            resources[0].SkeletonPath,
            StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(
            Path.Combine("Zulu", "z.skel"),
            resources[1].SkeletonPath,
            StringComparison.OrdinalIgnoreCase);
        Assert.Single(resources[0].AdditionalTexturePaths);
        Assert.DoesNotContain(
            resources[0].AdditionalTexturePaths,
            path => path.EndsWith(
                "_icon.png",
                StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Alpha", resources[0].Identity.DisplayName);
        Assert.Equal(
            CharacterResourceTypes.Standing,
            resources[0].ResourceType);
    }

    [Fact]
    public void DiscoverGroupsSkinsAndFindsAllRenderableStates()
    {
        string characterDirectory =
            Path.Combine(_temporaryDirectory, "Anis Star");
        CreateResourceSet(
            Path.Combine(characterDirectory, CharacterResourceTypes.Standing),
            "c017_00");
        CreateResourceSet(
            Path.Combine(characterDirectory, CharacterResourceTypes.Standing),
            "c017_01");
        CreateResourceSet(
            Path.Combine(characterDirectory, CharacterResourceTypes.Aim),
            "c017_00");
        Directory.CreateDirectory(
            Path.Combine(characterDirectory, CharacterResourceTypes.Cover));
        string iconDirectory =
            Path.Combine(characterDirectory, CharacterResourceTypes.Icons);
        Directory.CreateDirectory(iconDirectory);
        File.WriteAllBytes(
            Path.Combine(iconDirectory, "c017_00_icon.png"),
            []);

        CharacterResourceDiscoveryService service = new();
        IReadOnlyList<CharacterResourceFiles> standing =
            service.Discover(_temporaryDirectory);
        IReadOnlyList<CharacterResourceFiles> all =
            service.DiscoverAll(_temporaryDirectory);

        Assert.Equal(2, standing.Count);
        Assert.Equal(3, all.Count);
        CharacterResourceFiles aim = Assert.Single(
            all,
            resource => string.Equals(
                resource.ResourceType,
                CharacterResourceTypes.Aim,
                StringComparison.OrdinalIgnoreCase));
        Assert.Equal("c017_00", aim.Identity.ResourceName);
    }

    [Fact]
    public void DiscoverForSkeletonRejectsNonSkeletonFiles()
    {
        string textPath = Path.Combine(_temporaryDirectory, "character.txt");
        File.WriteAllText(textPath, string.Empty);

        CharacterResourceDiscoveryService service = new();

        Assert.Null(service.DiscoverForSkeleton(textPath));
    }

    private void CreateCharacter(
        string directoryName,
        string fileName,
        bool includeAdditionalTexture = false,
        bool includeIcon = false)
    {
        string directory = Path.Combine(_temporaryDirectory, directoryName);
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, $"{fileName}.skel"), []);
        File.WriteAllText(Path.Combine(directory, $"{fileName}.atlas"), string.Empty);
        File.WriteAllBytes(Path.Combine(directory, $"{fileName}_a.png"), []);

        if (includeAdditionalTexture)
        {
            File.WriteAllBytes(Path.Combine(directory, $"{fileName}_b.png"), []);
        }

        if (includeIcon)
        {
            File.WriteAllBytes(
                Path.Combine(directory, $"{fileName}_icon.png"),
                []);
        }
    }

    private static void CreateResourceSet(
        string directory,
        string resourceName)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(
            Path.Combine(directory, $"{resourceName}.skel"),
            []);
        File.WriteAllText(
            Path.Combine(directory, $"{resourceName}.atlas"),
            string.Empty);
        File.WriteAllBytes(
            Path.Combine(directory, $"{resourceName}.png"),
            []);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }
}
