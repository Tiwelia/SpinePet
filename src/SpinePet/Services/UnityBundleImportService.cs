using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using SpinePet.Infrastructure;
using SpinePet.Models;

namespace SpinePet.Services;

public sealed partial class UnityBundleImportService
{
    private static readonly byte[] UnityFsHeader = "UnityFS"u8.ToArray();

    private readonly CharacterIdentityService _identityService;
    private readonly CharacterResourceDiscoveryService _resourceDiscovery;
    private readonly string _extractorScriptPath;
    private readonly string _pythonCommand;

    public UnityBundleImportService(
        CharacterIdentityService? identityService = null,
        CharacterResourceDiscoveryService? resourceDiscovery = null,
        string? extractorScriptPath = null,
        string pythonCommand = "python")
    {
        _identityService = identityService ?? new CharacterIdentityService();
        _resourceDiscovery = resourceDiscovery ??
            new CharacterResourceDiscoveryService(_identityService);
        _extractorScriptPath =
            extractorScriptPath ?? AppPaths.BundleExtractorScript;
        _pythonCommand = pythonCommand;
    }

    public static bool HasUnityFsHeader(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return false;
        }

        using FileStream stream = File.OpenRead(filePath);
        Span<byte> header = stackalloc byte[UnityFsHeader.Length];
        return stream.Read(header) == header.Length &&
            header.SequenceEqual(UnityFsHeader);
    }

    public static bool TryParseFileName(
        string filePath,
        out CharacterBundleDescriptor? descriptor)
    {
        string fileName = Path.GetFileNameWithoutExtension(filePath);
        Match match = BundleFileNamePattern().Match(fileName);
        if (!match.Success)
        {
            descriptor = null;
            return false;
        }

        string resourceType = match.Groups["type"].Value.ToLowerInvariant();
        if (!CharacterResourceTypes.IsRenderable(resourceType))
        {
            descriptor = null;
            return false;
        }

        string characterCode = match.Groups["character"].Value;
        string skinCode = match.Groups["skin"].Value;
        descriptor = new CharacterBundleDescriptor(
            $"c{characterCode}_{skinCode}",
            characterCode,
            skinCode,
            resourceType);
        return true;
    }

    public async Task<CharacterBundleImportResult> ImportAsync(
        string bundlePath,
        string resourceDirectory,
        CancellationToken cancellationToken = default)
    {
        if (!HasUnityFsHeader(bundlePath))
        {
            throw new InvalidDataException(
                "The selected file does not have a UnityFS header.");
        }

        if (!TryParseFileName(bundlePath, out CharacterBundleDescriptor? descriptor) ||
            descriptor == null)
        {
            throw new InvalidDataException(
                "The file name must start with " +
                "c<character>_<skin>_<standing|aim|cover>.");
        }

        if (!File.Exists(_extractorScriptPath))
        {
            throw new FileNotFoundException(
                "The UnityPy extractor script was not found.",
                _extractorScriptPath);
        }

        Directory.CreateDirectory(resourceDirectory);
        CharacterIdentity identity = _identityService.Resolve(
            $"{descriptor.ResourceName}.skel");
        string characterDirectory = FindExistingCharacterDirectory(
            resourceDirectory,
            descriptor.ResourceName) ?? Path.Combine(
                resourceDirectory,
                SanitizeDirectoryName(identity.DisplayName));
        string destinationDirectory = Path.Combine(
            characterDirectory,
            descriptor.ResourceType);
        string workingDirectory = Path.Combine(
            resourceDirectory,
            $".SpinePet-Import-{Guid.NewGuid():N}");

        Directory.CreateDirectory(workingDirectory);
        try
        {
            await RunExtractorAsync(
                bundlePath,
                descriptor.ResourceName,
                workingDirectory,
                cancellationToken);

            string extractedSkeletonPath = Path.Combine(
                workingDirectory,
                $"{descriptor.ResourceName}.skel");
            CharacterResourceFiles? stagedResources =
                _resourceDiscovery.DiscoverForSkeleton(
                    extractedSkeletonPath,
                    descriptor.ResourceType);
            if (stagedResources == null)
            {
                throw new InvalidDataException(
                    "The Unity bundle does not contain a complete matching " +
                    "Spine skeleton, atlas, and texture set.");
            }

            EnsureCharacterDirectories(characterDirectory);
            MoveExtractedFiles(workingDirectory, destinationDirectory);

            string destinationSkeletonPath = Path.Combine(
                destinationDirectory,
                $"{descriptor.ResourceName}.skel");
            CharacterResourceFiles? importedResources =
                _resourceDiscovery.DiscoverForSkeleton(
                    destinationSkeletonPath,
                    descriptor.ResourceType);
            if (importedResources == null)
            {
                throw new InvalidDataException(
                    "The extracted character files could not be discovered.");
            }

            return new CharacterBundleImportResult(
                importedResources,
                destinationDirectory);
        }
        finally
        {
            DeleteWorkingDirectory(workingDirectory, resourceDirectory);
        }
    }

    private async Task RunExtractorAsync(
        string bundlePath,
        string resourceName,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = _pythonCommand,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(_extractorScriptPath);
        startInfo.ArgumentList.Add("--bundle");
        startInfo.ArgumentList.Add(bundlePath);
        startInfo.ArgumentList.Add("--resource-id");
        startInfo.ArgumentList.Add(resourceName);
        startInfo.ArgumentList.Add("--output-directory");
        startInfo.ArgumentList.Add(outputDirectory);

        using Process process = Process.Start(startInfo) ??
            throw new InvalidOperationException(
                "The UnityPy extractor could not be started.");
        Task<string> standardOutputTask =
            process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> standardErrorTask =
            process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        string standardOutput = await standardOutputTask;
        string standardError = await standardErrorTask;

        if (process.ExitCode != 0)
        {
            string details = string.IsNullOrWhiteSpace(standardError)
                ? standardOutput
                : standardError;
            throw new InvalidDataException(
                $"UnityPy extraction failed: {details.Trim()}");
        }

        AppLogger.Write(
            nameof(UnityBundleImportService),
            $"extract-succeeded resource={resourceName} output={standardOutput.Trim()}");
    }

    private static void MoveExtractedFiles(
        string workingDirectory,
        string destinationDirectory)
    {
        string[] sourceFiles = Directory
            .EnumerateFiles(workingDirectory)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (sourceFiles.Length == 0)
        {
            throw new InvalidDataException(
                "The UnityPy extractor produced no files.");
        }

        Directory.CreateDirectory(destinationDirectory);
        string[] destinationPaths = sourceFiles
            .Select(sourcePath => Path.Combine(
                destinationDirectory,
                Path.GetFileName(sourcePath)))
            .ToArray();
        string? conflict = destinationPaths.FirstOrDefault(File.Exists);
        if (conflict != null)
        {
            throw new IOException(
                $"A character resource already exists: {conflict}");
        }

        List<string> movedFiles = [];
        try
        {
            for (int index = 0; index < sourceFiles.Length; index++)
            {
                File.Move(sourceFiles[index], destinationPaths[index]);
                movedFiles.Add(destinationPaths[index]);
            }
        }
        catch
        {
            foreach (string movedFile in movedFiles)
            {
                if (File.Exists(movedFile))
                {
                    File.Delete(movedFile);
                }
            }

            throw;
        }
    }

    private static void EnsureCharacterDirectories(string characterDirectory)
    {
        foreach (string resourceType in CharacterResourceTypes.Renderable)
        {
            Directory.CreateDirectory(
                Path.Combine(characterDirectory, resourceType));
        }

        Directory.CreateDirectory(
            Path.Combine(characterDirectory, CharacterResourceTypes.Icons));
    }

    private string? FindExistingCharacterDirectory(
        string resourceDirectory,
        string resourceName)
    {
        CharacterResourceFiles? existing =
            _resourceDiscovery.DiscoverAll(resourceDirectory)
                .FirstOrDefault(resource => string.Equals(
                    resource.Identity.ResourceName,
                    resourceName,
                    StringComparison.OrdinalIgnoreCase));
        string? stateDirectory = existing == null
            ? null
            : Path.GetDirectoryName(existing.SkeletonPath);
        if (string.IsNullOrWhiteSpace(stateDirectory))
        {
            return null;
        }

        string stateDirectoryName = Path.GetFileName(
            Path.TrimEndingDirectorySeparator(stateDirectory));
        return CharacterResourceTypes.IsRenderable(stateDirectoryName)
            ? Path.GetDirectoryName(stateDirectory)
            : stateDirectory;
    }

    private static void DeleteWorkingDirectory(
        string workingDirectory,
        string resourceDirectory)
    {
        if (!Directory.Exists(workingDirectory))
        {
            return;
        }

        string resolvedWorkingDirectory =
            Path.GetFullPath(workingDirectory);
        string resolvedResourceDirectory =
            Path.GetFullPath(resourceDirectory)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        if (!resolvedWorkingDirectory.StartsWith(
            resolvedResourceDirectory,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Refusing to delete an import directory outside res.");
        }

        Directory.Delete(resolvedWorkingDirectory, recursive: true);
    }

    private static string SanitizeDirectoryName(string directoryName)
    {
        char[] invalidCharacters = Path.GetInvalidFileNameChars();
        string sanitized = string.Concat(
            directoryName.Select(character =>
                invalidCharacters.Contains(character) ? '_' : character));
        return string.IsNullOrWhiteSpace(sanitized)
            ? "Unknown Character"
            : sanitized.Trim();
    }

    [GeneratedRegex(
        @"^c(?<character>\d+)_(?<skin>[^_]+)_" +
        @"(?<type>standing|aim|cover)(?:_.*)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BundleFileNamePattern();
}
