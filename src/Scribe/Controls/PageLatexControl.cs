using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Scribe.Ink;
using Scribe.Models;
using Scribe.Storage;
using Scribe.Views;
using WpfMath.Controls;

namespace Scribe.Controls;

public sealed class PageLatexControl : Border
{
    public LatexDto Model { get; }
    private readonly FormulaControl _formulaControl;
    private bool _isDragging;
    private Point _dragStartPoint;
    private Point _initialCanvasPos;

    public event EventHandler? Changed;
    public event EventHandler? RequestDelete;

    public PageLatexControl(LatexDto model)
    {
        Model = model;

        Background = Brushes.Transparent;
        BorderBrush = Brushes.Transparent;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(6);
        Padding = new Thickness(10, 6, 10, 6);
        Cursor = Cursors.Hand;
        Focusable = true;

        _formulaControl = new FormulaControl
        {
            Scale = model.Scale > 0 ? model.Scale : 22,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        UpdateFormula();
        Child = _formulaControl;

        // Context menu
        var menu = new ContextMenu();
        var editItem = new MenuItem { Header = "Edit equation…" };
        editItem.Click += (_, _) => Edit();
        menu.Items.Add(editItem);

        var copyItem = new MenuItem { Header = "Copy LaTeX" };
        copyItem.Click += (_, _) =>
        {
            try { Clipboard.SetText(Model.Latex); } catch { }
        };
        menu.Items.Add(copyItem);

        menu.Items.Add(new Separator());

        var deleteItem = new MenuItem { Header = "Delete equation" };
        deleteItem.Click += (_, _) => RequestDelete?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(deleteItem);

        ContextMenu = menu;

        MouseEnter += (_, _) =>
        {
            if (!_isDragging)
                BorderBrush = (Brush)FindResource("BorderBrush");
        };
        MouseLeave += (_, _) =>
        {
            if (!_isDragging && !IsKeyboardFocused)
                BorderBrush = Brushes.Transparent;
        };

        GotKeyboardFocus += (_, _) => BorderBrush = (Brush)FindResource("AccentLineBrush");
        LostKeyboardFocus += (_, _) => BorderBrush = Brushes.Transparent;

        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        KeyDown += OnKeyDown;
    }

    public void UpdateFormula()
    {
        try
        {
            _formulaControl.Formula = Model.Latex;
            _formulaControl.Scale = Model.Scale > 0 ? Model.Scale : 22;
            _formulaControl.Foreground = new SolidColorBrush(
                InkTheme.Adapt(StrokeCodec.FromHex(Model.Color, Colors.Black)));
        }
        catch
        {
            // If syntax error, formula control displays cleanly or fails gracefully
        }
    }

    public void Edit()
    {
        var owner = Window.GetWindow(this);
        var dialog = new LatexEditDialog(Model.Latex, Model.Scale, isEditing: true);
        if (owner is not null) dialog.Owner = owner;

        if (dialog.ShowDialog() == true)
        {
            Model.Latex = dialog.ResultLatex;
            Model.Scale = dialog.ResultScale;
            UpdateFormula();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            Edit();
            e.Handled = true;
            return;
        }

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
        else if (e.Key == Key.Enter)
        {
            Edit();
            e.Handled = true;
        }
    }

    public void SyncToModel()
    {
        Model.X = InkCanvas.GetLeft(this);
        Model.Y = InkCanvas.GetTop(this);
        if (double.IsNaN(Model.X)) Model.X = 0;
        if (double.IsNaN(Model.Y)) Model.Y = 0;
    }
}
