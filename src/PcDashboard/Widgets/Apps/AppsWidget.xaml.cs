using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using PcDashboard.Services;

namespace PcDashboard.Widgets.Apps;

public partial class AppsWidget : UserControl
{
    private Popup? _popup;

    public AppsWidget() => InitializeComponent();

    private AppsViewModel? Vm => DataContext as AppsViewModel;

    // ---------- Sağ tık: sırala / kaldır / konum seç ----------

    private void Tile_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not AppTile tile || Vm is not { } vm) return;
        e.Handled = true;

        var menu = new ContextMenu { PlacementTarget = element };
        menu.Items.Add(Item("Sola taşı", () => vm.Move(tile, -1), vm.Tiles.IndexOf(tile) > 0));
        menu.Items.Add(Item("Sağa taşı", () => vm.Move(tile, +1), vm.Tiles.IndexOf(tile) < vm.Tiles.Count - 1));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Konumunu seç…", () =>
        {
            var path = PickFile();
            if (path is not null) vm.Relocate(tile, path);
        }));
        menu.Items.Add(Item("Kaldır", () => vm.Remove(tile)));
        menu.IsOpen = true;
    }

    private static MenuItem Item(string header, Action action, bool enabled = true)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled };
        item.Click += (_, _) => action();
        return item;
    }

    private static string? PickFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Uygulama seç",
            Filter = "Uygulamalar ve kısayollar (*.exe;*.lnk;*.url)|*.exe;*.lnk;*.url|Tüm dosyalar (*.*)|*.*",
            DereferenceLinks = false,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    // ---------- Ekle: aranabilir uygulama listesi ----------

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || sender is not FrameworkElement anchor) return;
        if (_popup is not null) _popup.IsOpen = false;

        var search = new TextBox
        {
            FontSize = 15,
            Padding = new Thickness(10, 8, 10, 8),
            Background = new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)),
            Foreground = Brushes.White,
            CaretBrush = Brushes.White,
            BorderThickness = new Thickness(0),
            Margin = new Thickness(6, 6, 6, 6),
        };

        var results = new StackPanel();
        var footer = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 6, 6, 2) };
        footer.Children.Add(FooterButton("Gözat… (.exe)", () =>
        {
            if (_popup is not null) _popup.IsOpen = false;
            var path = PickFile();
            if (path is not null) vm.AddFile(path);
        }));
        footer.Children.Add(FooterButton("Varsayılanlara dön", () =>
        {
            if (_popup is not null) _popup.IsOpen = false;
            vm.ResetToDefaults();
        }));

        var panel = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(search, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        panel.Children.Add(search);
        panel.Children.Add(footer);
        panel.Children.Add(new ScrollViewer
        {
            Content = results,
            Height = 340,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        });

        var border = new Border
        {
            Background = (Brush)FindResource("PopupBrush"),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(6),
            Width = 380,
            Child = panel,
        };
        TextElement.SetFontFamily(border, (FontFamily)FindResource("UiFont"));
        TextElement.SetForeground(border, (Brush)FindResource("TextBrush"));

        _popup = new Popup
        {
            Child = border,
            PlacementTarget = anchor,
            Placement = PlacementMode.Bottom,
            VerticalOffset = 6,
            AllowsTransparency = true,
            StaysOpen = false,
        };

        async Task Fill()
        {
            var list = await vm.SearchAsync(search.Text);
            results.Children.Clear();
            if (list.Count == 0)
            {
                results.Children.Add(new TextBlock
                {
                    Text = "Sonuç yok",
                    FontSize = 14,
                    Foreground = (Brush)FindResource("MutedBrush"),
                    Margin = new Thickness(12, 8, 12, 8),
                });
            }

            foreach (var app in list)
            {
                var row = new Button { Style = (Style)FindResource("MenuRowButton"), Content = AppRow(app) };
                row.Click += (_, _) =>
                {
                    if (_popup is not null) _popup.IsOpen = false;
                    vm.Add(app);
                };
                results.Children.Add(row);
            }
        }

        search.TextChanged += async (_, _) => await Fill();
        _popup.IsOpen = true;
        search.Focus();
        Keyboard.Focus(search);
        await Fill();
    }

    private FrameworkElement AppRow(InstalledApp app)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new Image
        {
            Source = AppCatalog.GetIcon(app.Target, 48),
            Width = 24,
            Height = 24,
            HorizontalAlignment = HorizontalAlignment.Left,
        });
        var text = new TextBlock
        {
            Text = app.Name,
            FontSize = 15,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        return grid;
    }

    private Button FooterButton(string text, Action action)
    {
        var button = new Button
        {
            Style = (Style)FindResource("ChipButton"),
            Content = text,
            Margin = new Thickness(0, 0, 8, 0),
        };
        button.Click += (_, _) => action();
        return button;
    }
}
