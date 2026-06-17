using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.IO;

namespace CbtExam.Desktop;

public partial class App : Application
{
    private string ThemeFile => Path.Combine(
        Path.GetDirectoryName(Environment.ProcessPath) ?? AppDomain.CurrentDomain.BaseDirectory,
        "theme.json");

    private static readonly string LogFile = Path.Combine(
        Path.GetDirectoryName(Environment.ProcessPath) ?? AppDomain.CurrentDomain.BaseDirectory,
        "cbt_error.log");

    // Current theme state — read by MainWindow to apply DWM on load
    public static string CurrentTheme  { get; private set; } = "Light";
    public static string CurrentAccent { get; private set; } = "Teal";

    // ── Logging ────────────────────────────────────────────────────────────
    public static void Log(string message, Exception? ex = null)
    {
        try
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
            if (ex is not null)
                line += $"\n  Exception: {ex.ToString()}";
            File.AppendAllText(LogFile, line + "\n");
        }
        catch { }
    }

    // ── Shell Integration ─────────────────────────────────────────────────
    [DllImport("shell32.dll", SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, int wParam, IntPtr lParam);

    private const int WM_SETICON = 0x80;
    private const int ICON_SMALL = 0;
    private const int ICON_BIG = 1;

    // ── Startup ────────────────────────────────────────────────────────────
    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            PdfSharp.Fonts.GlobalFontSettings.FontResolver = new SystemFontResolver();
        }
        catch { }

        // Fix taskbar grouping by binding to the executable path
        try 
        { 
            var path = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(path)) SetCurrentProcessExplicitAppUserModelID(path); 
        } 
        catch { }

        base.OnStartup(e);

        // Ensure app doesn't close when switching between windows
        ShutdownMode = ShutdownMode.OnLastWindowClose;

        DispatcherUnhandledException          += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        
        LoadTheme();

        var login = new Views.LoginWindow();
        login.Show();
    }

    // ── Exception handlers ─────────────────────────────────────────────────
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log("UI thread exception", e.Exception);
        MessageBox.Show(
            $"An unexpected error occurred:\n\n{e.Exception.Message}\n\n{e.Exception.InnerException?.Message}\n\nSee cbt_error.log for details.",
            "CBT Exam — Error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();
        Log("Unobserved task exception", e.Exception);
        Dispatcher.Invoke(() =>
            MessageBox.Show(
                $"Background error:\n\n{e.Exception.InnerException?.Message ?? e.Exception.Message}\n\nSee cbt_error.log for details.",
                "CBT Exam — Error", MessageBoxButton.OK, MessageBoxImage.Warning));
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        Log("Fatal unhandled exception", ex);
        MessageBox.Show(
            $"Fatal error:\n\n{ex?.Message}\n\nSee cbt_error.log for details.",
            "CBT Exam — Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    // ── Forceful close handler ──────────────────────────────────────────────
    private void OnProcessExit(object? sender, EventArgs e)
    {
        // Best-effort only — the embedded server may already be stopping.
        // Use task.Wait(timeout) to avoid a deadlock from .Result on the
        // thread-pool during shutdown.
        try
        {
            Log("Process exit detected - attempting to end all active sessions");

            var exeDir = Path.GetDirectoryName(Environment.ProcessPath)
                          ?? AppDomain.CurrentDomain.BaseDirectory;
            var serverUrlFile = Path.Combine(exeDir, "server_url.txt");

            string serverUrl = "http://localhost:5000";
            if (File.Exists(serverUrlFile))
            {
                try { serverUrl = File.ReadAllText(serverUrlFile).Trim(); } catch { }
            }
            else
            {
                var settingsFile = Path.Combine(exeDir, "settings.json");
                if (File.Exists(settingsFile))
                {
                    try
                    {
                        var json = File.ReadAllText(settingsFile);
                        using var doc = System.Text.Json.JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("Port", out var portProp))
                            serverUrl = $"http://localhost:{portProp.GetInt32()}";
                    }
                    catch { }
                }
            }

            using var httpClient = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(1) };
            httpClient.BaseAddress = new Uri(serverUrl + "/");
            httpClient.DefaultRequestHeaders.Add("X-Admin-Key",
                Environment.GetEnvironmentVariable("CBT_ADMIN_KEY") ?? "admin123");

            try
            {
                var task = httpClient.PostAsync("api/sessions/end-all", null);
                if (task.Wait(TimeSpan.FromSeconds(1)) && task.Result.IsSuccessStatusCode)
                    Log("Successfully ended all active sessions on forceful close");
                else
                    Log($"end-all did not succeed (status: {(task.IsCompleted ? task.Result.StatusCode.ToString() : "timeout")})");
            }
            catch (Exception ex)
            {
                Log("Error calling end-all sessions API", ex);
            }
        }
        catch (Exception ex)
        {
            Log("Error in ProcessExit handler", ex);
        }
    }

    // ── Server URL persistence for forceful close handling ───────────────────
    public static void StoreServerUrl(string url)
    {
        try
        {
            var exeDir = Path.GetDirectoryName(Environment.ProcessPath)
                          ?? AppDomain.CurrentDomain.BaseDirectory;
            var serverUrlFile = Path.Combine(exeDir, "server_url.txt");
            File.WriteAllText(serverUrlFile, url);
            Log($"Stored server URL: {url}");
        }
        catch (Exception ex)
        {
            Log("Failed to store server URL", ex);
        }
    }

    public static void ClearServerUrl()
    {
        try
        {
            var exeDir = Path.GetDirectoryName(Environment.ProcessPath)
                          ?? AppDomain.CurrentDomain.BaseDirectory;
            var serverUrlFile = Path.Combine(exeDir, "server_url.txt");
            if (File.Exists(serverUrlFile))
            {
                File.Delete(serverUrlFile);
                Log("Cleared server URL");
            }
        }
        catch (Exception ex)
        {
            Log("Failed to clear server URL", ex);
        }
    }

    // ── Theme engine ───────────────────────────────────────────────────────
    public void ApplyTheme(string theme, string accent)
    {
        CurrentTheme  = theme;
        CurrentAccent = accent;

        var dark = string.Equals(theme, "Dark", StringComparison.OrdinalIgnoreCase);

        // Accent palette
        var (accentHex, accentDarkHex, accentTintHex, accentMutedHex, accentFgHex) = accent switch
        {
            "Blue"    => ("#2563EB", "#1D4ED8", "#EFF6FF", "#BFDBFE", "#1E40AF"),
            "Purple"  => ("#7C3AED", "#6D28D9", "#F5F3FF", "#DDD6FE", "#4C1D95"),
            "Emerald" => ("#059669", "#047857", "#ECFDF5", "#A7F3D0", "#065F46"),
            "Rose"    => ("#E11D48", "#BE123C", "#FFF1F2", "#FECDD3", "#9F1239"),
            _         => ("#0D9488", "#0F766E", "#F0FDFA", "#99F6E4", "#134E4A"), // Teal
        };

        if (dark)
        {
            // ── Dark surfaces ──
            Set("BgBrush",            "#0F172A");  // deep navy
            Set("CardBrush",          "#1E293B");  // slate-800
            Set("SurfaceBrush",       "#1E293B");
            Set("InputBgBrush",       "#0F172A");
            Set("InputBorderBrush",   "#334155");
            Set("TextPrimaryBrush",   "#F1F5F9");
            Set("TextSecondaryBrush", "#94A3B8");
            Set("BorderBrush",        "#334155");
            Set("NavHoverBrush",      "#1E3A2F");
            Set("NavHoverFgBrush",    accentTintHex);
            Set("NavActiveBrush",     "#1E3A2F");
            Set("NavActiveFgBrush",   accentHex);
            Set("RowHoverBrush",      "#1E293B");
            Set("RowSelectedBrush",   "#1E3A2F");
            Set("HeaderBgBrush",      "#0F172A");
        }
        else
        {
            // ── Light surfaces ──
            Set("BgBrush",            "#F8FAFC");
            Set("CardBrush",          "#FFFFFF");
            Set("SurfaceBrush",       "#FFFFFF");
            Set("InputBgBrush",       "#FFFFFF");
            Set("InputBorderBrush",   accentMutedHex);
            Set("TextPrimaryBrush",   "#0F172A");
            Set("TextSecondaryBrush", "#64748B");
            Set("BorderBrush",        "#E5E7EB");
            Set("NavHoverBrush",      accentTintHex);
            Set("NavHoverFgBrush",    accentFgHex);
            Set("NavActiveBrush",     accentTintHex);
            Set("NavActiveFgBrush",   accentHex);
            Set("RowHoverBrush",      "#F9FAFB");
            Set("RowSelectedBrush",   accentTintHex);
            Set("HeaderBgBrush",      "#F8FAFC");
        }

        // Accent brushes (same for both modes)
        Set("AccentBrush",     accentHex);
        Set("AccentDarkBrush", accentDarkHex);

        SaveTheme(theme, accent);

        // Apply DWM title bar color to all open windows
        foreach (Window w in Current.Windows)
            ApplyTitleBar(w, dark, accentHex);
    }

    // Called from MainWindow after it's loaded so the HWND exists.
    // ForceWindowIcon sets both WPF icon and native WM_SETICON; ApplyTitleBar
    // then sets the DWM caption colour. Kept as one call site to avoid the
    // double icon-load that occurred when both were called separately.
    public void ApplyTitleBarToWindow(Window w)
    {
        ForceWindowIcon(w);

        var dark = string.Equals(CurrentTheme, "Dark", StringComparison.OrdinalIgnoreCase);
        var (accentHex, _, _, _, _) = CurrentAccent switch
        {
            "Blue"    => ("#2563EB", "", "", "", ""),
            "Purple"  => ("#7C3AED", "", "", "", ""),
            "Emerald" => ("#059669", "", "", "", ""),
            "Rose"    => ("#E11D48", "", "", "", ""),
            _         => ("#0D9488", "", "", "", ""),
        };
        ApplyTitleBar(w, dark, accentHex);
    }

    /// <summary>
    /// Sets the window icon on the WPF layer and sends WM_SETICON to the native
    /// Win32 layer so the taskbar button always shows the correct icon even after
    /// a LoginWindow → MainWindow transition recreates the taskbar entry.
    /// </summary>
    public static void ForceWindowIcon(Window w)
    {
        try
        {
            // ── WPF layer ──────────────────────────────────────────────────────────
            // BitmapImage cannot decode .ico properly — it picks the smallest frame.
            // Use IconBitmapDecoder to explicitly select the largest frame (256x256
            // or 32x32 if no 256 frame exists), which is what the taskbar shows.
            var iconUri    = new Uri("pack://application:,,,/Resources/appicon.ico", UriKind.Absolute);
            var stream     = Application.GetResourceStream(iconUri)?.Stream;
            if (stream != null)
            {
                using var ms = new System.IO.MemoryStream();
                stream.CopyTo(ms);
                ms.Position = 0;

                var decoder = new System.Windows.Media.Imaging.IconBitmapDecoder(
                    ms,
                    System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
                    System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);

                // Pick the largest frame available (sorted by width descending)
                var bestFrame = decoder.Frames
                    .OrderByDescending(f => f.PixelWidth)
                    .First();

                w.Icon = bestFrame;
            }

            // ── Native layer: WM_SETICON ───────────────────────────────────────────
            var hwnd = new WindowInteropHelper(w).EnsureHandle();
            if (hwnd == IntPtr.Zero) return;

            // Try loading from the exe first (works in published builds)
            var exePath = Environment.ProcessPath
                          ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;

            IntPtr hIconBig   = IntPtr.Zero;
            IntPtr hIconSmall = IntPtr.Zero;

            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
            {
                // IMAGE_ICON=1, LR_LOADFROMFILE=0x10
                hIconBig   = LoadImage(IntPtr.Zero, exePath, 1, 32, 32, 0x10);
                hIconSmall = LoadImage(IntPtr.Zero, exePath, 1, 16, 16, 0x10);
            }

            // Fallback: extract from the .ico resource file written next to the exe
            if (hIconBig == IntPtr.Zero)
            {
                var icoPath = Path.Combine(
                    Path.GetDirectoryName(exePath ?? AppDomain.CurrentDomain.BaseDirectory) ?? "",
                    "appicon.ico");
                if (!File.Exists(icoPath))
                {
                    // Write the embedded icon to disk so LoadImage can read it
                    var res = Application.GetResourceStream(iconUri)?.Stream;
                    if (res != null)
                    {
                        using var fs = File.Create(icoPath);
                        res.CopyTo(fs);
                    }
                }
                if (File.Exists(icoPath))
                {
                    hIconBig   = LoadImage(IntPtr.Zero, icoPath, 1, 32, 32, 0x10);
                    hIconSmall = LoadImage(IntPtr.Zero, icoPath, 1, 16, 16, 0x10);
                }
            }

            if (hIconBig   != IntPtr.Zero) SendMessage(hwnd, WM_SETICON, ICON_BIG,   hIconBig);
            if (hIconSmall != IntPtr.Zero) SendMessage(hwnd, WM_SETICON, ICON_SMALL, hIconSmall);
        }
        catch { /* icon is cosmetic — never crash */ }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);

    // ── DWM title bar ──────────────────────────────────────────────────────
    private static void ApplyTitleBar(Window w, bool dark, string accentHex)
    {
        try
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd == IntPtr.Zero) return;

            // Dark/light caption text (DWMWA_USE_IMMERSIVE_DARK_MODE = 20)
            int darkMode = dark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, 20, ref darkMode, sizeof(int));

            // Title bar background color (DWMWA_CAPTION_COLOR = 35) — Win11 only, safe to fail
            var color = ParseBgr(dark ? "#1E293B" : accentHex);
            DwmSetWindowAttribute(hwnd, 35, ref color, sizeof(int));
        }
        catch { /* older Windows — silently skip */ }
    }

    // Convert #RRGGBB → COLORREF (0x00BBGGRR)
    private static int ParseBgr(string hex)
    {
        hex = hex.TrimStart('#');
        int r = Convert.ToInt32(hex[..2], 16);
        int g = Convert.ToInt32(hex[2..4], 16);
        int b = Convert.ToInt32(hex[4..6], 16);
        return r | (g << 8) | (b << 16);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    // ── Brush helper ───────────────────────────────────────────────────────
    private void Set(string key, string colorHex)
    {
        var color = (Color)ColorConverter.ConvertFromString(colorHex);
        Resources[key] = new SolidColorBrush(color);
    }

    // ── Persistence ────────────────────────────────────────────────────────
    private void SaveTheme(string theme, string accent)
    {
        try
        {
            // Use FileStream with FileShare.ReadWrite to handle locked files
            using var fs = new FileStream(ThemeFile, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            using var writer = new StreamWriter(fs);
            writer.Write($"{theme}|{accent}");
        }
        catch (IOException)
        {
            // Silently ignore if file is locked - theme will be saved on next successful attempt
        }
    }

    private void LoadTheme()
    {
        try
        {
            if (!File.Exists(ThemeFile)) { ApplyTheme("Light", "Teal"); return; }
            
            // Use FileStream with FileShare.ReadWrite to handle locked files
            using var fs = new FileStream(ThemeFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs);
            var content = reader.ReadToEnd();
            var parts = content.Split('|');
            ApplyTheme(parts.Length >= 1 ? parts[0] : "Light",
                       parts.Length >= 2 ? parts[1] : "Teal");
        }
        catch (IOException)
        {
            // If file is locked, use default theme
            ApplyTheme("Light", "Teal");
        }
    }
}

public class SystemFontResolver : PdfSharp.Fonts.IFontResolver
{
    public PdfSharp.Fonts.FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
    {
        string name = familyName.ToLower();
        if (name == "segoe ui" || name == "segoeui")
        {
            if (bold && italic) return new PdfSharp.Fonts.FontResolverInfo("SegoeUI#bi");
            if (bold) return new PdfSharp.Fonts.FontResolverInfo("SegoeUI#b");
            if (italic) return new PdfSharp.Fonts.FontResolverInfo("SegoeUI#i");
            return new PdfSharp.Fonts.FontResolverInfo("SegoeUI#r");
        }
        if (name == "consolas")
        {
            if (bold && italic) return new PdfSharp.Fonts.FontResolverInfo("Consolas#bi");
            if (bold) return new PdfSharp.Fonts.FontResolverInfo("Consolas#b");
            if (italic) return new PdfSharp.Fonts.FontResolverInfo("Consolas#i");
            return new PdfSharp.Fonts.FontResolverInfo("Consolas#r");
        }
        
        return new PdfSharp.Fonts.FontResolverInfo("SegoeUI#r");
    }

    public byte[]? GetFont(string faceName)
    {
        try
        {
            string fontFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
            string file = faceName switch
            {
                "SegoeUI#r" => "segoeui.ttf",
                "SegoeUI#b" => "segoeuib.ttf",
                "SegoeUI#i" => "segoeuii.ttf",
                "SegoeUI#bi" => "segoeuiz.ttf",
                "Consolas#r" => "consola.ttf",
                "Consolas#b" => "consolab.ttf",
                "Consolas#i" => "consolai.ttf",
                "Consolas#bi" => "consolaz.ttf",
                _ => "segoeui.ttf"
            };

            string fullPath = Path.Combine(fontFolder, file);
            if (File.Exists(fullPath))
            {
                return File.ReadAllBytes(fullPath);
            }
            
            string defaultPath = Path.Combine(fontFolder, "segoeui.ttf");
            if (File.Exists(defaultPath)) return File.ReadAllBytes(defaultPath);
            
            defaultPath = Path.Combine(fontFolder, "arial.ttf");
            if (File.Exists(defaultPath)) return File.ReadAllBytes(defaultPath);
        }
        catch { }
        
        return null;
    }
}
