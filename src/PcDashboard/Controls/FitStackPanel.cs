using System.Windows;
using System.Windows.Controls;

namespace PcDashboard.Controls;

/// <summary>
/// Dikey yığın; alana tamamen sığmayan öğeleri hiç çizmez. Haber listesinde son başlığın
/// yarım görünmesini engeller.
/// </summary>
public sealed class FitStackPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        double height = 0, width = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            if (height + child.DesiredSize.Height > availableSize.Height) break;
            height += child.DesiredSize.Height;
            width = Math.Max(width, child.DesiredSize.Width);
        }

        return new Size(double.IsInfinity(availableSize.Width) ? width : availableSize.Width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var y = 0.0;
        foreach (UIElement child in InternalChildren)
        {
            var h = child.DesiredSize.Height;
            if (y + h > finalSize.Height + 0.5)
            {
                child.Arrange(new Rect(0, 0, 0, 0));
                continue;
            }

            child.Arrange(new Rect(0, y, finalSize.Width, h));
            y += h;
        }

        return finalSize;
    }
}
