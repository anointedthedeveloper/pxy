using System.Windows;
using System.Windows.Threading;

namespace CbtExam.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Catch any unhandled exception on the UI thread — show message instead of silent crash
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // Catch unhandled exceptions from background Task threads
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // Catch anything else
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"An unexpected error occurred:\n\n{e.Exception.Message}\n\n{e.Exception.InnerException?.Message}",
            "CBT Exam — Error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true; // prevent app from closing
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved(); // prevent process termination
        Dispatcher.Invoke(() =>
            MessageBox.Show(
                $"Background error:\n\n{e.Exception.InnerException?.Message ?? e.Exception.Message}",
                "CBT Exam — Error", MessageBoxButton.OK, MessageBoxImage.Warning));
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        MessageBox.Show(
            $"Fatal error:\n\n{ex?.Message}",
            "CBT Exam — Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
