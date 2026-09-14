using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using AstraClient.ViewModels;

namespace AstraClient.Views;

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    // Open a native file picker to select javaw.exe / java.
    private void BrowseJava_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title  = "Select Java Executable",
            Filter = "Java Executable|javaw.exe;java.exe;java|All Files|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == true && DataContext is SettingsViewModel vm)
            vm.JavaOverridePath = dialog.FileName;
    }
}
