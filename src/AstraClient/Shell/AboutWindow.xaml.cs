using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Input;

namespace AstraClient.Shell;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        PopulateInfo();
    }

    private void PopulateInfo()
    {
        var asm     = Assembly.GetExecutingAssembly();
        var version = asm.GetName().Version ?? new Version(1, 31, 0, 0);
        var build   = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? $"{version.Major}.{version.Minor}.{version.Build}";

        // Trim git hash suffix if present (e.g. "1.31.0+abc1234" -> build hash part).
        string displayVersion = $"v{version.Major}.{version.Minor}.{version.Build}";
        string buildStr;

        if (build.Contains('+'))
        {
            var parts   = build.Split('+', 2);
            buildStr = parts[1].Length >= 7 ? $"#{parts[1][..7]}" : $"#{parts[1]}";
        }
        else
        {
            buildStr = $"build {version.Major}.{version.Minor}.{version.Build}";
        }

        VersionLabel.Text   = displayVersion;
        BuildLabel.Text     = buildStr;
        BuildDateLabel.Text = $"Released {DateTime.Now:MMMM yyyy}";
        RuntimeLabel.Text   = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
        CopyrightLabel.Text = $"© {DateTime.Now.Year} Astra Client Contributors";
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => DragMove();

    private void CloseButton_Click(object sender, RoutedEventArgs e)
        => Close();

    private void ReleaseHistory_Click(object sender, RoutedEventArgs e)
    {
        var history = new ReleaseHistoryWindow { Owner = this };
        history.ShowDialog();
    }

    private void ModrinthLink_Click(object sender, RoutedEventArgs e)
        => OpenUrl("https://modrinth.com");

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { }
    }
}
