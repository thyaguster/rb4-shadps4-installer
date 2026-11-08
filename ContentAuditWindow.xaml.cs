using System.Windows;
using System.Windows.Input;
using RB4ShadPS4Installer.Models;

namespace RB4ShadPS4Installer;

public partial class ContentAuditWindow : Window
{
    private readonly ContentAuditReport _report;

    public ContentAuditWindow(ContentAuditReport report)
    {
        InitializeComponent();
        _report = report;
        HeadingText.Text = report.Passed ? "Content library looks healthy" : "Content library needs attention";
        SummaryText.Text = report.Summary + " Nothing was changed.";
        FindingsList.ItemsSource = report.Findings;
    }

    private void DragHeader(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void CopyReport(object sender, RoutedEventArgs e) => Clipboard.SetText(_report.ToPlainText());
    private void CloseDialog(object sender, RoutedEventArgs e) => Close();
}
