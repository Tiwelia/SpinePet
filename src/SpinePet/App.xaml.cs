using System.IO;
using System.Windows;
using SpinePet.Services;
using SpinePet.Views;
using Application = System.Windows.Application;

namespace SpinePet;

public partial class App : Application
{
    private TrayIconService? _trayIcon;
    private PetManager? _petManager;
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var configService = new ConfigService();
        _petManager = new PetManager(configService);

        _mainWindow = new MainWindow(_petManager);

        _trayIcon = new TrayIconService(
            openPanel: () =>
            {
                _mainWindow.Dispatcher.Invoke(() =>
                {
                    // Reopen panel and switch to config mode
                    _mainWindow.SwitchToConfigMode();
                });
            },
            showAll: () => _petManager.ShowAll(),
            hideAll: () => _petManager.HideAll(),
            exit: () =>
            {
                _mainWindow.Dispatcher.Invoke(() =>
                {
                    _petManager.SaveAllState();
                    _petManager.RemoveAllWindows();
                    _trayIcon?.Dispose();
                    Shutdown();
                });
            }
        );
        _trayIcon.Initialize();

        _ = SpineWebViewService.WarmupEnvironmentAsync();

        AutoScanResources();
        _ = _petManager.RestoreAllAsync(configMode: false);
        _ = _petManager.PrimeHiddenWindowsAsync();

        _mainWindow.Show();
    }

    private void AutoScanResources()
    {
        try
        {
            var resPath = FindResPath();
            if (!Directory.Exists(resPath)) return;

            foreach (var dir in Directory.GetDirectories(resPath))
            {
                var skelFiles = Directory.GetFiles(dir, "*.skel");
                if (skelFiles.Length == 0) continue;
                var atlasFiles = Directory.GetFiles(dir, "*.atlas");
                var pngFiles = Directory.GetFiles(dir, "*.png");
                if (atlasFiles.Length > 0 && pngFiles.Length > 0)
                {
                    if (!_petManager!.Characters.Any(c => c.SkelPath == skelFiles[0]))
                        _petManager.AddCharacter(skelFiles[0], atlasFiles[0], pngFiles[0]);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Auto-scan error: {ex.Message}");
        }
    }

    private static string FindResPath() => SpineWebViewService.FindResPath();

    protected override void OnExit(ExitEventArgs e)
    {
        _petManager?.SaveAllState();
        _petManager?.RemoveAllWindows();
        _trayIcon?.Dispose();
        base.OnExit(e);
    }
}
