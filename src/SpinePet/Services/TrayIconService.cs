using System.Drawing;
using System.Windows.Forms;
using Application = System.Windows.Forms.Application;

namespace SpinePet.Services;

public class TrayIconService : IDisposable
{
    private NotifyIcon? _notifyIcon;
    private readonly Action _openPanel;
    private readonly Action _showAll;
    private readonly Action _hideAll;
    private readonly Action _exit;

    public TrayIconService(Action openPanel, Action showAll, Action hideAll, Action exit)
    {
        _openPanel = openPanel;
        _showAll = showAll;
        _hideAll = hideAll;
        _exit = exit;
    }

    public void Initialize()
    {
        _notifyIcon = new NotifyIcon
        {
            Text = "SpinePet — Render Mode",
            Visible = true
        };

        using var bitmap = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.Transparent);
        using var bgBrush = new SolidBrush(Color.FromArgb(255, 137, 180, 250));
        g.FillEllipse(bgBrush, 1, 1, 30, 30);
        using var font = new Font("Segoe UI", 14, FontStyle.Bold);
        using var textBrush = new SolidBrush(Color.FromArgb(255, 30, 30, 46));
        g.DrawString("S", font, textBrush, 6, 3);
        _notifyIcon.Icon = Icon.FromHandle(bitmap.GetHicon());

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Panel", null, (_, _) => _openPanel());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Show All", null, (_, _) => _showAll());
        menu.Items.Add("Hide All", null, (_, _) => _hideAll());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => _exit());

        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.DoubleClick += (_, _) => _openPanel();
    }

    public void Dispose()
    {
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
    }
}
