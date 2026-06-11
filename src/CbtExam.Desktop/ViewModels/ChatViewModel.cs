using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;

namespace CbtExam.Desktop.ViewModels;

public class ChatMessage
{
    public string Text     { get; set; } = "";
    public bool   IsUser   { get; set; }
    public string Time     { get; set; } = DateTime.Now.ToString("h:mm tt");
}

public class ChatViewModel : BaseViewModel
{
    // ── Visibility ──────────────────────────────────────────────────────────
    private bool _isOpen;
    public bool IsOpen
    {
        get => _isOpen;
        set { Set(ref _isOpen, value); OnPropertyChanged(nameof(IsClose)); }
    }
    public bool IsClose => !_isOpen;

    // ── Input ───────────────────────────────────────────────────────────────
    private string _inputText = "";
    public string InputText { get => _inputText; set => Set(ref _inputText, value); }

    // ── Bubble position (from bottom-right corner) ──────────────────────────
    private double _bubbleRight  = 24;
    private double _bubbleBottom = 24;
    public double BubbleRight  { get => _bubbleRight;  set { Set(ref _bubbleRight,  value); SavePosition(); } }
    public double BubbleBottom { get => _bubbleBottom; set { Set(ref _bubbleBottom, value); SavePosition(); } }

    // ── Messages ─────────────────────────────────────────────────────────────
    public ObservableCollection<ChatMessage> Messages { get; } = [];

    // ── Commands ──────────────────────────────────────────────────────────────
    public RelayCommand ToggleCommand { get; }
    public RelayCommand SendCommand   { get; }
    public RelayCommand ClearCommand  { get; }

    // ── Position file path ────────────────────────────────────────────────────
    private static string PositionFile => Path.Combine(
        Path.GetDirectoryName(Environment.ProcessPath) ?? AppDomain.CurrentDomain.BaseDirectory,
        "chat_position.json");

    public ChatViewModel()
    {
        ToggleCommand = new RelayCommand(() => IsOpen = !IsOpen);
        SendCommand   = new RelayCommand(Send, () => !string.IsNullOrWhiteSpace(InputText));
        ClearCommand  = new RelayCommand(() => Messages.Clear());

        LoadPosition();

        // Greeting on first open
        Messages.Add(new ChatMessage
        {
            Text   = "👋 Hi! I'm your CBT Exam Assistant. Ask me anything about the software, exams, students, sessions, or general questions.",
            IsUser = false,
            Time   = DateTime.Now.ToString("h:mm tt")
        });
    }

