using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Diagnostics;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using PcDashboard.Controls;
using PcDashboard.Core;
using PcDashboard.Services;

namespace PcDashboard.Widgets.Devices;

public partial class DevicesWidget : UserControl
{
    private Popup? _popup;

    public DevicesWidget() => InitializeComponent();

    /// <summary>Satıra tıklanınca o yuvanın aygıt listesini cam görünümlü bir açılır pencerede gösterir.</summary>
    private void Row_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement row || row.DataContext is not DeviceSlotViewModel slot) return;
        if (DataContext is not DevicesViewModel vm) return;

        _popup?.SetCurrentValue(Popup.IsOpenProperty, false);

        var options = vm.GetOptions(slot);
        var list = new StackPanel();

        list.Children.Add(new TextBlock
        {
            Text = slot.Kind is "mic" or "output" ? $"{slot.Title} · Windows varsayılanı" : slot.Title,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("MutedBrush"),
            Margin = new Thickness(12, 6, 12, 6),
        });

        if (options.Count == 0 || (options.Count == 1 && options[0].IsAuto))
        {
            list.Children.Add(new TextBlock
            {
                Text = "Uygun aygıt bulunamadı",
                FontSize = 14,
                Foreground = (Brush)FindResource("MutedBrush"),
                Margin = new Thickness(12, 4, 12, 8),
            });
        }

        foreach (var option in options)
        {
            var button = new Button
            {
                Style = (Style)FindResource("MenuRowButton"),
                Content = BuildOption(option),
            };
            button.Click += (_, _) =>
            {
                if (_popup is not null) _popup.IsOpen = false;
                vm.Select(slot, option);
            };
            list.Children.Add(button);
        }

        var border = new Border
        {
            Background = (Brush)FindResource("PopupBrush"),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(6),
            MinWidth = Math.Max(260, row.ActualWidth),
            MaxWidth = 460,
            Child = new ScrollViewer
            {
                Content = list,
                MaxHeight = 420,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            },
        };
        // Açılır pencere görsel ağacın dışında; yazı tipini ve rengi kendisi taşımalı
        TextElement.SetFontFamily(border, (FontFamily)FindResource("UiFont"));
        TextElement.SetForeground(border, (Brush)FindResource("TextBrush"));

        _popup = new Popup
        {
            Child = border,
            PlacementTarget = row,
            Placement = PlacementMode.Bottom,
            VerticalOffset = 6,
            AllowsTransparency = true,
            StaysOpen = false,
            PopupAnimation = PopupAnimation.Fade,
        };
        _popup.IsOpen = true;
    }

    /// <summary>Sağ tık: klavye / mouse satırında Keychron tanılaması.</summary>
    private void Row_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement row || row.DataContext is not DeviceSlotViewModel slot || !slot.IsHid) return;
        e.Handled = true;

        var item = new MenuItem { Header = "Aygıt tanılaması (15 sn) → günlüğü aç" };
        item.Click += async (_, _) =>
        {
            item.IsEnabled = false;
            var previous = slot.Subtitle;
            slot.Subtitle = "Tanılama çalışıyor… klavyede birkaç tuşa bas";
            slot.SetLine2(battery: false, meter: false);
            if (slot.Hid is { } hid)
                await HidDiagnostics.RunAsync(hid.VendorId, hid.Name.Split(' ')[0], TimeSpan.FromSeconds(15));
            else
                await HidDiagnostics.RunKeychronAsync(TimeSpan.FromSeconds(15));
            slot.Subtitle = previous;
            try
            {
                Process.Start(new ProcessStartInfo(Log.FilePath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Error("Günlük açılamadı", ex);
            }
        };

        var menu = new ContextMenu { PlacementTarget = row };
        menu.Items.Add(item);
        menu.IsOpen = true;
    }

    private FrameworkElement BuildOption(SlotOption option)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        if (option.IsSelected)
        {
            grid.Children.Add(new Icon
            {
                Data = (Geometry)FindResource("IconCheck"),
                Width = 16,
                Height = 16,
                StrokeThickness = 2.2,
                Foreground = (Brush)FindResource("AccentBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
            });
        }

        var texts = new StackPanel();
        texts.Children.Add(new TextBlock
        {
            Text = option.Label,
            FontSize = 15,
            FontWeight = option.IsSelected ? FontWeights.SemiBold : FontWeights.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        if (!string.IsNullOrWhiteSpace(option.Detail))
        {
            texts.Children.Add(new TextBlock
            {
                Text = option.Detail,
                FontSize = 12,
                Foreground = (Brush)FindResource("MutedBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 2, 0, 0),
            });
        }

        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);
        return grid;
    }
}
