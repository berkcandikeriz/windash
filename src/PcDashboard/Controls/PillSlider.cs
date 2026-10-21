using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace PcDashboard.Controls;

/// <summary>
/// Taslaktaki hap biçimli kaydırıcı (parlaklık / ses): 44 px yüksek, soldan dolan vurgu rengi,
/// solda ikon, sağda değer. Tıklayıp sürükleyerek veya fare tekerleğiyle değiştirilir.
/// IconCommand verilirse ikonun üstüne tıklamak değeri değiştirmez, komutu çalıştırır (ör. sessize alma).
/// Görünümü Themes/Glass.xaml içinde.
/// </summary>
public sealed class PillSlider : RangeBase
{
    private const double IconHitWidth = 48;

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(PillSlider), new PropertyMetadata(Brushes.White));

    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(Geometry), typeof(PillSlider), new PropertyMetadata(null));

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(PillSlider), new PropertyMetadata(""));

    public static readonly DependencyProperty IconCommandProperty = DependencyProperty.Register(
        nameof(IconCommand), typeof(ICommand), typeof(PillSlider), new PropertyMetadata(null));

    public static readonly DependencyProperty IsDraggingProperty = DependencyProperty.Register(
        nameof(IsDragging), typeof(bool), typeof(PillSlider), new PropertyMetadata(false));

    private FrameworkElement? _root;
    private FrameworkElement? _fill;

    static PillSlider()
    {
        MinimumProperty.OverrideMetadata(typeof(PillSlider), new FrameworkPropertyMetadata(0.0));
        MaximumProperty.OverrideMetadata(typeof(PillSlider), new FrameworkPropertyMetadata(100.0));
        FocusableProperty.OverrideMetadata(typeof(PillSlider), new FrameworkPropertyMetadata(false));
    }

    public Brush Fill
    {
        get => (Brush)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public Geometry? Icon
    {
        get => (Geometry?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public ICommand? IconCommand
    {
        get => (ICommand?)GetValue(IconCommandProperty);
        set => SetValue(IconCommandProperty, value);
    }

    public bool IsDragging
    {
        get => (bool)GetValue(IsDraggingProperty);
        private set => SetValue(IsDraggingProperty, value);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _root = GetTemplateChild("PART_Root") as FrameworkElement;
        _fill = GetTemplateChild("PART_Fill") as FrameworkElement;
        UpdateVisuals();
    }

    protected override void OnValueChanged(double oldValue, double newValue)
    {
        base.OnValueChanged(oldValue, newValue);
        UpdateVisuals();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        UpdateVisuals();
    }

    /// <summary>Hap şeklinde kırpma (CSS: border-radius 22px + overflow hidden) ve dolgu genişliği.</summary>
    private void UpdateVisuals()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;

        if (_root is not null)
        {
            var radius = ActualHeight / 2;
            var clip = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight), radius, radius);
            clip.Freeze();
            _root.Clip = clip;
        }

        if (_fill is not null)
        {
            var range = Maximum - Minimum;
            var ratio = range <= 0 ? 0 : Math.Clamp((Value - Minimum) / range, 0, 1);
            _fill.Width = ActualWidth * ratio;
        }
    }

    private void SetFromPoint(Point p)
    {
        if (ActualWidth <= 0) return;
        var ratio = Math.Clamp(p.X / ActualWidth, 0, 1);
        Value = Math.Round(Minimum + ratio * (Maximum - Minimum));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!IsEnabled) return;

        var p = e.GetPosition(this);
        if (IconCommand is { } command && p.X <= IconHitWidth)
        {
            if (command.CanExecute(null)) command.Execute(null);
            e.Handled = true;
            return;
        }

        CaptureMouse();
        IsDragging = true;
        SetFromPoint(p);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (IsDragging) SetFromPoint(e.GetPosition(this));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!IsDragging) return;
        SetFromPoint(e.GetPosition(this));
        IsDragging = false;
        ReleaseMouseCapture();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        IsDragging = false;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!IsEnabled) return;
        Value = Math.Clamp(Value + Math.Sign(e.Delta) * 2, Minimum, Maximum);
        e.Handled = true;
    }
}
