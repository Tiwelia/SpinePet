namespace SpinePet.Models;

public sealed record CharacterResourceFiles(
    string SkeletonPath,
    string AtlasPath,
    string PrimaryTexturePath,
    IReadOnlyList<string> AdditionalTexturePaths);
