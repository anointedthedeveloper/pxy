using System.Windows;
using System.Windows.Input;

namespace CbtExam.Desktop.Views;

public partial class BroadcastDialog : Window
{
    public string BroadcastMessage { get; private set; } = string.Empty;

    public BroadcastDialog()
    {
        InitializeComponent();
        Loaded += BroadcastDialog_Loaded;
        MessageTextBox.Focus();
    }

    private void BroadcastDialog_Loaded(object sender, RoutedEventArgs e)
    {
        var app = Application.Current as App;
        app?.ApplyTitleBarToWindow(this);
    }

    private void MessageTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
    }

    private void SendButton_Click(object sender, RoutedEventArgs e)
    {
        var msg = MessageTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(msg))
        {
            MessageBox.Show("Please enter a message to broadcast.", "Broadcast Message", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        BroadcastMessage = msg;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
        base.OnMouseDown(e);
    }
}