    private void Send()
    {
        var text = InputText.Trim();
        if (string.IsNullOrEmpty(text)) return;

        Messages.Add(new ChatMessage { Text = text, IsUser = true, Time = DateTime.Now.ToString("h:mm tt") });
        InputText = "";

        var reply = GenerateReply(text);
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            Messages.Add(new ChatMessage { Text = reply, IsUser = false, Time = DateTime.Now.ToString("h:mm tt") });
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    // ── Knowledge base ────────────────────────────────────────────────────────
    private static string GenerateReply(string input)
    {
        var q = input.ToLowerInvariant().Trim();

        // ── Date / Time ────────────────────────────────────────────────────────
        if (q.Contains("time") && (q.Contains("what") || q.Contains("current") || q.Contains("now")))
            return $"🕐 The current time is {DateTime.Now:h:mm:ss tt}.";
        if (q.Contains("date") && (q.Contains("what") || q.Contains("today") || q.Contains("current")))
            return $"📅 Today is {DateTime.Now:dddd, MMMM d, yyyy}.";
        if (q.Contains("day") && (q.Contains("what") || q.Contains("today")))
            return $"📅 Today is {DateTime.Now:dddd}.";
        if (q.Contains("year"))
            return $"📅 The current year is {DateTime.Now.Year}.";
        if (q.Contains("month"))
            return $"📅 The current month is {DateTime.Now:MMMM}.";

        // ── Greetings ──────────────────────────────────────────────────────────
        if (Matches(q, "hello", "hi", "hey", "good morning", "good afternoon", "good evening", "howdy", "sup", "greetings"))
        {
            var greet = DateTime.Now.Hour < 12 ? "Good morning" : DateTime.Now.Hour < 17 ? "Good afternoon" : "Good evening";
            return $"{greet}! 👋 How can I assist you with the CBT Exam System today?";
        }
        if (Matches(q, "how are you", "how r you", "you good", "how do you do"))
            return "I'm running perfectly! 😊 Ready to help you manage your exams.";
        if (Matches(q, "thank", "thanks", "thank you", "ty"))
            return "You're welcome! 😊 Let me know if there's anything else I can help with.";
        if (Matches(q, "bye", "goodbye", "see you", "later", "exit"))
            return "Goodbye! 👋 Come back anytime you need help.";

        // ── App overview ───────────────────────────────────────────────────────
        if (Matches(q, "what is this", "what is cbt", "about this app", "what does this do", "what is prep4jamb", "about the software"))
            return "📚 This is the **CBT Exam System (Prep4JAMB)** — an admin console for managing Computer-Based Tests. It lets you create exams, manage students, run live exam sessions, monitor candidates in real time, view results, and generate reports.";

        // ── Dashboard ─────────────────────────────────────────────────────────
        if (Matches(q, "dashboard", "overview", "home screen", "main screen"))
            return "📊 The **Dashboard** is your main overview page. It shows total students, active exams, submitted results, live server status, connected devices, and quick-access exam management. Navigate there using the sidebar.";

        // ── Exams ──────────────────────────────────────────────────────────────
        if (Matches(q, "create exam", "make exam", "new exam", "add exam"))
            return "➕ To create an exam:\n1. Click **Create Exam** in the sidebar\n2. Enter a title, subject, and duration\n3. Toggle shuffle options if needed\n4. Click **Save Exam**\n5. Then add questions manually or bulk-import via JSON.";
        if (Matches(q, "bulk import", "import json", "import questions", "json import"))
            return "📥 **Bulk Import**: On the Create Exam page, scroll to the Bulk Import section. Paste a JSON array like:\n`[{\"questionNumber\":1,\"text\":\"...\",\"options\":[\"A\",\"B\",\"C\",\"D\"],\"correctAnswer\":\"A\"}]`\nThen click **Import JSON**.";
        if (Matches(q, "edit exam", "update exam", "modify exam"))
            return "✏️ Go to **Exams** in the sidebar, find your exam, and click the edit icon. You can update the title, duration, and questions.";
        if (Matches(q, "delete exam", "remove exam"))
            return "🗑️ Go to **Exams**, find the exam, and click the delete icon. Note: deleting an exam also removes all associated questions and results.";
        if (Matches(q, "shuffle", "randomize", "random order"))
            return "🔀 **Shuffle Questions** randomizes the question order per student. **Shuffle Options** randomizes the answer choices. Both can be toggled when creating an exam.";
        if (Matches(q, "advanced mode", "4 subjects", "multi subject", "multiple subjects"))
            return "⚙️ **Advanced Mode** lets you configure a JAMB-style exam with 4 subjects, each with its own year range and question count. Toggle it using the 'Advanced Mode' checkbox on the Create Exam page.";
        if (Matches(q, "duration", "time limit", "exam time"))
            return "⏱️ Exam duration is set in **minutes** when creating an exam. Students will see a countdown timer during their exam.";

        // ── Question Bank ──────────────────────────────────────────────────────
        if (Matches(q, "question bank", "questions", "bank", "jamb questions", "past questions", "past paper"))
            return "📖 The **Question Bank** holds all stored JAMB past questions organised by subject and year. You can browse, search, filter, and download question packs. Use the **Settings** page to download the full repo.";
        if (Matches(q, "download questions", "get questions", "question repo", "sync questions"))
            return "⬇️ Go to **Settings** and find the 'Download Question Repository' section. Click download to sync the latest JAMB past questions to your local database.";

        // ── Students ──────────────────────────────────────────────────────────
        if (Matches(q, "add student", "register student", "create student", "new student"))
            return "👤 Go to **Students** in the sidebar and click **Add Student**. Enter the student's name and registration number. Students can also self-register from their exam device.";
        if (Matches(q, "students", "student list", "candidates", "view students"))
            return "👥 The **Students** page lists all registered candidates. You can search, filter, add, edit, or remove students, and see their exam history.";
        if (Matches(q, "student id", "registration number", "reg number", "student number"))
            return "🔑 Each student has a unique **Student ID / Registration Number** used to log into the exam client app. It's set when adding a student and cannot be changed after login.";

        // ── Sessions ──────────────────────────────────────────────────────────
        if (Matches(q, "session", "start exam", "begin exam", "launch exam", "exam session", "waiting room"))
            return "🚀 To start a session:\n1. Go to **Sessions**\n2. Select or create a session\n3. Share the **Session Code** with students\n4. Wait for students to join the waiting room\n5. Click **Start Exam** when ready.";
        if (Matches(q, "session code", "exam code", "join code"))
            return "🔐 The **Session Code** is a short code students enter in the exam client to join the waiting room. It's generated automatically when you create a session.";
        if (Matches(q, "end session", "stop exam", "finish session", "close session"))
            return "🛑 To end a session, go to **Sessions**, select the active session, and click **End Session**. This immediately stops all active exams and locks submissions.";
        if (Matches(q, "pause", "pause exam", "freeze exam"))
            return "⏸️ You can pause individual students from the **Monitor** page by clicking the pause icon next to their entry.";

        // ── Monitor ────────────────────────────────────────────────────────────
        if (Matches(q, "monitor", "live monitor", "real time", "realtime", "watching", "watch students"))
            return "👁️ The **Monitor** page shows a live real-time view of all students currently taking an exam — their status, tab switches (cheat detection), time remaining, and submission status. It updates via SignalR.";
        if (Matches(q, "cheat", "cheating", "tab switch", "switching tabs", "focus loss"))
            return "⚠️ The system tracks **tab switches / focus loss** for each student. Every time a student leaves the exam window, the count increments. You'll see a cheat warning notification and the count appears in the Monitor.";

        // ── Devices ────────────────────────────────────────────────────────────
        if (Matches(q, "devices", "connected devices", "computers", "laptops", "nodes"))
            return "💻 The **Devices** page shows all computers connected to your exam server — their IP addresses, connection status, and last seen time. Students connect automatically when they open the exam client.";
        if (Matches(q, "how many devices", "device count", "online devices"))
            return "💻 Check the **Devices** page or the Dashboard for a count of currently online devices.";

        // ── Results ────────────────────────────────────────────────────────────
        if (Matches(q, "results", "scores", "marks", "grades", "see results", "view results"))
            return "📈 The **Results** page shows all submitted exam scores. You can filter by student or exam, see individual scores and answer breakdowns, and export results.";
        if (Matches(q, "export results", "download results", "pdf", "print results"))
            return "📄 On the **Results** page, use the export button to generate a PDF report of results for the selected exam session.";
        if (Matches(q, "pass mark", "pass rate", "passing score", "cutoff"))
            return "📊 Pass marks and cutoffs are not hard-coded — they're shown in comparison on the Results page. JAMB's official cut-off is typically 200/400. You can filter by score range.";

        // ── Reports ────────────────────────────────────────────────────────────
        if (Matches(q, "reports", "analytics", "statistics", "analysis", "graph", "chart"))
            return "📉 The **Reports** page gives you analytics — score distributions, subject performance, attempt trends, and session comparisons visualised as charts.";

        // ── Settings ──────────────────────────────────────────────────────────
        if (Matches(q, "settings", "configuration", "preferences", "config", "setup"))
            return "⚙️ The **Settings** page lets you change the theme (Light/Dark), accent color, server port, admin password, and download the question repository.";
        if (Matches(q, "theme", "dark mode", "light mode", "change color", "appearance"))
            return "🎨 Click the sun/moon icon in the top-right to toggle Dark/Light mode. Full theme control (accent colors: Teal, Blue, Purple, Emerald, Rose) is in **Settings**.";
        if (Matches(q, "port", "server port", "change port", "network port"))
            return "🌐 The server port is configurable in **Settings**. Default is usually 5000. Change it if there's a conflict with another app, then restart the server.";
        if (Matches(q, "password", "admin password", "change password", "login password"))
            return "🔒 The admin login password can be changed in **Settings** under the Security section. The default password is set during first launch.";

        // ── Server ────────────────────────────────────────────────────────────
        if (Matches(q, "server", "server url", "ip address", "server running", "server start", "server stop", "embedded server"))
            return "🖥️ This app runs an **embedded ASP.NET Core server** locally. The server URL is shown in the top bar when running. Students connect to this URL from their exam devices on the same network. You can toggle the server from the server status button.";
        if (Matches(q, "server not running", "server error", "server fail", "server down", "server offline"))
            return "❌ If the server won't start:\n1. Check the **Troubleshooting Guide** (ErrorGuide in sidebar)\n2. Ensure the port isn't blocked by a firewall\n3. Try restarting as Administrator\n4. Check `cbt_error.log` next to the exe for details.";

        // ── Network / Connectivity ────────────────────────────────────────────
        if (Matches(q, "wifi", "network", "connect", "same network", "hotspot", "lan"))
            return "📡 All exam devices must be on the **same local network** as this admin computer. Share the server URL (shown in the top bar) with students. Works with WiFi, hotspot, or LAN.";

        // ── Notifications ─────────────────────────────────────────────────────
        if (Matches(q, "notification", "alerts", "bell", "unread"))
            return "🔔 The **Notifications** panel (bell icon, top-right) shows real-time alerts: student logins, exam starts, submissions, and cheat warnings. The badge count shows unread notifications.";

        // ── Broadcast ─────────────────────────────────────────────────────────
        if (Matches(q, "broadcast", "message students", "send message", "announce", "announcement"))
            return "📢 Use the **Broadcast** button in the Sessions page to send a message to all students currently in an active session. They'll see it as a popup.";

        // ── Sidebar / Navigation ──────────────────────────────────────────────
        if (Matches(q, "sidebar", "navigation", "menu", "collapse menu", "hide sidebar"))
            return "🗂️ Click the hamburger icon (☰) in the top-left to collapse or expand the sidebar. In collapsed mode, it shows only icons.";
        if (Matches(q, "search", "find", "global search", "search bar"))
            return "🔍 Use the **search bar** in the top navigation to quickly find students, exams, or questions across the system.";

        // ── Build / Install ───────────────────────────────────────────────────
        if (Matches(q, "build", "compile", "build exe", "bat file", "build bat", "publish"))
            return "🔨 Run `build.bat` in the root folder to compile and publish the app. It will restore packages, build in Release mode, and output a single-file exe to `publish/CbtExam/`.";
        if (Matches(q, "install", "setup", "requirements", "prerequisites", ".net", "dotnet"))
            return "⚙️ This app is self-contained — it bundles the .NET runtime. No separate .NET installation needed on deployment machines. Just copy the published exe.";

        // ── Troubleshooting ───────────────────────────────────────────────────
        if (Matches(q, "error", "problem", "issue", "not working", "bug", "crash", "troubleshoot", "fix"))
            return "🔧 For issues:\n1. Check the **ErrorGuide** page (sidebar → Troubleshooting)\n2. Look at `cbt_error.log` next to the exe\n3. Restart the server from the top bar\n4. Ensure all devices are on the same network.";
        if (Matches(q, "log", "error log", "cbt_error.log", "log file"))
            return "📋 The app writes detailed logs to `cbt_error.log` in the same folder as the exe. Check this file when something goes wrong — it includes timestamps and stack traces.";
        if (Matches(q, "database", "db", "sqlite", "cbt_exam.db", "data file"))
            return "🗄️ The app uses **SQLite** (`cbt_exam.db`) stored in the same folder as the exe. It auto-migrates on first run. If you need to reset data, stop the app and delete the `.db` file (⚠️ this erases all data).";

        // ── JAMB specific ─────────────────────────────────────────────────────
        if (Matches(q, "jamb", "utme", "waec", "neco", "post utme"))
            return "🎓 This system is optimised for **JAMB UTME** mock exams with past question support. The question bank covers JAMB past questions by subject and year. Subjects typically include English, Mathematics, Physics, Chemistry, Biology, etc.";
        if (Matches(q, "subject", "subjects", "available subjects"))
            return "📚 Supported JAMB subjects include English Language, Mathematics, Physics, Chemistry, Biology, Government, Economics, Literature, Geography, Commerce, and more. Check the Question Bank for available coverage.";

        // ── Math / calculation ────────────────────────────────────────────────
        var mathResult = TryMath(q);
        if (mathResult != null) return mathResult;

        // ── Who made this ─────────────────────────────────────────────────────
        if (Matches(q, "who made", "who built", "developer", "creator", "author", "contact developer", "who created"))
            return "👨‍💻 This app was built as a professional CBT exam management system. Use **Contact Developer** from the Help menu if you need to reach the development team.";

        // ── Help ───────────────────────────────────────────────────────────────
        if (Matches(q, "help", "what can you do", "capabilities", "commands", "features", "list", "options"))
            return "💡 I can help with:\n• **Time & Date** — current time, date, day\n• **Exams** — create, edit, delete, import questions\n• **Students** — add, manage, view\n• **Sessions** — start/stop, session codes\n• **Monitor** — live tracking, cheat detection\n• **Results** — scores, export\n• **Settings** — theme, server, password\n• **Troubleshooting** — errors, logs\n• **General questions** — just ask!";

        // ── Fallback ──────────────────────────────────────────────────────────
        return $"🤔 I'm not sure about that. Try asking about:\n• Exams, Students, Sessions, Monitor, Results\n• Settings, Server, Devices, Question Bank\n• Or type **help** to see everything I can do.";
    }

    private static bool Matches(string input, params string[] keywords)
        => keywords.Any(k => input.Contains(k, StringComparison.OrdinalIgnoreCase));

    private static string? TryMath(string q)
    {
        // simple arithmetic: "what is 5 + 3", "calculate 100 / 4", "2 * 8", etc.
        var m = Regex.Match(q, @"(\d+(?:\.\d+)?)\s*([\+\-\*\/x×÷])\s*(\d+(?:\.\d+)?)");
        if (!m.Success) return null;
        if (!double.TryParse(m.Groups[1].Value, out var a)) return null;
        if (!double.TryParse(m.Groups[3].Value, out var b)) return null;
        var op = m.Groups[2].Value;
        double result = op switch
        {
            "+" => a + b,
            "-" => a - b,
            "*" or "x" or "×" => a * b,
            "/" or "÷" => b == 0 ? double.NaN : a / b,
            _ => double.NaN
        };
        if (double.IsNaN(result)) return "⚠️ Can't divide by zero!";
        return $"🧮 {a} {op} {b} = **{result}**";
    }

    // ── Position persistence ──────────────────────────────────────────────────
    private void SavePosition()
    {
        try
        {
            var json = JsonSerializer.Serialize(new { Right = BubbleRight, Bottom = BubbleBottom });
            File.WriteAllText(PositionFile, json);
        }
        catch { }
    }

    private void LoadPosition()
    {
        try
        {
            if (!File.Exists(PositionFile)) return;
            var doc = JsonDocument.Parse(File.ReadAllText(PositionFile));
            if (doc.RootElement.TryGetProperty("Right",  out var r)) BubbleRight  = r.GetDouble();
            if (doc.RootElement.TryGetProperty("Bottom", out var b)) BubbleBottom = b.GetDouble();
        }
        catch { }
    }
}
