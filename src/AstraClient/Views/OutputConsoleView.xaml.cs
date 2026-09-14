using System.Windows;
using System.Windows.Controls;

namespace AstraClient.Views;

public partial class OutputConsoleView : UserControl
{
    public OutputConsoleView()
    {
        InitializeComponent();
        OutputTextBox.TextChanged += OutputTextBox_TextChanged;
    }

    private void OutputTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        OutputTextBox.ScrollToEnd();
    }
}
