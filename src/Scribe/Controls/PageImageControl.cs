using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Scribe.Models;

namespace Scribe.Controls;

public sealed class PageImageControl : Border
{
    public ImageDto Model { get; }

    private readonly Image _image;
    private readonly Thumb _resizeGrip;
    private readonly Grid _container;

    private bool _isDragging;
    private Point _dragStartPoint;
    private Point _initialCanvasPos;
    private double _origWidth;
    private double _origHeight;

    public event EventHandler? Changed;
    public event EventHandler? RequestDelete;

    public PageImageControl(ImageDto model, string sectionDir)
    {
        Model = model;

        Background = Brushes.Transparent;
        BorderBrush = Brushes.Transparent;
        BorderThickness = new Thickness(1.5);
        CornerRadius = new CornerRadius(4);
        Cursor = Cursors.Hand;
        Focusable = true;

        _container = new Grid();

        var path = Path.Combine(sectionDir, model.File.Replace('/', Path.DirectorySeparatorChar));
        BitmapImage? bitmap = null;

        if (File.Exists(path))
        {
            try
            {
                bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();

                _origWidth = bitmap.PixelWidth;
                _origHeight = bitmap.PixelHeight;
            }
            catch
            {
                // Unreadable file
            }
        }

        double width = model.Width > 0 ? model.Width : (_origWidth > 0 ? _origWidth : 300);
        double height = model.Height > 0 ? model.Height : (_origHeight > 0 ? _origHeight : 200);

        Width = width;
        Height = height;

        _image = new Image
        {
            Source = bitmap,
            Stretch = Stretch.Fill,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        _container.Children.Add(_image);

        // Resize grip at bottom right
        _resizeGrip = new Thumb
        {
            Width = 14,
            Height = 14,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Cursor = Cursors.SizeNWSE,
            Margin = new Thickness(0, 0, 2, 2),
            Visibility = Visibility.Collapsed,
            Template = CreateGripTemplate(),
        };

        _resizeGrip.DragDelta += OnResizeDragDelta;
        _container.Children.Add(_resizeGrip);

        Child = _container;

        // Context menu
        var menu = new ContextMenu();

        var copyItem = new MenuItem { Header = "Copy image" };
        copyItem.Click += (_, _) =>
        {
            if (_image.Source is BitmapSource bs)
            {
                try { Clipboard.SetImage(bs); } catch { }
            }
        };
        menu.Items.Add(copyItem);

        var resetSize = new MenuItem { Header = "Original size" };
        resetSize.Click += (_, _) =>
        {
            if (_origWidth > 0 && _origHeight > 0)
            {
                Width = _origWidth;
                Height = _origHeight;
                SyncToModel();
                Changed?.Invoke(this, EventArgs.Empty);
            }
        };
        menu.Items.Add(resetSize);

        menu.Items.Add(new Separator());

        var deleteItem = new MenuItem { Header = "Delete image" };
        deleteItem.Click += (_, _) => RequestDelete?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(deleteItem);

        ContextMenu = menu;

        MouseEnter += (_, _) =>
        {
            if (!_isDragging)
            {
                BorderBrush = (Brush)FindResource("BorderBrush");
                _resizeGrip.Visibility = Visibility.Visible;
            }
        };

        MouseLeave += (_, _) =>
        {
            if (!_isDragging && !IsKeyboardFocused)
            {
                BorderBrush = Brushes.Transparent;
                _resizeGrip.Visibility = Visibility.Collapsed;
            }
        };

        GotKeyboardFocus += (_, _) =>
        {
            BorderBrush = (Brush)FindResource("AccentLineBrush");
            _resizeGrip.Visibility = Visibility.Visible;
        };

        LostKeyboardFocus += (_, _) =>
        {
            BorderBrush = Brushes.Transparent;
            _resizeGrip.Visibility = Visibility.Collapsed;
        };

        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        KeyDown += OnKeyDown;
    }

    private ControlTemplate CreateGripTemplate()
    {
        var template = new ControlTemplate(typeof(Thumb));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
        border.SetValue(Border.WidthProperty, 12.0);
        border.SetValue(Border.HeightProperty, 12.0);
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
        border.SetResourceReference(Border.BackgroundProperty, "AccentBrush");

        template.VisualTree = border;
        return template;
    }

    private void OnResizeDragDelta(object sender, DragDeltaEventArgs e)
    {
        double newWidth = Math.Max(40, Width + e.HorizontalChange);
        double newHeight = Math.Max(30, Height + e.VerticalChange);

        // If Shift is pressed, maintain aspect ratio
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && _origWidth > 0 && _origHeight > 0)
        {
            double ratio = _origHeight / _origWidth;
            newHeight = newWidth * ratio;
        }

        Width = newWidth;
        Height = newHeight;
        SyncToModel();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Focus();
        _isDragging = true;
        _dragStartPoint = e.GetPosition(Parent as IInputElement);
        _initialCanvasPos = new Point(InkCanvas.GetLeft(this), InkCanvas.GetTop(this));
        if (double.IsNaN(_initialCanvasPos.X)) _initialCanvasPos.X = 0;
        if (double.IsNaN(_initialCanvasPos.Y)) _initialCanvasPos.Y = 0;

        CaptureMouse();
        BorderBrush = (Brush)FindResource("AccentLineBrush");
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging || Parent is not UIElement parentEl) return;

        var currentPoint = e.GetPosition(parentEl);
        double deltaX = currentPoint.X - _dragStartPoint.X;
        double deltaY = currentPoint.Y - _dragStartPoint.Y;

        double newLeft = Math.Max(0, _initialCanvasPos.X + deltaX);
        double newTop = Math.Max(0, _initialCanvasPos.Y + deltaY);

        InkCanvas.SetLeft(this, newLeft);
        InkCanvas.SetTop(this, newTop);

        e.Handled = true;
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            ReleaseMouseCapture();
            BorderBrush = IsKeyboardFocused ? (Brush)FindResource("AccentLineBrush") : Brushes.Transparent;
            SyncToModel();
            Changed?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Delete or Key.Back)
        {
            RequestDelete?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    public void SyncToModel()
    {
        Model.X = InkCanvas.GetLeft(this);
        Model.Y = InkCanvas.GetTop(this);
        Model.Width = Width;
        Model.Height = Height;
        if (double.IsNaN(Model.X)) Model.X = 0;
        if (double.IsNaN(Model.Y)) Model.Y = 0;
    }
}
