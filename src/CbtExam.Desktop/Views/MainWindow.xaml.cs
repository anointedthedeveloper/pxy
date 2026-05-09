using CbtExam.Desktop.ViewModels;
using System.Windows;

namespace CbtExam.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Must catch here — async void exceptions bypass the global handler
        try
        {
            if (DataContext is MainViewModel vm)
                await vm.InitAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to start server:\n\n{ex.Message}\n\n{ex.InnerException?.Message}",
                "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
