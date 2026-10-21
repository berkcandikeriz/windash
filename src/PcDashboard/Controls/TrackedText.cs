using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace PcDashboard.Controls;

/// <summary>
/// Harf aralığı (letter-spacing) ayarlanabilen tek satırlık metin. WPF'in TextBlock'u harf aralığını
/// desteklemediği için taslaktaki büyük harfli başlıklar (0.08em) ve büyük saat (-0.04em) bununla çizilir.
/// </summary>
public class TrackedText : FrameworkElement
{
    private static readonly CultureInfo Tr = new("tr-TR");

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(TrackedText),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Harf aralığı, yazı boyutunun katı olarak (CSS'teki em).</summary>
    public static readonly DependencyProperty TrackingProperty = DependencyProperty.Register(
        nameof(Tracking), typeof(double), typeof(TrackedText),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty UppercaseProperty = DependencyProperty.Register(
        nameof(Uppercase), typeof(bool), typeof(TrackedText),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner(typeof(TrackedText),
        new FrameworkPropertyMetadata(SystemFonts.MessageFontFamily,
            FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontSizeProperty = TextElement.FontSizeProperty.AddOwner(typeof(TrackedText),
        new FrameworkPropertyMetadata(SystemFonts.MessageFontSize,
            FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontWeightProperty = TextElement.FontWeightProperty.AddOwner(typeof(TrackedText),
        new FrameworkPropertyMetadata(FontWeights.Normal,
            FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(typeof(TrackedText),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    private readonly List<(FormattedText Glyph, double X)> _glyphs = new();
    private double _baseline;

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public double Tracking
    {
        get => (double)GetValue(TrackingProperty);
        set => SetValue(TrackingProperty, value);
    }

    public bool Uppercase
    {
        get => (bool)GetValue(UppercaseProperty);
        set => SetValue(UppercaseProperty, value);
    }

    public FontFamily FontFamily
    {
        get => (FontFamily)GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public FontWeight FontWeight
    {
        get => (FontWeight)GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>Üstten taban çizgisine uzaklık (BaselinePanel hizalaması için).</summary>
    public double Baseline
    {
        get
        {
            if (!IsMeasureValid) Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return _baseline;
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _glyphs.Clear();
        var text = Text ?? "";
        if (Uppercase) text = text.ToUpper(Tr);

        var typeface = new Typeface(FontFamily, FontStyles.Normal, FontWeight, FontStretches.Normal);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var spacing = Tracking * FontSize;

        // Satır yüksekliği ve taban çizgisi tüm metne göre (harf harf değişmesin)
        var line = Create(string.IsNullOrEmpty(text) ? " " : text, typeface, dpi);
        _baseline = line.Baseline;

        var x = 0.0;
        var info = new StringInfo(text);
        for (var i = 0; i < info.LengthInTextElements; i++)
        {
            var glyph = Create(info.SubstringByTextElements(i, 1), typeface, dpi);
            _glyphs.Add((glyph, x));
            x += glyph.WidthIncludingTrailingWhitespace + spacing;
        }

        // CSS gibi son harften sonraki aralığı da genişliğe kat (başlıklarda fark edilmez)
        var width = Math.Max(0, x);
        return new Size(width, line.Height);
    }

    protected override void OnRender(DrawingContext dc)
    {
        foreach (var (glyph, x) in _glyphs)
        {
            glyph.SetForegroundBrush(Foreground);
            dc.DrawText(glyph, new Point(x, 0));
        }
    }

    private FormattedText Create(string s, Typeface typeface, double dpi) =>
        new(s, Tr, FlowDirection.LeftToRight, typeface, FontSize, Foreground, dpi);
}
