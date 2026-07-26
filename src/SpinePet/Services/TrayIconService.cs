using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SpinePet.Infrastructure;

namespace SpinePet.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly Action _openPanel;
    private readonly Action _showAll;
    private readonly Action _hideAll;
    private readonly Action _exit;
    private NotifyIcon? _notifyIcon;
    private ContextMenuStrip? _contextMenu;

    public TrayIconService(
        Action openPanel,
        Action showAll,
        Action hideAll,
        Action exit)
    {
        _openPanel = openPanel;
        _showAll = showAll;
        _hideAll = hideAll;
        _exit = exit;
    }

    public void Initialize()
    {
        if (_notifyIcon != null)
        {
            return;
        }

        _contextMenu = new ContextMenuStrip();
        _contextMenu.Items.Add("Open Panel", null, (_, _) => _openPanel());
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add("Show All", null, (_, _) => _showAll());
        _contextMenu.Items.Add("Hide All", null, (_, _) => _hideAll());
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add("Exit", null, (_, _) => _exit());

        _notifyIcon = new NotifyIcon
        {
            Text = "SpinePet — Render Mode",
            Icon = CreateIcon(),
            ContextMenuStrip = _contextMenu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => _openPanel();
    }

    public void Dispose()
    {
        if (_notifyIcon == null)
        {
            return;
        }

        _notifyIcon.Visible = false;
        _notifyIcon.Icon?.Dispose();
        _notifyIcon.Dispose();
        _notifyIcon = null;
        _contextMenu?.Dispose();
        _contextMenu = null;
        GC.SuppressFinalize(this);
    }

    private static Icon CreateIcon()
    {
        using Bitmap bitmap = new(32, 32);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        using SolidBrush backgroundBrush =
            new(Color.FromArgb(255, 137, 180, 250));
        graphics.FillEllipse(backgroundBrush, 1, 1, 30, 30);
        using Font font = new("Segoe UI", 14, FontStyle.Bold);
        using SolidBrush textBrush =
            new(Color.FromArgb(255, 30, 30, 46));
        graphics.DrawString("S", font, textBrush, 6, 3);

        IntPtr iconHandle = bitmap.GetHicon();
        try
        {
            using Icon temporaryIcon = Icon.FromHandle(iconHandle);
            return (Icon)temporaryIcon.Clone();
        }
        finally
        {
            if (!DestroyIcon(iconHandle))
            {
                AppLogger.Write(
                    nameof(TrayIconService),
                    $"destroy-icon-failed error={Marshal.GetLastPInvokeError()}");
            }
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr iconHandle);
}
