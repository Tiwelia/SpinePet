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

    public static string RendererPageRelativePath { get; } =
        File.Exists(Path.Combine(
            ProjectRoot,
            "src",
            "SpinePet",
            "Web",
            "renderer-host.html"))
            ? "src/SpinePet/Web/renderer-host.html"
            : "Web/renderer-host.html";

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
}
