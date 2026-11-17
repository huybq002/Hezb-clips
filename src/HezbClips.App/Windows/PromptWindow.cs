using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace HezbClips.App.Windows;

/// <summary>Tiny modal "type a line of text" dialog in the app's theme.</summary>
public sealed class PromptWindow : Window
{
    readonly TextBox _box = new() { MinWidth = 340 };
    string? _result;

    PromptWindow(string title, string message, string initial)
    {
        Title = title; SizeToContent = SizeToContent.WidthAndHeight; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        Background = Ui.Brush("Bg"); Foreground = Ui.Brush("Text"); Icon = Ui.Logo("logo-light.png", 64);
        _box.Text = initial;
        var ok = new Button { Content = "Lưu", IsDefault = true, Style = Ui.Style("PrimaryButton"), Padding = new Thickness(20, 7, 20, 7), Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Hủy", IsCancel = true, Padding = new Thickness(16, 7, 16, 7) };
        ok.Click += (_, _) => { _result = _box.Text; DialogResult = true; };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
        row.Children.Add(ok); row.Children.Add(cancel);
        var col = new StackPanel { Margin = new Thickness(20) };
        col.Children.Add(Ui.Label(message, dim: true));
        _box.Margin = new Thickness(0, 8, 0, 0); col.Children.Add(_box); col.Children.Add(row);
        Content = col;
        Loaded += (_, _) => { _box.Focus(); _box.SelectAll(); };
    }

    /// <summary>Returns the entered text, or null when cancelled.</summary>
    public static string? Ask(Window? owner, string title, string message, string initial = "")
    {
        var w = new PromptWindow(title, message, initial) { Owner = owner };
        return w.ShowDialog() == true ? w._result : null;
    }
}
