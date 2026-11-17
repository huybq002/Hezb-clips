using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HezbClips.Core.Hotkeys;
using HezbClips.Core.Settings;

namespace HezbClips.App.Windows;

public sealed record MainActions(Action Region, Action Full, Action Window, Action Delay, Action CopyRegion, Action Pin, Action Scroll,
    Action Record, Action Gif, Action<string> Edit, Action<string> PinFile, Action<AppSettings> Save, Action Redetect, Action<AppTheme> Theme);

/// <summary>Main app window: quick capture buttons on top, the media library below, and a gear button
/// that swaps the library for the settings sections.</summary>
public sealed class MainWindow : Window
{
    readonly LibraryView _library;
    readonly SettingsView _settings;
    readonly ContentControl _body = new();
    readonly Button _gear = new() { Content = "⚙", ToolTip = "Cài đặt", Style = Ui.Style("IconButton") };
    readonly TextBlock _title = new() { FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Ui.Brush("Text"), VerticalAlignment = VerticalAlignment.Center };
    bool _inSettings;

    public MainWindow(AppSettings s, string? ffmpeg, string dataDir, IReadOnlyList<HotkeyFailure> failures, MainActions a)
    {
        Title = "Hezb Clips"; Width = 1040; Height = 700; MinWidth = 760; MinHeight = 480;
        Icon = Ui.Logo("logo-light.png", 64); Background = Ui.Brush("Bg"); WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _library = new LibraryView(s.OutputDir, ffmpeg, dataDir, a.Edit, a.PinFile);
        _settings = new SettingsView(s, failures, a.Save, a.Redetect, a.Theme);

        var header = new DockPanel { Margin = new Thickness(20, 16, 20, 12) };
        var logo = new Ui.LogoImage { Width = 36, Height = 36, Margin = new Thickness(0, 0, 10, 0) };
        _title.Text = "Hezb Clips";
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(logo); left.Children.Add(_title);
        DockPanel.SetDock(left, Dock.Left); header.Children.Add(left);

        _gear.Click += (_, _) => { if (_inSettings) ShowLibrary(); else ShowSettings(); };
        DockPanel.SetDock(_gear, Dock.Right); header.Children.Add(_gear);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 10, 0) };
        actions.Children.Add(Action("✂", "Chụp vùng", s.HotkeyRegionShot, a.Region, primary: true));
        actions.Children.Add(Action("🖥", "Toàn màn hình", s.HotkeyFullShot, a.Full));
        actions.Children.Add(Action("⏺", "Quay video", s.HotkeyRecord, a.Record));
        actions.Children.Add(Action("GIF", "Quay GIF", s.HotkeyGif, a.Gif));
        DockPanel.SetDock(actions, Dock.Right); header.Children.Add(actions);
        header.Children.Add(new Border()); // fills the gap

        // second row: the other capture functions, each with its own hotkey
        var tools = new WrapPanel { Margin = new Thickness(20, 0, 20, 10) };
        tools.Children.Add(new TextBlock { Text = "Công cụ nhanh", Foreground = Ui.Brush("TextDim"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 4) });
        tools.Children.Add(Chip("🗔", "Cửa sổ", "Chụp cửa sổ đang focus", s.HotkeyWindowShot, a.Window));
        tools.Children.Add(Chip("⏱", "Chụp trễ", $"Đếm ngược {s.DelaySeconds} giây rồi chụp toàn màn hình", s.HotkeyDelayShot, a.Delay));
        tools.Children.Add(Chip("📋", "Chụp & copy", "Chụp vùng và copy vào clipboard", s.HotkeyCopyRegion, a.CopyRegion));
        tools.Children.Add(Chip("📌", "Chụp & ghim", "Chụp vùng và ghim lên màn hình", s.HotkeyPinShot, a.Pin));
        tools.Children.Add(Chip("↧", "Cuộn trang", "Chụp trang dài bằng cách tự cuộn", s.HotkeyScroll, a.Scroll));

        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        DockPanel.SetDock(tools, Dock.Top); root.Children.Add(tools);
        _body.Margin = new Thickness(20, 0, 20, 16);
        root.Children.Add(_body);
        Content = root;

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.F5 && !_inSettings) _library.Reload();
            else if (e.Key == Key.Escape && _inSettings) ShowLibrary();
        };
        Activated += (_, _) => { if (!_inSettings) _library.Reload(); };
        ShowLibrary();
    }

    static Button Chip(string icon, string text, string tip, string hotkey, Action click)
    {
        var b = new Button { Content = $"{icon} {text}", Margin = new Thickness(0, 0, 6, 4), Padding = new Thickness(11, 4, 11, 4), FontSize = 12, ToolTip = $"{tip} ({hotkey})" };
        b.Click += (_, _) => click();
        return b;
    }

    static Button Action(string icon, string text, string hotkey, Action click, bool primary = false)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock { Text = icon, FontSize = 15, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        var col = new StackPanel();
        col.Children.Add(new TextBlock { Text = text, FontWeight = FontWeights.SemiBold });
        col.Children.Add(new TextBlock { Text = hotkey, FontSize = 10, Opacity = 0.7 });
        content.Children.Add(col);
        var b = new Button { Content = content, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(14, 6, 14, 6), ToolTip = $"{text} ({hotkey})" };
        if (primary) b.Style = Ui.Style("PrimaryButton");
        b.Click += (_, _) => click();
        return b;
    }

    public void ShowLibrary()
    {
        _inSettings = false; _body.Content = _library; _gear.Content = "⚙"; _gear.ToolTip = "Cài đặt";
        _library.Reload();
    }

    public void ShowSettings(string? section = null)
    {
        _inSettings = true; _body.Content = _settings; _gear.Content = "✕"; _gear.ToolTip = "Đóng cài đặt (Esc)";
        if (section is not null) _settings.ShowSection(section);
    }

    public void RefreshLibrary() => _library.Reload();
    public void SetFolder(string dir) => _library.SetFolder(dir);
}
