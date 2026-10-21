using System.Windows;
using System.Windows.Controls;

namespace PcDashboard.Controls;

/// <summary>
/// Çocuklarını yan yana dizip taban çizgilerine göre hizalar (CSS'teki align-items: baseline).
/// Taban çizgisi yalnızca TrackedText için bilinir; diğerleri alttan hizalanır.
/// </summary>
public class BaselinePanel : Panel
{
    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(BaselinePanel),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    private static double BaselineOf(UIElement child) =>
        child is TrackedText t ? t.Baseline + t.Margin.Top : child.DesiredSize.Height;

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = 0, above = 0, below = 0;
        var visible = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            if (child.Visibility == Visibility.Collapsed) continue;
            visible++;
            width += child.DesiredSize.Width;
            var b = BaselineOf(child);
            above = Math.Max(above, b);
            below = Math.Max(below, child.DesiredSize.Height - b);
        }

        width += Math.Max(0, visible - 1) * Spacing;
        return new Size(width, above + below);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double above = 0;
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility != Visibility.Collapsed) above = Math.Max(above, BaselineOf(child));
        }

        var x = 0.0;
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            var y = above - BaselineOf(child);
            child.Arrange(new Rect(new Point(x, y), child.DesiredSize));
            x += child.DesiredSize.Width + Spacing;
        }

        return finalSize;
    }
}
