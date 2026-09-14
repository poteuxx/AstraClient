using System.Windows.Controls;
using System.Windows.Input;
using AstraClient.ViewModels;

namespace AstraClient.Views;

public partial class BrowseView : UserControl
{
    public BrowseView() => InitializeComponent();

    // Set the SelectedType when a type tab is checked (Tag contains the type string).
    private void TypeTab_Checked(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string typeStr && DataContext is BrowseViewModel vm)
            vm.SelectedType = typeStr;
    }

    // Trigger search on Enter key in the search box.
    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is BrowseViewModel vm)
            vm.SearchCommand.Execute(null);
    }
}
