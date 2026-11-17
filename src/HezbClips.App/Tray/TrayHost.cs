using System;
using System.Drawing;
using System.Windows.Forms;

namespace HezbClips.App.Tray;

public sealed class TrayHost : IDisposable
{
    readonly NotifyIcon _icon;
    readonly ToolStripMenuItem _record;
    Action? _balloonClick;

    public TrayHost(System.Collections.Generic.IReadOnlyList<(string Text, Action Click)> items, Action toggleRecord, Action settings, Action exit, Action library)
    {
        var menu = new ContextMenuStrip();
        foreach (var (text, click) in items) menu.Items.Add(text, null, (_, _) => click());
        _record = new ToolStripMenuItem("Quay màn hình", null, (_, _) => toggleRecord());
        menu.Items.Add(_record);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Thư viện", null, (_, _) => library());
        menu.Items.Add("Cài đặt", null, (_, _) => settings());
        menu.Items.Add("Thoát", null, (_, _) => exit());
        _icon = new NotifyIcon { Icon = LoadIcon(), Text = "Hezb Clips", ContextMenuStrip = menu, Visible = true };
        _icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) library(); };
        _icon.BalloonTipClicked += (_, _) => _balloonClick?.Invoke();
    }

    static Icon LoadIcon()
    {
        try
        {
            var info = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/" + "app-light.ico"));
            if (info is not null) return new Icon(info.Stream, SystemInformation.SmallIconSize);
        }
        catch (Exception) { /* fall back */ }
        return SystemIcons.Application;
    }

    public void SetRecording(bool on) => _record.Text = on ? "Dừng quay" : "Quay màn hình";

    public void SetTooltip(string text) => _icon.Text = text.Length > 63 ? text[..63] : text;

    public void Balloon(string title, string text, Action? onClick = null)
    {
        _balloonClick = onClick;
        _icon.ShowBalloonTip(4000, title, text, ToolTipIcon.Info);
    }

    public void Dispose() { _icon.Visible = false; _icon.Dispose(); }
}
