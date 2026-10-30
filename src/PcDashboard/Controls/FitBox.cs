using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PcDashboard.Controls;

/// <summary>
/// İçeriği kartın genişliğine yayar; içerik karta dikeyde sığmıyorsa kırpmak yerine orantılı küçültür.
/// Masaüstü panelinde kart boyutları kullanıcıya bırakıldığı için her boyutta okunur kalmasını sağlar.
/// </summary>
public sealed class FitBox : Decorator
{
    private double _scale = 1;

    protected override Size MeasureOverride(Size constraint)
    {
        var child = Child;
        if (child is null) return new Size();

        var width = double.IsInfinity(constraint.Width) ? double.PositiveInfinity : constraint.Width;
        child.Measure(new Size(width, double.PositiveInfinity));
        var desired = child.DesiredSize;

        _scale = 1;
        if (!double.IsInfinity(constraint.Height) && desired.Height > constraint.Height && desired.Height > 0)
        {
            _scale = Math.Max(0.3, constraint.Height / desired.Height);
            // Küçülünce genişlik de büyür (aynı alana ölçekli sığar): yeniden ölç
            if (!double.IsInfinity(width))
            {
                child.Measure(new Size(width / _scale, double.PositiveInfinity));
                desired = child.DesiredSize;
            }
        }

        return new Size(
            double.IsInfinity(constraint.Width) ? desired.Width * _scale : constraint.Width,
            double.IsInfinity(constraint.Height) ? desired.Height * _scale : Math.Min(constraint.Height, desired.Height * _scale));
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        var child = Child;
        if (child is null) return arrangeSize;

        var width = arrangeSize.Width / _scale;
        var height = Math.Max(arrangeSize.Height / _scale, child.DesiredSize.Height);
        child.RenderTransform = _scale < 1 ? new ScaleTransform(_scale, _scale) : Transform.Identity;
        child.Arrange(new Rect(0, 0, width, height));
        return arrangeSize;
    }
}
