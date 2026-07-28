using System.IO;

namespace SpinePet.Infrastructure;

internal static class AppPaths
{
    private const string ApplicationDirectoryName = "SpinePet";

    public static string LocalDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        ApplicationDirectoryName);

    public static string ConfigFile { get; } = Path.Combine(
        LocalDataDirectory,
        "config.json");

    public static string LogDirectory { get; } = Path.Combine(
        LocalDataDirectory,
        "Logs");

    public static string ProjectRoot { get; } = FindProjectRoot();

    public static string ResourceDirectory { get; } = Path.Combine(
        ProjectRoot,
        "res");

    public static string BundleExtractorScript { get; } =
        ResolveBundledFile(
            Path.Combine("src", "SpinePet", "Tools", "extract_spine_bundle.py"),
            Path.Combine("Tools", "extract_spine_bundle.py"));

    private static string FindProjectRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SpinePet.sln")) ||
                Directory.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return AppContext.BaseDirectory;
    }

    private static string ResolveBundledFile(
        string sourceRelativePath,
        string outputRelativePath)
    {
        string sourcePath = Path.Combine(ProjectRoot, sourceRelativePath);
        return File.Exists(sourcePath)
            ? sourcePath
            : Path.Combine(AppContext.BaseDirectory, outputRelativePath);
    }
}
