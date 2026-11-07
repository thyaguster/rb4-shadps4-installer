using System.Windows;
using System.Windows.Input;

namespace RB4ShadPS4Installer;

public partial class ConfirmationWindow : Window
{
    public ConfirmationWindow(string title, string heading, string message, string action)
    {
        InitializeComponent();
        Title = title;
        WindowTitleText.Text = title;
        HeadingText.Text = heading;
        MessageText.Text = message;
        ConfirmButton.Content = action;
    }

    private void DragHeader(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void ConfirmAction(object sender, RoutedEventArgs e) => DialogResult = true;
    private void CancelAction(object sender, RoutedEventArgs e) => DialogResult = false;
}
