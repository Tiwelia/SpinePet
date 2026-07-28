namespace SpinePet.Models;

public sealed record CharacterBundleImportResult(
    CharacterResourceFiles Resources,
    string DestinationDirectory);
