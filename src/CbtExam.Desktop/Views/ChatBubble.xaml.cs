using CbtExam.Desktop.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CbtExam.Desktop.Views;

public partial class ChatBubble : UserControl
{
    // ── Drag state ────────────────────────────────────────────────────────────
    private bool  _isDragging;
    private Point _dragStart;
    private Point _anchorStart;
    private bool  _wasDragged;

    public ChatBubble()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    // ── Initialise ────────────────────────────────────────────────────────────
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PositionAnchor();

        if (Window.GetWindow(this) is Window w)
        {
            w.SizeChanged += (_, _) => PositionAnchor();

            // T+C global hotkey — restores hidden FAB and opens chat
            w.KeyDown += Window_KeyDown;
        }

        // Auto-scroll when messages change
        if (DataContext is ChatViewModel vm)
        {
            vm.Messages.CollectionChanged  += (_, _) => ScrollToBottom();
            vm.PropertyChanged += (_, e2) =>
            {
                if (e2.PropertyName == nameof(ChatViewModel.IsTyping))
                    ScrollToBottom();
                if (e2.PropertyName == nameof(ChatViewModel.IsFabVisible))
                    PositionAnchor();
            };
        }
    }

    // ── T+C hotkey ────────────────────────────────────────────────────────────
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not ChatViewModel vm) return;

        // Ignore if user is typing in an input field
        if (e.OriginalSource is TextBox or PasswordBox) return;

        bool tDown = Keyboard.IsKeyDown(Key.T);
        bool cDown = e.Key == Key.C && tDown
                  || e.Key == Key.T && Keyboard.IsKeyDown(Key.C);

        if (tDown && e.Key == Key.C || e.Key == Key.T && Keyboard.IsKeyDown(Key.C))
        {
            vm.IsFabVisible = true;
            vm.IsOpen       = true;
            e.Handled       = true;
            Dispatcher.BeginInvoke(() => InputBox.Focus(),
                System.Windows.Threading.DispatcherPriority.Input);
        }
    }

    // ── Position anchor on canvas ─────────────────────────────────────────────
    // The anchor panel is positioned so the FAB sits at (BubbleRight, BubbleBottom)
    // from the bottom-right corner.  The popup naturally stacks ABOVE the FAB
    // because the StackPanel inside AnchorPanel is top-to-bottom: [popup] [FAB].
    private void PositionAnchor()
    {
        if (DataContext is not ChatViewModel vm) return;

        var canvas = RootCanvas;
        var panel  = AnchorPanel;

        if (canvas.ActualWidth < 1 || canvas.ActualHeight < 1) return;

        panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var w = panel.DesiredSize.Width;
        var h = panel.DesiredSize.Height;

        // Bottom-right anchor: FAB bottom-right edge sits vm.BubbleRight / vm.BubbleBottom
        // from the canvas bottom-right corner.
        double left = canvas.ActualWidth  - vm.BubbleRight  - w;
        double top  = canvas.ActualHeight - vm.BubbleBottom - h;

        left = Math.Max(0, Math.Min(canvas.ActualWidth  - w, left));
        top  = Math.Max(0, Math.Min(canvas.ActualHeight - h, top));

        Canvas.SetLeft(panel, left);
        Canvas.SetTop(panel,  top);
    }

    // ── Drag: FAB ─────────────────────────────────────────────────────────────
    private void Fab_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        StartDrag(e);
        e.Handled = true;
    }

    // ── Drag: header bar ──────────────────────────────────────────────────────
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        StartDrag(e);
        e.Handled = true;
    }

    private void StartDrag(MouseButtonEventArgs e)
    {
        _isDragging  = true;
        _wasDragged  = false;
        _dragStart   = e.GetPosition(RootCanvas);
        _anchorStart = new Point(Canvas.GetLeft(AnchorPanel), Canvas.GetTop(AnchorPanel));

        Mouse.Capture(AnchorPanel);
        AnchorPanel.MouseMove         += Panel_MouseMove;
        AnchorPanel.MouseLeftButtonUp += Panel_MouseLeftButtonUp;
    }

    private void Panel_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;

        var pos = e.GetPosition(RootCanvas);
        var dx  = pos.X - _dragStart.X;
        var dy  = pos.Y - _dragStart.Y;

        if (Math.Abs(dx) > 3 || Math.Abs(dy) > 3)
            _wasDragged = true;

        var canvas = RootCanvas;
        var panel  = AnchorPanel;

        double newLeft = Math.Max(0, Math.Min(canvas.ActualWidth  - panel.ActualWidth,  _anchorStart.X + dx));
        double newTop  = Math.Max(0, Math.Min(canvas.ActualHeight - panel.ActualHeight, _anchorStart.Y + dy));

        Canvas.SetLeft(panel, newLeft);
        Canvas.SetTop(panel,  newTop);
    }

    private void Panel_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;

        Mouse.Capture(null);
        AnchorPanel.MouseMove         -= Panel_MouseMove;
        AnchorPanel.MouseLeftButtonUp -= Panel_MouseLeftButtonUp;

        if (DataContext is ChatViewModel vm && _wasDragged)
        {
            var canvas = RootCanvas;
            var panel  = AnchorPanel;

            // Convert canvas position back to right/bottom offset
            vm.BubbleRight  = canvas.ActualWidth  - Canvas.GetLeft(panel) - panel.ActualWidth;
            vm.BubbleBottom = canvas.ActualHeight - Canvas.GetTop(panel)  - panel.ActualHeight;
        }

        e.Handled = true;
    }

    // ── Enter to send ─────────────────────────────────────────────────────────
    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is ChatViewModel vm)
        {
            vm.SendCommand.Execute(null);
            e.Handled = true;
        }
    }

    // ── Scroll to bottom ──────────────────────────────────────────────────────
    private void ScrollToBottom()
    {
        Dispatcher.BeginInvoke(() =>
        {
            MessageScroll.ScrollToBottom();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }
}
