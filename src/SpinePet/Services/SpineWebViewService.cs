using System.IO;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using SpinePet.Infrastructure;

namespace SpinePet.Services;

public static class SpineWebViewService
{
    private const string VirtualHost = "spinepet.local";

    private static readonly Lazy<Task<CoreWebView2Environment>> SharedWebViewEnvironment =
        new(CreateSharedWebViewEnvironment);

    public static async Task InitializeAsync(
        WebView2 webView,
        EventHandler<CoreWebView2WebMessageReceivedEventArgs> webMessageHandler)
    {
        if (webView.CoreWebView2 != null)
        {
            return;
        }

        CoreWebView2Environment environment = await SharedWebViewEnvironment.Value;
        await webView.EnsureCoreWebView2Async(environment);
        CoreWebView2? core = webView.CoreWebView2;
        if (core == null)
        {
            return;
        }

        webView.DefaultBackgroundColor = System.Drawing.Color.Transparent;
        core.Settings.AreDefaultScriptDialogsEnabled = false;
        core.Settings.IsScriptEnabled = true;
        core.Settings.IsWebMessageEnabled = true;
        core.WebMessageReceived -= webMessageHandler;
        core.WebMessageReceived += webMessageHandler;
        core.SetVirtualHostNameToFolderMapping(
            VirtualHost,
            AppPaths.ProjectRoot,
            CoreWebView2HostResourceAccessKind.Allow);
        core.Navigate(
            $"https://{VirtualHost}/{AppPaths.RendererPageRelativePath}");
    }

    public static Task WarmupEnvironmentAsync() => SharedWebViewEnvironment.Value;

    public static string ToVirtualUrl(string filePath)
    {
        string root = AppPaths.ProjectRoot.Replace('\\', '/').TrimEnd('/');
        string normalized = filePath.Replace('\\', '/');
        string relative = normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? normalized[root.Length..].TrimStart('/')
            : normalized;
        return $"https://{VirtualHost}/{relative}";
    }

    private static Task<CoreWebView2Environment> CreateSharedWebViewEnvironment()
    {
        string userDataFolder = Path.Combine(AppPaths.LocalDataDirectory, "WebView2");
        Directory.CreateDirectory(userDataFolder);
        return CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
    }
}
