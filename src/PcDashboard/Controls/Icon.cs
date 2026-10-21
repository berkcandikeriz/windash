using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PcDashboard.Controls;

/// <summary>
/// 24x24 koordinat sisteminde çizilmiş çizgi ikon. Rengi Foreground'dan alır,
/// boyutu Width/Height ile ölçeklenir (görünümü Themes/Glass.xaml içinde).
/// </summary>
public class Icon : Control
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(Geometry), typeof(Icon), new PropertyMetadata(null));

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness), typeof(double), typeof(Icon), new PropertyMetadata(1.8));

    public Geometry? Data
    {
        get => (Geometry?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }
}
