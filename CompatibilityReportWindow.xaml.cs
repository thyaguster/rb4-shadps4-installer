using System.Windows;
using RB4ShadPS4Installer.Models;

namespace RB4ShadPS4Installer;

public partial class CompatibilityReportWindow : Window
{
    public CompatibilityReportWindow(CompatibilityReport report)
    {
        InitializeComponent();
        SummaryText.Text = report.Summary;
        ItemsList.ItemsSource = report.Items;
    }

    private void CloseWindow(object sender, RoutedEventArgs e) => Close();
}
