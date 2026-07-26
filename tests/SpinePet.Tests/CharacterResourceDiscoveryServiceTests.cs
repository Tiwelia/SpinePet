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
        CreateCharacter("Alpha", "a", includeAdditionalTexture: true);
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
        bool includeAdditionalTexture = false)
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
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }
}
