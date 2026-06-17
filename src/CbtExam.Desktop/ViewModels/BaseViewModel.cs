using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace CbtExam.Desktop.ViewModels;

public abstract class BaseViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; set => Set(ref _isBusy, value); }

    private string _busyMessage = "Loading...";
    public string BusyMessage { get => _busyMessage; set => Set(ref _busyMessage, value); }

    /// <summary>
    /// Clipboard.SetText can throw CLIPBRD_E_CANT_OPEN if another app holds
    /// the clipboard. Retry up to 5 times with a short delay.
    /// </summary>
    protected static void ClipboardSetTextSafe(string text)
    {
        for (int i = 0; i < 5; i++)
        {
            try
            {
                System.Windows.Clipboard.SetText(text);
                return;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                System.Threading.Thread.Sleep(20);
            }
        }
    }
}

public class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? _) => canExecute?.Invoke() ?? true;
    public void Execute(object? _) => execute();
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public class RelayCommand<T>(Action<T?> execute, Func<T?, bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? p) => canExecute?.Invoke(p is T t ? t : default) ?? true;
    public void Execute(object? p) => execute(p is T t ? t : default);
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
