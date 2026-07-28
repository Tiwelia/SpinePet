using SpinePet.Models;
using SpinePet.Rendering.Native;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SpinePet.Tests;

public sealed class NativeSpineResourceTests
{
    [Fact]
    public void PremultiplyBgraPixelsConvertsStraightAlphaBeforeFiltering()
    {
        byte[] pixels =
        [
            200, 100, 50, 128,
            25, 50, 75, 0,
            10, 20, 30, 255
        ];

        NativeTextureSource.PremultiplyBgraPixels(pixels);

        Assert.Equal(
            [
                100, 50, 25, 128,
                0, 0, 0, 0,
                10, 20, 30, 255
            ],
            pixels);
    }

    [Fact]
    public void LoadStraightAlphaTexturePremultipliesGpuUploadOnlyOnce()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"spinepet-alpha-{Guid.NewGuid():N}.png");
        try
        {
            BitmapSource bitmap = BitmapSource.Create(
                1,
                1,
                96,
                96,
                PixelFormats.Bgra32,
                null,
                new byte[] { 200, 100, 50, 128 },
                4);
            PngBitmapEncoder encoder = new();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (FileStream stream = File.Create(path))
                encoder.Save(stream);

            NativeTextureSource straight =
                NativeTextureSource.Load(path, premultipliedAlpha: false);
            NativeTextureSource premultiplied =
                NativeTextureSource.Load(path, premultipliedAlpha: true);

            Assert.Equal(
                new byte[] { 100, 50, 25, 128 },
                straight.CopyBgraPixels());
            Assert.Equal(
                new byte[] { 200, 100, 50, 128 },
                premultiplied.CopyBgraPixels());
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void LoadAllInstalledSpine41ResourcesProducesRenderableGeometry()
    {
        string repositoryRoot = FindRepositoryRoot();
        string resourceRoot = Path.Combine(repositoryRoot, "res");
        if (!Directory.Exists(resourceRoot))
            return;

        string[] atlases = Directory.GetFiles(
            resourceRoot,
            "*.atlas",
            SearchOption.AllDirectories);
        Assert.NotEmpty(atlases);

        foreach (string atlasPath in atlases)
        {
            string skeletonPath = Path.ChangeExtension(atlasPath, ".skel");
            if (!File.Exists(skeletonPath))
                continue;

            CharacterConfig config = new()
            {
                AtlasPath = atlasPath,
                SkeletonPath = skeletonPath
            };

            using NativeSpineResource resource = NativeSpineResource.Load(config);
            resource.SetAnimation(
                resource.AnimationNames.Count > 0
                    ? resource.AnimationNames[0]
                    : null,
                true);
            resource.Update(1f / 60f);

            NativeSpineGeometry geometry = new();
            Assert.NotEmpty(geometry.Build(resource.Skeleton));
            Assert.StartsWith("4.1", resource.SkeletonData.Version);
            Assert.NotEmpty(resource.TextureLoader.Textures);
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null &&
               !File.Exists(Path.Combine(directory.FullName, "SpinePet.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ??
               throw new DirectoryNotFoundException("Repository root not found.");
    }
}
