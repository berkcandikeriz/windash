using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using PcDashboard.Core;

namespace PcDashboard.Controls;

/// <summary>
/// Kartları ızgara üzerinde taşıma ve boyutlandırma (Düzenle modu). Ana pencerede ve masaüstü panelinde ortaktır.
///  - Kartı tutup sürükle: taşı (hücreye oturur).
///  - Sağ alt köşedeki tutamağı sürükle: boyutlandır.
/// Kartın <see cref="FrameworkElement.Tag"/> değeri o kartın <see cref="WidgetPlacement"/>'ıdır; değişiklikler ona yazılır.
/// </summary>
public sealed class LayoutEditor
{
    private readonly Grid _grid;
    private readonly IReadOnlyList<FrameworkElement> _cards;
    private readonly Action _changed;
    private readonly List<CardAdorner> _adorners = new();
    private readonly Dictionary<FrameworkElement, (int Col, int Row, int ColSpan, int RowSpan)> _snapshot = new();

    public LayoutEditor(Grid grid, IReadOnlyList<FrameworkElement> cards, Action changed)
    {
        _grid = grid;
        _cards = cards;
        _changed = changed;
    }

    public int Columns => Math.Max(1, _grid.ColumnDefinitions.Count);
    public int Rows => Math.Max(1, _grid.RowDefinitions.Count);

    public void Begin()
    {
        foreach (var card in _cards)
        {
            _snapshot[card] = (Grid.GetColumn(card), Grid.GetRow(card), Grid.GetColumnSpan(card), Grid.GetRowSpan(card));
            var layer = AdornerLayer.GetAdornerLayer(card);
            if (layer is null) continue;
            var adorner = new CardAdorner(card, this);
            layer.Add(adorner);
            _adorners.Add(adorner);
        }
    }

    /// <summary>Düzenlemeyi bitirir. <paramref name="keep"/> false ise kartlar başlangıçtaki yerlerine döner.</summary>
    public void End(bool keep)
    {
        foreach (var adorner in _adorners)
            AdornerLayer.GetAdornerLayer(adorner.AdornedElement)?.Remove(adorner);
        _adorners.Clear();

        if (!keep)
        {
            foreach (var (card, s) in _snapshot)
            {
                Grid.SetColumn(card, s.Col);
                Grid.SetRow(card, s.Row);
                Grid.SetColumnSpan(card, s.ColSpan);
                Grid.SetRowSpan(card, s.RowSpan);
            }
        }

        _snapshot.Clear();
        WriteBack();
    }

    /// <summary>Kartların ızgaradaki yerlerini WidgetPlacement nesnelerine yazar.</summary>
    public void WriteBack()
    {
        foreach (var card in _cards)
        {
            if (card.Tag is not WidgetPlacement p) continue;
            p.Column = Grid.GetColumn(card);
            p.Row = Grid.GetRow(card);
            p.ColumnSpan = Grid.GetColumnSpan(card);
            p.RowSpan = Grid.GetRowSpan(card);
        }
    }

    internal Point Position(MouseEventArgs e) => e.GetPosition(_grid);

    internal Size CellSize => new(_grid.ActualWidth / Columns, _grid.ActualHeight / Rows);

    internal void Move(FrameworkElement card, int col, int row)
    {
        var colSpan = Grid.GetColumnSpan(card);
        var rowSpan = Grid.GetRowSpan(card);
        Grid.SetColumn(card, Math.Clamp(col, 0, Math.Max(0, Columns - colSpan)));
        Grid.SetRow(card, Math.Clamp(row, 0, Math.Max(0, Rows - rowSpan)));
    }

    internal void Resize(FrameworkElement card, int colSpan, int rowSpan)
    {
        var col = Grid.GetColumn(card);
        var row = Grid.GetRow(card);
        Grid.SetColumnSpan(card, Math.Clamp(colSpan, 1, Columns - col));
        Grid.SetRowSpan(card, Math.Clamp(rowSpan, 1, Rows - row));
    }

    internal void Committed()
    {
        WriteBack();
        _changed();
    }

    /// <summary>Düzenleme sırasında kartın üstüne çizilen çerçeve, taşıma alanı ve boyut tutamağı.</summary>
    private sealed class CardAdorner : Adorner
    {
        private const double GripSize = 26;

        private readonly LayoutEditor _editor;
        private readonly FrameworkElement _card;
        private readonly Brush _accent;
        private readonly Pen _border;
        private bool _resizing;
        private bool _dragging;
        private Point _start;
        private (int Col, int Row, int ColSpan, int RowSpan) _origin;

        public CardAdorner(FrameworkElement card, LayoutEditor editor) : base(card)
        {
            _card = card;
            _editor = editor;
            _accent = (card.TryFindResource("AccentBrush") as Brush) ?? Brushes.CornflowerBlue;
            _border = new Pen(_accent, 2) { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) };
            _border.Freeze();
            Cursor = Cursors.SizeAll;
            IsHitTestVisible = true;
        }

        private Rect Bounds => new(0, 0, Math.Max(0, _card.ActualWidth), Math.Max(0, _card.ActualHeight));

