using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace HezbClips.App.Windows;

/// <summary>Every change in the editor is a command, so each one can be undone and redone in order:
/// adding a shape, clearing all, deleting, moving, cropping, rotating, restoring the original, auto-masking.</summary>
public sealed partial class ImageEditorWindow
{
    interface IEditCmd
    {
        string Name { get; }
        void Do();
        void Undo();
    }

    sealed class AddCmd(ImageEditorWindow w, UIElement el, string name) : IEditCmd
    {
        public string Name => name;
        public void Do() => w.AttachItem(el);
        public void Undo() => w.DetachItem(el);
    }

    sealed class DeleteCmd(ImageEditorWindow w, UIElement el, int index) : IEditCmd
    {
        public string Name => "xóa chú thích";
        public void Do() => w.DetachItem(el);
        public void Undo() => w.AttachItem(el, index);
    }

    sealed class ClearCmd(ImageEditorWindow w, List<UIElement> items) : IEditCmd
    {
        public string Name => "xóa hết chú thích";
        public void Do() { foreach (var i in items) w.DetachItem(i); }
        public void Undo() { foreach (var i in items) w.AttachItem(i); }
    }

    /// <summary>Several changes that undo together (e.g. one auto-mask pass that covers many words).</summary>
    sealed class GroupCmd(string name, List<IEditCmd> cmds) : IEditCmd
    {
        public string Name => name;
        public void Do() { foreach (var c in cmds) c.Do(); }
        public void Undo() { for (int i = cmds.Count - 1; i >= 0; i--) cmds[i].Undo(); }
    }

    sealed class MoveCmd(ImageEditorWindow w, UIElement el, Vector delta) : IEditCmd
    {
        public string Name => "di chuyển";
        public void Do() => w.Shift(el, delta);
        public void Undo() => w.Shift(el, -delta);
    }

    /// <summary>Crop / rotate / restore: swaps the picture. Annotations were baked into the new picture, so they leave the canvas;
    /// undo brings back the old picture together with its annotations.</summary>
    sealed class BaseCmd(ImageEditorWindow w, BitmapSource oldBase, BitmapSource newBase, List<UIElement> oldItems, string name) : IEditCmd
    {
        public string Name => name;
        public void Do() { foreach (var i in oldItems) w.DetachItem(i); w.SetBase(newBase); }
        public void Undo() { w.SetBase(oldBase); foreach (var i in oldItems) w.AttachItem(i); }
    }

    readonly Stack<IEditCmd> _undo = new();
    readonly Stack<IEditCmd> _redo = new();
    IEditCmd? _savedTop;   // the command on top when the file was last saved: dirty = top differs

    bool Dirty => !ReferenceEquals(_undo.Count > 0 ? _undo.Peek() : null, _savedTop);

    /// <summary>Run a new command: it can be undone, and anything that was undone before is forgotten.</summary>
    void Run(IEditCmd cmd)
    {
        cmd.Do();
        _undo.Push(cmd); _redo.Clear();
        RefreshUi();
    }

    void Undo()
    {
        CommitText();
        if (_undo.Count == 0) { Hud("", "Không còn gì để hoàn tác", ok: false); return; }
        var cmd = _undo.Pop(); cmd.Undo(); _redo.Push(cmd);
        RefreshUi();
        Hud("", "Đã hoàn tác: " + cmd.Name);
    }

    void Redo()
    {
        CommitText();
        if (_redo.Count == 0) { Hud("", "Không còn gì để làm lại", ok: false); return; }
        var cmd = _redo.Pop(); cmd.Do(); _undo.Push(cmd);
        RefreshUi();
        Hud("", "Đã làm lại: " + cmd.Name);
    }

    void ClearAll()
    {
        CommitText();
        if (_items.Count == 0) { Hud("", "Chưa có chú thích nào để xóa", ok: false); return; }
        var n = _items.Count;
        Run(new ClearCmd(this, _items.ToList()));
        Hud("", $"Đã xóa {n} chú thích (Ctrl+Z để lấy lại)");
    }

    // ---- low level: what the commands call ----

    void AttachItem(UIElement el, int? index = null)
    {
        if (!_canvas.Children.Contains(el)) _canvas.Children.Add(el);
        if (index is { } i && i >= 0 && i <= _items.Count) _items.Insert(i, el); else _items.Add(el);
        // keep the on-canvas order equal to _items order so later shapes stay on top
        foreach (var it in _items) { _canvas.Children.Remove(it); _canvas.Children.Add(it); }
    }

    void DetachItem(UIElement el)
    {
        _items.Remove(el); _canvas.Children.Remove(el);
        if (ReferenceEquals(_selected, el)) _selected = null;
    }

    /// <summary>Move an annotation by a vector (undoable through MoveCmd).</summary>
    void Shift(UIElement el, Vector d)
    {
        if (el is FrameworkElement { Tag: "mosaic" } fe)
        {
            // a mosaic shows the pixelated picture under its own position, so the brush must follow the move
            Canvas.SetLeft(fe, Canvas.GetLeft(fe) + d.X); Canvas.SetTop(fe, Canvas.GetTop(fe) + d.Y);
            if (fe is Shape s) s.Fill = MosaicBrush(new Rect(Canvas.GetLeft(fe), Canvas.GetTop(fe), fe.Width, fe.Height));
        }
        else
        {
            if (el.RenderTransform is not TranslateTransform t) el.RenderTransform = t = new TranslateTransform();
            t.X += d.X; t.Y += d.Y;
        }
        RefreshUi();
    }

    /// <summary>Swap the picture under the annotations (crop, rotate, restore) and resize the canvas.</summary>
    void SetBase(BitmapSource bmp)
    {
        _bmp = bmp; _pixelated = Pixelate(bmp);
        _canvas.Width = _ui.Width = bmp.PixelWidth; _canvas.Height = _ui.Height = bmp.PixelHeight;
        _baseImage.Source = bmp; _baseImage.Width = bmp.PixelWidth; _baseImage.Height = bmp.PixelHeight;
        _selected = null; CancelCrop();
        ApplyZoom();
    }
}
