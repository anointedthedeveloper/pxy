using System.Windows.Controls;
using System.Windows.Input;

namespace CbtExam.Desktop.Views;

public partial class ExamsView : UserControl
{
    public ExamsView() => InitializeComponent();

    private void YearChip_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.FrameworkElement fe && fe.DataContext is ViewModels.YearToggle toggle)
        {
            toggle.IsSelected = !toggle.IsSelected;
        }
    }
}
