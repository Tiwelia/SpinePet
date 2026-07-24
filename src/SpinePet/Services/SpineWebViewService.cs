using System.IO;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace SpinePet.Services;

public static class SpineWebViewService
{
    public const string VirtualHost = "spinepet.local";

    private static readonly Lazy<Task<CoreWebView2Environment>> SharedWebViewEnvironment =
        new(CreateSharedWebViewEnvironment);

    public static async Task InitializeAsync(
        WebView2 webView,
        EventHandler<CoreWebView2WebMessageReceivedEventArgs> webMessageHandler)
    {
        if (webView.CoreWebView2 != null)
            return;

        var env = await SharedWebViewEnvironment.Value;
        await webView.EnsureCoreWebView2Async(env);
        var core = webView.CoreWebView2;
        if (core == null)
            return;

        webView.DefaultBackgroundColor = System.Drawing.Color.Transparent;
        core.Settings.AreDefaultScriptDialogsEnabled = false;
        core.Settings.IsScriptEnabled = true;
        core.Settings.IsWebMessageEnabled = true;
        core.WebMessageReceived -= webMessageHandler;
        core.WebMessageReceived += webMessageHandler;
        core.SetVirtualHostNameToFolderMapping(
            VirtualHost,
            FindProjectRoot(),
            CoreWebView2HostResourceAccessKind.Allow);
        core.Navigate($"https://{VirtualHost}/src/SpinePet/www/pet.html");
    }

    public static Task WarmupEnvironmentAsync() => SharedWebViewEnvironment.Value;

    public static string ToVirtualUrl(string filePath)
    {
        var root = FindProjectRoot().Replace('\\', '/').TrimEnd('/');
        var normalized = filePath.Replace('\\', '/');
        var relative = normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? normalized.Substring(root.Length).TrimStart('/')
            : normalized;
        return $"https://{VirtualHost}/{relative}";
    }

    public static string FindProjectRoot()
    {
        var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "res")) &&
                Directory.Exists(Path.Combine(dir.FullName, "src")))
                return dir.FullName;
            dir = dir.Parent;
        }

        return AppDomain.CurrentDomain.BaseDirectory;
    }

    public static string FindResPath() => Path.Combine(FindProjectRoot(), "res");

    public static void AppendLog(string owner, string message)
    {
        try
        {
            var logDirectory = Path.Combine(FindProjectRoot(), "log");
            Directory.CreateDirectory(logDirectory);
            var logPath = Path.Combine(logDirectory, $"spinepet-{DateTime.Now:yyyyMMdd}.log");
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{owner}] {message}{Environment.NewLine}";
            File.AppendAllText(logPath, line);
        }
        catch
        {
        }
    }

    private static Task<CoreWebView2Environment> CreateSharedWebViewEnvironment()
    {
        var userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SpinePet",
            "WebView2");
        Directory.CreateDirectory(userDataFolder);
        return CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
    }
}