        private Rect Grip
        {
            get
            {
                var b = Bounds;
                return new Rect(b.Right - GripSize - 6, b.Bottom - GripSize - 6, GripSize, GripSize);
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            var b = Bounds;
            // Kartın içeriğine tıklanmasın diye tüm kartı kaplayan hafif katman
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0x30, 0x0A, 0x0C, 0x14)), _border, b, 28, 28);

            var g = Grip;
            dc.DrawEllipse(_accent, null, new Point(g.X + g.Width / 2, g.Y + g.Height / 2), g.Width / 2, g.Height / 2);
            var arrow = new Pen(new SolidColorBrush(Color.FromRgb(0x0E, 0x10, 0x13)), 2);
            dc.DrawLine(arrow, new Point(g.X + 8, g.Bottom - 8), new Point(g.Right - 8, g.Y + 8));
            dc.DrawLine(arrow, new Point(g.Right - 8, g.Y + 8), new Point(g.Right - 8, g.Y + 14));
            dc.DrawLine(arrow, new Point(g.Right - 8, g.Y + 8), new Point(g.Right - 14, g.Y + 8));
            dc.DrawLine(arrow, new Point(g.X + 8, g.Bottom - 8), new Point(g.X + 8, g.Bottom - 14));
            dc.DrawLine(arrow, new Point(g.X + 8, g.Bottom - 8), new Point(g.X + 14, g.Bottom - 8));

            if (_card.Tag is WidgetPlacement p)
            {
                var text = new FormattedText($"{Title(p.Type)} · {p.ColumnSpan}×{p.RowSpan}",
                    System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI Semibold"), 13, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                var pill = new Rect(b.X + 12, b.Y + 12, text.Width + 20, text.Height + 8);
                dc.DrawRoundedRectangle(_accent, null, pill, pill.Height / 2, pill.Height / 2);
                text.SetForegroundBrush(new SolidColorBrush(Color.FromRgb(0x0E, 0x10, 0x13)));
                dc.DrawText(text, new Point(pill.X + 10, pill.Y + 4));
            }
        }

        private static string Title(string type) => type.Trim().ToLowerInvariant() switch
        {
            "clock" => "Saat",
            "weather" => "Hava durumu",
            "media" => "Şu an çalıyor",
            "news" => "Haberler",
            "apps" => "Uygulamalar",
            "devices" => "Cihazlar",
            "controls" => "Kontrol merkezi",
            "system" => "Sistem",
            "network" => "Ağ",
            "calendar" => "Takvim",
            "window" => "Pencere",
            _ => type,
        };

        protected override HitTestResult? HitTestCore(PointHitTestParameters hit) =>
            Bounds.Contains(hit.HitPoint) ? new PointHitTestResult(this, hit.HitPoint) : null;

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging) Cursor = Grip.Contains(e.GetPosition(this)) ? Cursors.SizeNWSE : Cursors.SizeAll;
            if (!_dragging || e.LeftButton != MouseButtonState.Pressed) return;

            var now = _editor.Position(e);
            var cell = _editor.CellSize;
            if (cell.Width <= 0 || cell.Height <= 0) return;
            var dc = (int)Math.Round((now.X - _start.X) / cell.Width);
            var dr = (int)Math.Round((now.Y - _start.Y) / cell.Height);

            if (_resizing) _editor.Resize(_card, _origin.ColSpan + dc, _origin.RowSpan + dr);
            else _editor.Move(_card, _origin.Col + dc, _origin.Row + dr);

            _editor.WriteBack();
            InvalidateVisual();
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            _resizing = Grip.Contains(e.GetPosition(this));
            _dragging = true;
            _start = _editor.Position(e);
            _origin = (Grid.GetColumn(_card), Grid.GetRow(_card), Grid.GetColumnSpan(_card), Grid.GetRowSpan(_card));
            Panel.SetZIndex(_card, 100);
            CaptureMouse();
            e.Handled = true;
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (!_dragging) return;
            _dragging = false;
            _resizing = false;
            Panel.SetZIndex(_card, 0);
            ReleaseMouseCapture();
            e.Handled = true;
            _editor.Committed();
        }
    }
}

/// <summary>Düzenle modunun üstte duran araç çubuğu: Varsayılana dön · İptal · Bitti. Kartların üstünde kalması için ayrı bir açılır pencerede.</summary>
public static class EditToolbar
{
    public static System.Windows.Controls.Primitives.Popup Show(FrameworkElement target, string hint,
        Action done, Action cancel, Action reset)
    {
        var popup = new System.Windows.Controls.Primitives.Popup
        {
            PlacementTarget = target,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Custom,
            AllowsTransparency = true,
            StaysOpen = true,
        };
        popup.CustomPopupPlacementCallback = (popupSize, targetSize, _) => new[]
        {
            new System.Windows.Controls.Primitives.CustomPopupPlacement(
                new Point((targetSize.Width - popupSize.Width) / 2, 16),
                System.Windows.Controls.Primitives.PopupPrimaryAxis.None),
        };

        Button Make(string text, Action action, bool primary = false)
        {
            var button = new Button
            {
                Content = text,
                Style = (Style)target.FindResource("ChipButton"),
                FontWeight = primary ? FontWeights.SemiBold : FontWeights.Normal,
            };
            if (primary)
            {
                button.Background = (Brush)target.FindResource("AccentBrush");
                button.Foreground = (Brush)target.FindResource("OnAccentBrush");
            }

            button.Click += (_, _) => action();
            return button;
        }

        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new TextBlock
        {
            Text = hint,
            FontSize = 13,
            Foreground = (Brush)target.FindResource("MutedBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 8, 0),
            MaxWidth = 260,
            TextWrapping = TextWrapping.Wrap,
        });
        row.Children.Add(Make("Varsayılana dön", reset));
        row.Children.Add(Make("İptal", cancel));
        row.Children.Add(Make("Bitti", done, primary: true));

        var border = new Border
        {
            Background = (Brush)target.FindResource("PopupBrush"),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(10, 8, 10, 8),
            Child = row,
        };
        TextElement.SetFontFamily(border, (FontFamily)target.FindResource("UiFont"));
        TextElement.SetForeground(border, (Brush)target.FindResource("TextBrush"));

        popup.Child = border;
        popup.IsOpen = true;
        return popup;
    }
}
