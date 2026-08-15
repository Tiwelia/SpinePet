using System.Windows.Media;
using System.Windows.Media.Imaging;
using SpinePet.Models;
using SpinePet.Rendering.Native;
using SpinePet.Services;

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

        IReadOnlyList<CharacterResourceFiles> installedResources =
            new CharacterResourceDiscoveryService().DiscoverAll(resourceRoot);
        Assert.NotEmpty(installedResources);

        foreach (CharacterResourceFiles installed in installedResources)
        {
            CharacterConfig config = new()
            {
                AtlasPath = installed.AtlasPath,
                SkeletonPath = installed.SkeletonPath
            };

            using NativeSpineResource resource = NativeSpineResource.Load(config);
            resource.SetAnimation(
                resource.AnimationNames.Count > 0
                    ? resource.AnimationNames[0]
                    : null,
                true);
            resource.Update(1f / 60f);

            NativeSpineGeometry geometry = new();
            IReadOnlyList<NativeSpineDrawBatch> firstBuild =
                geometry.Build(resource.Skeleton);
            Assert.NotEmpty(firstBuild);
            NativeSpineVertex[][] vertexBuffers = firstBuild
                .Select(batch => batch.Vertices)
                .ToArray();
            int[][] indexBuffers = firstBuild
                .Select(batch => batch.Indices)
                .ToArray();

            IReadOnlyList<NativeSpineDrawBatch> secondBuild =
                geometry.Build(resource.Skeleton);

            Assert.Same(firstBuild, secondBuild);
            Assert.Equal(vertexBuffers.Length, secondBuild.Count);
            for (int batchIndex = 0;
                 batchIndex < secondBuild.Count;
                 batchIndex++)
            {
                NativeSpineDrawBatch batch = secondBuild[batchIndex];
                Assert.Same(vertexBuffers[batchIndex], batch.Vertices);
                Assert.Same(indexBuffers[batchIndex], batch.Indices);
                Assert.InRange(
                    batch.VertexCount,
                    1,
                    batch.Vertices.Length);
                Assert.InRange(
                    batch.IndexCount,
                    1,
                    batch.Indices.Length);
            }

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
