using System.IO;
using SpinePet.Models;
using SpinePet.Services;

namespace SpinePet.Tests;

public sealed class UnityBundleImportServiceTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SpinePet.Tests",
        Guid.NewGuid().ToString("N"));

    public UnityBundleImportServiceTests()
    {
        Directory.CreateDirectory(_temporaryDirectory);
    }

    [Fact]
    public void TryParseFileNameReadsIdentitySkinAndRenderableType()
    {
        bool parsed = UnityBundleImportService.TryParseFileName(
            Path.Combine(
                _temporaryDirectory,
                "c233_01_standing_假面紫罗兰_Dorothy"),
            out CharacterBundleDescriptor? descriptor);

        Assert.True(parsed);
        Assert.NotNull(descriptor);
        Assert.Equal("c233_01", descriptor.ResourceName);
        Assert.Equal("233", descriptor.CharacterCode);
        Assert.Equal("01", descriptor.SkinCode);
        Assert.Equal(CharacterResourceTypes.Standing, descriptor.ResourceType);
    }

    [Fact]
    public void TryParseFileNameRejectsIconBundles()
    {
        Assert.False(UnityBundleImportService.TryParseFileName(
            "c015_00_icons_Na0h_Summer-Anis",
            out _));
    }

    [Fact]
    public void HasUnityFsHeaderChecksMagicBytesInsteadOfExtension()
    {
        string unityPath = Path.Combine(
            _temporaryDirectory,
            "c233_01_cover_author_name");
        string otherPath = Path.Combine(
            _temporaryDirectory,
            "c233_01_aim_author_name.bundle");
        File.WriteAllBytes(
            unityPath,
            [0x55, 0x6E, 0x69, 0x74, 0x79, 0x46, 0x53, 0x00]);
        File.WriteAllBytes(otherPath, [0x50, 0x4B, 0x03, 0x04]);

        Assert.True(UnityBundleImportService.HasUnityFsHeader(unityPath));
        Assert.False(UnityBundleImportService.HasUnityFsHeader(otherPath));
    }

    [Fact]
    public async Task ImportAsyncCreatesStateLayoutAndRefusesToOverwrite()
    {
        string extractorPath =
            Path.Combine(_temporaryDirectory, "fake_extractor.py");
        await File.WriteAllTextAsync(
            extractorPath,
            """
            import argparse
            from pathlib import Path

            parser = argparse.ArgumentParser()
            parser.add_argument("--bundle")
            parser.add_argument("--resource-id", required=True)
            parser.add_argument("--output-directory", required=True, type=Path)
            args = parser.parse_args()
            args.output_directory.mkdir(parents=True, exist_ok=True)
            (args.output_directory / f"{args.resource_id}.skel").write_bytes(b"skel")
            (args.output_directory / f"{args.resource_id}.atlas").write_text(
                f"{args.resource_id}.png\nsize:1,1\n",
                encoding="utf-8",
            )
            (args.output_directory / f"{args.resource_id}.png").write_bytes(b"png")
            """);
        string bundlePath = Path.Combine(
            _temporaryDirectory,
            "c233_01_standing_author_Dorothy");
        await File.WriteAllBytesAsync(
            bundlePath,
            [0x55, 0x6E, 0x69, 0x74, 0x79, 0x46, 0x53, 0x00]);
        string resourceDirectory =
            Path.Combine(_temporaryDirectory, "res");
        CharacterIdentityService identityService = new();
        CharacterResourceDiscoveryService discoveryService =
            new(identityService);
        UnityBundleImportService service = new(
            identityService,
            discoveryService,
            extractorPath);

        CharacterBundleImportResult result =
            await service.ImportAsync(bundlePath, resourceDirectory);

        string characterDirectory =
            Path.Combine(resourceDirectory, "Dorothy");
        Assert.Equal(
            Path.Combine(characterDirectory, CharacterResourceTypes.Standing),
            result.DestinationDirectory);
        foreach (string resourceType in new[]
                 {
                     CharacterResourceTypes.Standing,
                     CharacterResourceTypes.Aim,
                     CharacterResourceTypes.Cover,
                     CharacterResourceTypes.Icons
                 })
        {
            Assert.True(Directory.Exists(
                Path.Combine(characterDirectory, resourceType)));
        }

        Assert.True(File.Exists(Path.Combine(
            result.DestinationDirectory,
            "c233_01.skel")));
        await Assert.ThrowsAsync<IOException>(
            () => service.ImportAsync(bundlePath, resourceDirectory));
        Assert.Empty(
            Directory.EnumerateDirectories(
                resourceDirectory,
                ".SpinePet-Import-*"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }
}
