using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PcDashboard.Widgets.Devices;

/// <summary>Masaüstü paneli için sade cihaz listesi (DevicesViewModel ile; seçim ve tanılama ana kartla aynı).</summary>
public partial class DevicesCompactWidget : UserControl
{
    private readonly DeviceRowActions _actions;

    public DevicesCompactWidget()
    {
        InitializeComponent();
        _actions = new DeviceRowActions(this);
    }

    private void Row_Click(object sender, RoutedEventArgs e) => _actions.OpenPicker(sender);

    private void Row_RightClick(object sender, MouseButtonEventArgs e) => _actions.ShowDiagnosticsMenu(sender, e);
}
