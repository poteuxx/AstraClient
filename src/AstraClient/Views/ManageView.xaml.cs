using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using AstraClient.ViewModels;

namespace AstraClient.Views;

public partial class ManageView : UserControl
{
    public ManageView() => InitializeComponent();

    // Memory preset quick buttons (Tag = MB as string, e.g. "4096").
    private void MemoryPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn
            && int.TryParse(btn.Tag?.ToString(), out int mb)
            && DataContext is ManageViewModel vm)
        {
            vm.InstanceMemory = mb;
        }
    }
}
