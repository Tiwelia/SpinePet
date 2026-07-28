using System.Diagnostics;
using System.Windows;
using SpinePet.Infrastructure;
using SpinePet.Models;
using SpinePet.Services;
using SpinePet.Views;
using Application = System.Windows.Application;

namespace SpinePet;

public partial class App : Application
{
    private const string SingleInstanceMutexName =
        @"Local\SpinePet.SingleInstance.v1";

    private TrayIconService? _trayIcon;
    private CharacterManager? _characterManager;
    private MainWindow? _mainWindow;
    private EmergencyExitHotkey? _emergencyExitHotkey;
    private readonly ManualResetEventSlim _shutdownCompleted = new(false);
    private Mutex? _singleInstanceMutex;
    private int _emergencyExitRequested;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            SingleInstanceMutexName,
            out bool isFirstInstance);
        if (!isFirstInstance)
        {
            AppLogger.Write(nameof(App), "duplicate-instance-blocked");
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Shutdown();
            return;
        }

        _emergencyExitHotkey = new EmergencyExitHotkey(
            RequestEmergencyExit);
        _emergencyExitHotkey.Start();

        ConfigService configService = new();
        CharacterIdentityService identityService = new();
        CharacterResourceDiscoveryService resourceDiscovery =
            new(identityService);
        UnityBundleImportService bundleImporter =
            new(identityService, resourceDiscovery);
        _characterManager = new CharacterManager(configService, identityService);
        _mainWindow = new MainWindow(
            _characterManager,
            resourceDiscovery,
            bundleImporter);

        _trayIcon = new TrayIconService(
            openPanel: () => DispatchToUi(() => _mainWindow.SwitchToConfigMode()),
            showAll: () => DispatchToUi(() => _ = ShowAllCharactersAsync()),
            hideAll: () => DispatchToUi(_characterManager.HideAll),
            exit: () => DispatchToUi(ShutdownApplication)
        );
        _trayIcon.Initialize();

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

    private void RequestEmergencyExit()
    {
        if (Interlocked.Exchange(ref _emergencyExitRequested, 1) != 0)
            return;

        AppLogger.Write(
            nameof(App),
            "emergency-exit-requested shortcut=Ctrl+Alt+Shift+F12");

        try
        {
            Dispatcher.BeginInvoke(ShutdownApplication);
        }
        catch
        {
            // The watchdog below still terminates a broken dispatcher.
        }

        _ = Task.Run(() =>
        {
            if (_shutdownCompleted.Wait(TimeSpan.FromSeconds(1.5)))
                return;

            Process.GetCurrentProcess().Kill(entireProcessTree: true);
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            if (_characterManager != null)
            {
                _characterManager.SaveAllState();
                _characterManager.Close();
            }

            _trayIcon?.Dispose();
            _emergencyExitHotkey?.Dispose();
            if (_singleInstanceMutex != null)
            {
                try
                {
                    _singleInstanceMutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                    // The mutex was not owned during a partial startup.
                }

                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
            }
            base.OnExit(e);
        }
        finally
        {
            _shutdownCompleted.Set();
        }
    }
}
