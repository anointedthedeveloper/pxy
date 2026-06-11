using CbtExam.Desktop.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CbtExam.Desktop.Views;

public partial class ChatBubble : UserControl
{
    // Drag state
    private bool   _isDragging;
    private Point  _dragStart;       // mouse position at drag start (relative to Canvas)
    private Point  _anchorStart;     // Canvas.Left / Canvas.Top of AnchorPanel at drag start
    private bool   _wasDragged;      // suppress click/toggle if user dragged

    public ChatBubble()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    // ── Place the panel on the canvas after layout pass ───────────────────────
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PositionAnchor();
        // Re-position whenever the parent window resizes
        if (Window.GetWindow(this) is Window w)
            w.SizeChanged += (_, _) => PositionAnchor();

        // Auto-scroll to bottom when a new message arrives
        if (DataContext is ChatViewModel vm)
            vm.Messages.CollectionChanged += (_, _) => ScrollToBottom();
    }

    /// <summary>
    /// Converts BubbleRight/BubbleBottom (distance from bottom-right corner)
    /// into Canvas.Left / Canvas.Top coordinates and clamps to window bounds.
    /// </summary>
    private void PositionAnchor()
    {
        if (DataContext is not ChatViewModel vm) return;

        var canvas = RootCanvas;
        var panel  = AnchorPanel;

        // We need the canvas actual size — if not ready yet, defer
        if (canvas.ActualWidth < 1 || canvas.ActualHeight < 1) return;

        panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var panelW = panel.DesiredSize.Width;
        var panelH = panel.DesiredSize.Height;

        double left = canvas.ActualWidth  - vm.BubbleRight  - panelW;
        double top  = canvas.ActualHeight - vm.BubbleBottom - panelH;

        // Clamp so panel never leaves the canvas
        left = Math.Max(0, Math.Min(canvas.ActualWidth  - panelW, left));
        top  = Math.Max(0, Math.Min(canvas.ActualHeight - panelH, top));

        Canvas.SetLeft(panel, left);
        Canvas.SetTop(panel,  top);
    }

    // ── Drag — FAB button ─────────────────────────────────────────────────────
    private void Fab_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        StartDrag(e);
        e.Handled = true;
    }

    // ── Drag — header (also used for reposition when popup is open) ───────────
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
        AnchorPanel.MouseMove        += Panel_MouseMove;
        AnchorPanel.MouseLeftButtonUp += Panel_MouseLeftButtonUp;
    }

    private void Panel_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;

        var current = e.GetPosition(RootCanvas);
        var dx = current.X - _dragStart.X;
        var dy = current.Y - _dragStart.Y;

        if (Math.Abs(dx) > 3 || Math.Abs(dy) > 3)
            _wasDragged = true;

        var panel  = AnchorPanel;
        var canvas = RootCanvas;

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
        AnchorPanel.MouseMove        -= Panel_MouseMove;
        AnchorPanel.MouseLeftButtonUp -= Panel_MouseLeftButtonUp;

        // Persist the new position as right/bottom offset
        if (DataContext is ChatViewModel vm && _wasDragged)
        {
            var canvas = RootCanvas;
            var panel  = AnchorPanel;
            vm.BubbleRight  = canvas.ActualWidth  - Canvas.GetLeft(panel) - panel.ActualWidth;
            vm.BubbleBottom = canvas.ActualHeight - Canvas.GetTop(panel)  - panel.ActualHeight;
        }

        e.Handled = true;
    }

    // ── Send on Enter key ─────────────────────────────────────────────────────
    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is ChatViewModel vm)
        {
            vm.SendCommand.Execute(null);
            e.Handled = true;
        }
    }

    // ── Auto-scroll messages to bottom ────────────────────────────────────────
    private void ScrollToBottom()
    {
        Dispatcher.BeginInvoke(() =>
        {
            MessageScroll.ScrollToBottom();
            InputBox.Focus();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }
}
