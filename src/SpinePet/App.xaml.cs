using System.Windows;
using SpinePet.Infrastructure;
using SpinePet.Models;
using SpinePet.Services;
using SpinePet.Views;
using Application = System.Windows.Application;

namespace SpinePet;

public partial class App : Application
{
    private TrayIconService? _trayIcon;
    private CharacterManager? _characterManager;
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ConfigService configService = new();
        CharacterResourceDiscoveryService resourceDiscovery = new();
        _characterManager = new CharacterManager(configService);
        _mainWindow = new MainWindow(_characterManager, resourceDiscovery);

        _trayIcon = new TrayIconService(
            openPanel: () => DispatchToUi(() => _mainWindow.SwitchToConfigMode()),
            showAll: () => DispatchToUi(() => _ = ShowAllCharactersAsync()),
            hideAll: () => DispatchToUi(_characterManager.HideAll),
            exit: () => DispatchToUi(ShutdownApplication)
        );
        _trayIcon.Initialize();

        _ = WarmupWebViewEnvironmentAsync();

        ImportDiscoveredResources(resourceDiscovery);
        _ = RestoreCharactersAsync();

        _mainWindow.Show();
    }

    private void ImportDiscoveredResources(
        CharacterResourceDiscoveryService resourceDiscovery)
    {
        if (_characterManager == null)
        {
            return;
        }

        foreach (CharacterResourceFiles resources in
                 resourceDiscovery.Discover(AppPaths.ResourceDirectory))
        {
            _characterManager.AddCharacter(resources);
        }
    }

    private async Task RestoreCharactersAsync()
    {
        if (_characterManager == null)
        {
            return;
        }

        try
        {
            await _characterManager.RestoreAllAsync(configMode: false);
        }
        catch (Exception exception)
        {
            AppLogger.Write(
                nameof(App),
                $"restore-failed message={exception.Message}");
        }
    }

    private static async Task WarmupWebViewEnvironmentAsync()
    {
        try
        {
            await SpineWebViewService.WarmupEnvironmentAsync();
        }
        catch (Exception exception)
        {
            AppLogger.Write(
                nameof(App),
                $"webview-warmup-failed message={exception.Message}");
        }
    }

    private async Task ShowAllCharactersAsync()
    {
        if (_characterManager == null)
        {
            return;
        }

        try
        {
            await _characterManager.ShowAllAsync();
        }
        catch (Exception exception)
        {
            AppLogger.Write(
                nameof(App),
                $"show-all-failed message={exception.Message}");
        }
    }

    private void DispatchToUi(Action action)
    {
        if (_mainWindow == null)
        {
            return;
        }

        _mainWindow.Dispatcher.Invoke(action);
    }

    private void ShutdownApplication()
    {
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_characterManager != null)
        {
            _characterManager.SaveAllState();
            _characterManager.Close();
        }

        _trayIcon?.Dispose();
        base.OnExit(e);
    }
}
