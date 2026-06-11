using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;

namespace CbtExam.Desktop.ViewModels;

public class ChatMessage
{
    public string Text      { get; set; } = "";
    public bool   IsUser    { get; set; }
    public bool   IsTyping  { get; set; }   // typing indicator bubble
    public string Time      { get; set; } = DateTime.Now.ToString("h:mm tt");
}

public class ChatViewModel : BaseViewModel
{
    // ── Panel visibility ────────────────────────────────────────────────────
    private bool _isOpen;
    public bool IsOpen
    {
        get => _isOpen;
        set { Set(ref _isOpen, value); OnPropertyChanged(nameof(IsClose)); }
    }
    public bool IsClose => !_isOpen;

    // ── FAB visibility (can be hidden via X button, restored via T+C) ───────
    private bool _isFabVisible = true;
    public bool IsFabVisible
    {
        get => _isFabVisible;
        set { Set(ref _isFabVisible, value); SavePosition(); }
    }

    // ── Input ────────────────────────────────────────────────────────────────
    private string _inputText = "";
    public string InputText
    {
        get => _inputText;
        set { Set(ref _inputText, value); SendCommand.RaiseCanExecuteChanged(); }
    }

    // ── Typing indicator ─────────────────────────────────────────────────────
    private bool _isTyping;
    public bool IsTyping { get => _isTyping; set => Set(ref _isTyping, value); }

    // ── Bubble position (distance from bottom-right corner) ──────────────────
    private double _bubbleRight  = 24;
    private double _bubbleBottom = 24;
    public double BubbleRight  { get => _bubbleRight;  set { Set(ref _bubbleRight,  value); SavePosition(); } }
    public double BubbleBottom { get => _bubbleBottom; set { Set(ref _bubbleBottom, value); SavePosition(); } }

    // ── Messages ──────────────────────────────────────────────────────────────
    public ObservableCollection<ChatMessage> Messages { get; } = [];

    // ── Commands ──────────────────────────────────────────────────────────────
    public RelayCommand        ToggleCommand  { get; }
    public RelayCommand        SendCommand    { get; }
    public RelayCommand        ClearCommand   { get; }
    public RelayCommand        HideFabCommand { get; }

    // ── Persistence file ──────────────────────────────────────────────────────
    private static string PositionFile => Path.Combine(
        Path.GetDirectoryName(Environment.ProcessPath) ?? AppDomain.CurrentDomain.BaseDirectory,
        "chat_position.json");

    public ChatViewModel()
    {
        ToggleCommand  = new RelayCommand(() => { if (IsFabVisible) IsOpen = !IsOpen; });
        SendCommand    = new RelayCommand(Send, () => !string.IsNullOrWhiteSpace(InputText));
        ClearCommand   = new RelayCommand(ClearChat);
        HideFabCommand = new RelayCommand(() => { IsOpen = false; IsFabVisible = false; });

        LoadPosition();

        Messages.Add(new ChatMessage
        {
            Text   = "👋 Hi there! I'm your CBT Exam Assistant — fully briefed on every corner of this system. Ask me about exams, students, sessions, results, settings, or anything else. I'm here to help.",
            IsUser = false,
            Time   = DateTime.Now.ToString("h:mm tt")
        });
    }

    private void ClearChat()
    {
        Messages.Clear();
        Messages.Add(new ChatMessage
        {
            Text   = "Chat cleared. How can I assist you?",
            IsUser = false,
            Time   = DateTime.Now.ToString("h:mm tt")
        });
    }

    // ── Send flow with typing indicator ──────────────────────────────────────
    private async void Send()
    {
        var text = InputText.Trim();
        if (string.IsNullOrEmpty(text)) return;

        Messages.Add(new ChatMessage { Text = text, IsUser = true, Time = DateTime.Now.ToString("h:mm tt") });
        InputText = "";

        // Show typing indicator
        IsTyping = true;

        // Simulate realistic thinking delay (300–900ms based on reply length)
        var reply = GenerateReply(text);
        int delay = Math.Min(900, Math.Max(300, reply.Length * 2));
        await Task.Delay(delay);

        IsTyping = false;
        Messages.Add(new ChatMessage { Text = reply, IsUser = false, Time = DateTime.Now.ToString("h:mm tt") });
    }

    // ════════════════════════════════════════════════════════════════════════
    //  KNOWLEDGE BASE
    // ════════════════════════════════════════════════════════════════════════
    private static string GenerateReply(string input)
    {
        var q = input.ToLowerInvariant().Trim();

        // ── Date & Time ────────────────────────────────────────────────────────
        if (Has(q, "what time", "current time", "time now", "tell me the time"))
            return $"🕐 It's currently {DateTime.Now:h:mm:ss tt}.";
        if (Has(q, "what date", "today's date", "current date", "what day is it", "today"))
            return $"📅 Today is {DateTime.Now:dddd, MMMM d, yyyy}.";
        if (Has(q, "what year", "current year"))
            return $"📅 We're in {DateTime.Now.Year}.";
        if (Has(q, "what month", "current month"))
            return $"📅 The current month is {DateTime.Now:MMMM yyyy}.";
        if (Has(q, "time") && Has(q, "what", "current", "now"))
            return $"🕐 The current time is {DateTime.Now:h:mm tt}.";

        // ── Greetings ──────────────────────────────────────────────────────────
        if (Has(q, "hello", "hi there", "hey there", "good morning", "good afternoon", "good evening", "howdy", "greetings", "what's up", "wassup"))
        {
            var hour  = DateTime.Now.Hour;
            var greet = hour < 12 ? "Good morning" : hour < 17 ? "Good afternoon" : "Good evening";
            var picks = new[] {
                $"{greet}! 👋 Ready to help you run a smooth exam session today.",
                $"{greet}! What can I help you with on the CBT Exam System?",
                $"{greet}! 😊 I'm fully briefed and ready — what do you need?"
            };
            return picks[DateTime.Now.Second % picks.Length];
        }
        if (Has(q, "how are you", "how r you", "you alright", "you okay", "you good"))
            return "All systems green! 🟢 Running at full capacity and ready to assist. What do you need?";
        if (Has(q, "thank you", "thanks", "thank u", "ty", "appreciate", "cheers"))
        {
            var picks = new[] {
                "My pleasure! 😊 Always here when you need me.",
                "Happy to help! Let me know if anything else comes up.",
                "Anytime! That's what I'm here for. 🤝"
            };
            return picks[DateTime.Now.Second % picks.Length];
        }
        if (Has(q, "bye", "goodbye", "see you", "later", "peace out", "signing off"))
            return "Take care! 👋 The system is in good hands. Come back anytime.";
        if (Has(q, "who are you", "what are you", "are you a bot", "are you ai", "are you real"))
            return "I'm the CBT Exam Assistant — a purpose-built AI embedded into this system. I'm trained on everything about this platform and can also handle general knowledge, calculations, and more. Think of me as your always-available support companion. 🤖";

        // ── App overview ───────────────────────────────────────────────────────
        if (Has(q, "what is this", "what is cbt", "about this app", "what does this do", "what is prep4jamb", "about the software", "what is this app"))
            return "📚 This is **CBT Exam System (Prep4JAMB)** — a professional admin console for managing Computer-Based Tests at scale. Core capabilities include:\n• Creating and publishing exams\n• Managing student registrations\n• Running live exam sessions with real-time monitoring\n• Cheat detection via tab-switch tracking\n• Generating detailed results and PDF reports\n• An embedded local server so it works without internet";

        // ── Dashboard ─────────────────────────────────────────────────────────
        if (Has(q, "dashboard", "home screen", "main screen", "overview page"))
            return "📊 The **Dashboard** is your command centre — it surfaces total students, active exams, submitted counts, live server status, connected devices, and a live activity feed that updates every few seconds. It's the first page you land on after login.";

        // ── Exams ──────────────────────────────────────────────────────────────
        if (Has(q, "create exam", "make exam", "new exam", "add exam", "how to create"))
            return "➕ Creating an exam:\n1. Go to **Create Exam** in the sidebar\n2. Enter a title, subject, and duration (minutes)\n3. Enable Shuffle Questions / Shuffle Options if needed\n4. Click **Save Exam** — the exam is now registered\n5. Add questions individually or bulk-import via JSON\n\nTip: Use Advanced Mode for 4-subject JAMB-style exams.";
        if (Has(q, "bulk import", "import json", "import questions", "json import", "paste json"))
            return "📥 **Bulk JSON Import**:\nOn the Create Exam page, scroll to the Bulk Import section and paste an array like:\n```\n[{\"questionNumber\":1,\"text\":\"What is...?\",\"options\":[\"A\",\"B\",\"C\",\"D\"],\"correctAnswer\":\"A\"}]\n```\nClick **Import JSON** — all questions load instantly.";
        if (Has(q, "edit exam", "update exam", "modify exam", "change exam"))
            return "✏️ Head to **Exams** in the sidebar → locate your exam → click the edit (pencil) icon. You can update the title, duration, shuffle settings, and questions.";
        if (Has(q, "delete exam", "remove exam"))
            return "🗑️ Go to **Exams** → find the exam → click the delete icon. Be aware: this also permanently removes all associated questions and result records.";
        if (Has(q, "shuffle", "randomize questions", "random order", "shuffle options"))
            return "🔀 Two shuffle modes are available:\n• **Shuffle Questions** — each student gets questions in a different order\n• **Shuffle Options** — answer choices are randomised per student\n\nBoth are configurable per exam during creation.";
        if (Has(q, "advanced mode", "4 subjects", "multi subject", "four subjects"))
            return "⚙️ **Advanced Mode** enables a full JAMB-style multi-subject exam. You configure 4 subjects, each with a year range and question count. The system auto-pulls the right questions from the question bank and assembles the exam automatically.";
        if (Has(q, "duration", "time limit", "exam duration", "how long"))
            return "⏱️ Exam duration is set in **minutes** during exam creation. Students see a live countdown timer throughout their session. When time expires, the exam auto-submits.";

        // ── Question Bank ──────────────────────────────────────────────────────
        if (Has(q, "question bank", "past questions", "question library", "past paper", "jamb questions", "bank"))
            return "📖 The **Question Bank** is the heart of the system — it stores thousands of JAMB past questions indexed by subject and year. You can:\n• Browse and search questions\n• Filter by subject/year\n• Download full subject packs\n• Import from Excel/CSV\n\nSync the latest questions via the Settings page.";
        if (Has(q, "download questions", "sync questions", "question repo", "get questions"))
            return "⬇️ Go to **Settings** → Question Repository section → click **Download**. This syncs the latest JAMB past question database to your local machine. An internet connection is required for the initial sync only.";
        if (Has(q, "upload questions", "import excel", "csv import", "add questions from file"))
            return "📤 On the **Question Bank** page, use the import section to upload questions from Excel or CSV files. The system parses and validates each question before adding it to the bank.";

        // ── Students ──────────────────────────────────────────────────────────
        if (Has(q, "add student", "register student", "new student", "create student"))
            return "👤 To add a student:\n1. Go to **Students** in the sidebar\n2. Click **Add Student**\n3. Enter full name and a unique registration number\n\nAlternatively, students can self-register directly from the exam client app on their device.";
        if (Has(q, "student list", "view students", "all students", "candidates list"))
            return "👥 The **Students** page lists every registered candidate. You can search by name or ID, filter by status, bulk-import from a spreadsheet, or export the full list. Each student row links to their exam history.";
        if (Has(q, "student id", "registration number", "reg number", "student number"))
            return "🔑 Each student's **Registration Number** is their login credential for the exam client. It's unique, set during registration, and cannot be changed once the student has logged in to an active session.";
        if (Has(q, "delete student", "remove student", "remove candidate"))
            return "🗑️ On the **Students** page, locate the student and click the delete icon. This removes their record and all associated exam history. This action is irreversible.";

        // ── Sessions ──────────────────────────────────────────────────────────
        if (Has(q, "how to start", "start session", "begin exam", "launch exam", "create session", "exam session", "waiting room", "start exam"))
            return "🚀 Running an exam session:\n1. Navigate to **Sessions**\n2. Click **New Session** and select an exam\n3. Share the **Session Code** with students\n4. Students join from their devices — they appear in the waiting room\n5. Once everyone is ready, click **Start Exam**\n6. Monitor live from the Monitor page\n7. End with **End Session** when done";
        if (Has(q, "session code", "exam code", "join code", "access code for exam"))
            return "🔐 The **Session Code** is an auto-generated short code (e.g. `EX-2847`) that students enter in the exam client to join the correct session. Each session has a unique code.";
        if (Has(q, "end session", "stop exam", "close session", "finish exam"))
            return "🛑 To end a session: go to **Sessions** → select the active session → click **End Session**. All in-progress exams are immediately locked and submissions are finalised.";
        if (Has(q, "pause exam", "freeze exam", "suspend student"))
            return "⏸️ Individual students can be paused from the **Monitor** page. Click the pause icon next to any student's row. Their timer freezes and they see a 'Paused by Admin' message.";

        // ── Monitor ────────────────────────────────────────────────────────────
        if (Has(q, "monitor", "live monitor", "watch students", "real time", "realtime", "live view"))
            return "👁️ The **Monitor** page is your live exam control panel:\n• Real-time status per student (Not Started / Examining / Submitted)\n• Tab-switch / focus-loss cheat counter\n• Time remaining per student\n• Pause/resume controls\n• Auto-refreshes via SignalR — no manual refresh needed";
        if (Has(q, "cheat", "cheating", "tab switch", "focus loss", "switching tabs", "malpractice"))
            return "⚠️ The system automatically tracks **tab switches and focus loss** — every time a student minimises the exam or switches to another app, the counter increments. You'll receive a real-time notification and the count is visible on the Monitor page. High counts flag a candidate for review.";

        // ── Devices ────────────────────────────────────────────────────────────
        if (Has(q, "devices", "connected devices", "registered devices", "exam computers", "nodes"))
            return "💻 The **Devices** page shows every computer that has connected to your server — IP address, MAC address (where available), device name, online/offline status, and last seen timestamp. Devices appear automatically when students open the exam client.";

        // ── Results ────────────────────────────────────────────────────────────
        if (Has(q, "results", "scores", "marks", "grades", "view results", "see results"))
            return "📈 The **Results** page shows all submitted exam data:\n• Individual scores and percentage\n• Per-question answer breakdown\n• Comparison against average\n• Filter by student, exam, or date range\n• Export as PDF with one click";
        if (Has(q, "export", "pdf", "print results", "download results"))
            return "📄 From the **Results** page, click the **Export PDF** button on any session or student entry. The system generates a formatted PDF report including scores, answer analysis, and session metadata.";
        if (Has(q, "pass mark", "cutoff", "passing score", "jamb cutoff"))
            return "📊 The system doesn't enforce a fixed pass mark — that's your call. JAMB's typical aggregate cutoff is 200/400. You can filter the Results page by score range to quickly identify who passed or failed your own threshold.";

        // ── Reports ────────────────────────────────────────────────────────────
        if (Has(q, "reports", "analytics", "statistics", "charts", "graphs", "performance analysis"))
            return "📉 The **Reports** page gives you visual analytics:\n• Score distribution histograms\n• Subject-by-subject performance breakdown\n• Session-over-session comparison\n• Top/bottom performers\n• Attempt frequency trends\n\nAll charts are interactive and exportable.";

        // ── Settings ──────────────────────────────────────────────────────────
        if (Has(q, "settings", "configuration", "preferences", "setup page"))
            return "⚙️ The **Settings** page covers:\n• Theme (Light/Dark) and accent colour\n• Server port configuration\n• Admin password change\n• Question repository download\n• School logo upload\n• Session defaults\n• System diagnostics";
        if (Has(q, "theme", "dark mode", "light mode", "change colour", "change color", "appearance"))
            return "🎨 Toggle Dark/Light mode instantly using the sun/moon icon in the top bar. For accent colours (Teal, Blue, Purple, Emerald, Rose), go to **Settings**. All changes apply live without restarting.";
        if (Has(q, "port", "server port", "change port", "network port"))
            return "🌐 The server port is set in **Settings** (default: 5000). Change it if another application is using the same port. After changing, click **Restart Server** to apply the new port.";
        if (Has(q, "password", "change password", "admin password", "login password"))
            return "🔒 Go to **Settings** → Security → **Change Admin Password**. Enter your current password then the new one. The change takes effect on next login.";

        // ── Server ────────────────────────────────────────────────────────────
        if (Has(q, "server", "server url", "ip address", "embedded server", "server running"))
            return "🖥️ This app runs a **self-contained ASP.NET Core server** locally on your machine. The live URL is shown in the top bar (e.g. `http://192.168.x.x:5000`). Students connect to that URL from their devices on the same network. The server starts automatically on launch.";
        if (Has(q, "server not running", "server error", "server failed", "server down", "server offline", "server won't start"))
            return "❌ Server not starting? Try these steps:\n1. Open **Troubleshooting Guide** (sidebar)\n2. Check `cbt_error.log` next to the exe for the exact error\n3. Ensure no firewall is blocking the port\n4. Try running the exe as Administrator\n5. Confirm no other app is using the same port in Settings";

        // ── Network ───────────────────────────────────────────────────────────
        if (Has(q, "wifi", "network", "hotspot", "lan", "same network", "connect devices"))
            return "📡 All student devices must be on the **same local network** as the admin machine — WiFi, LAN, or a mobile hotspot all work. Share the server URL from the top bar. No internet required once the question bank is synced.";

        // ── Notifications ─────────────────────────────────────────────────────
        if (Has(q, "notification", "alerts", "bell icon", "unread count"))
            return "🔔 The **Notifications** panel (bell icon, top-right) delivers real-time system alerts:\n• Candidate login events\n• Exam start/submission confirmations\n• Cheat warnings\n• Session lifecycle events\n\nThe orange badge shows your unread count. Click it to mark all as read.";

        // ── Broadcast ─────────────────────────────────────────────────────────
        if (Has(q, "broadcast", "message students", "send message", "announcement", "announce to students"))
            return "📢 The **Broadcast** feature (Sessions page) lets you push a message to all students currently in an active session. It appears as a modal popup on their exam screen — useful for instructions, warnings, or time extensions.";

        // ── Keyboard shortcut ─────────────────────────────────────────────────
        if (Has(q, "shortcut", "keyboard", "hotkey", "reopen chat", "t+c", "tc"))
            return "⌨️ Press **T + C** anywhere in the app to instantly reopen the chat assistant if you've hidden it. This works as a global hotkey within the application window.";
        if (Has(q, "hide chat", "close chat", "remove chat", "dismiss chat"))
            return "You can hide the chat bubble by clicking the **✕** button next to the FAB. To bring it back, press **T + C** on your keyboard.";

        // ── Sidebar / Navigation ──────────────────────────────────────────────
        if (Has(q, "sidebar", "navigation", "menu", "collapse", "hide sidebar", "expand sidebar"))
            return "🗂️ The sidebar can be toggled using the **☰** hamburger icon in the top-left. Collapsed mode shows icon-only navigation. Your preference is preserved across sessions.";
        if (Has(q, "search", "global search", "search bar", "find student", "find exam"))
            return "🔍 The **global search bar** (top navigation) searches across students, exams, and questions simultaneously. Results open in a dedicated Search Results view.";

        // ── Build & Deploy ────────────────────────────────────────────────────
        if (Has(q, "build", "compile", "build.bat", "publish", "build exe"))
            return "🔨 To build the app:\n1. Run `build.bat` from the project root\n2. It kills any running instance, restores NuGet packages, and publishes a single-file self-contained exe\n3. Output lands in `publish/CbtExam/CbtExam.exe`\n4. Plays a sound on success or failure\n\nNo .NET installation needed on deployment machines.";
        if (Has(q, "install", "deployment", "distribute", "requirements", "dotnet", ".net"))
            return "⚙️ The published exe is fully **self-contained** — it bundles the .NET 8 runtime. Just copy `CbtExam.exe` to any Windows machine and run it. No installation required.";

        // ── Troubleshooting ───────────────────────────────────────────────────
        if (Has(q, "not working", "broken", "bug", "crash", "error", "issue", "problem", "troubleshoot", "fix"))
            return "🔧 Troubleshooting checklist:\n1. Check the **Error Guide** page (sidebar)\n2. Open `cbt_error.log` beside the exe — it has timestamps and stack traces\n3. Restart the server from the top bar\n4. Ensure all devices are on the same network\n5. If the DB is corrupted, rename `cbt_exam.db` and restart — it will rebuild\n\nStill stuck? Use **Contact Developer** from the Help menu.";
        if (Has(q, "log file", "error log", "cbt_error.log"))
            return "📋 Every error and event is written to `cbt_error.log` in the same folder as the exe. It's plain text — you can open it in Notepad. Bring this file when contacting the developer.";
        if (Has(q, "database", "sqlite", "cbt_exam.db", "reset database", "data file"))
            return "🗄️ The app uses **SQLite** (`cbt_exam.db`) stored alongside the exe. It auto-migrates on first run. To do a clean reset: stop the app, delete or rename the `.db` file, and restart. ⚠️ This permanently erases all students, exams, and results.";

        // ── JAMB ──────────────────────────────────────────────────────────────
        if (Has(q, "jamb", "utme", "jamb score", "jamb cutoff", "jamb exam"))
            return "🎓 This system is purpose-built for **JAMB UTME** mock exam delivery. The question bank covers JAMB past questions by subject and year going back many sessions. Students practice under realistic timed conditions — same format as the actual JAMB CBT.";
        if (Has(q, "waec", "neco", "post utme", "post-utme"))
            return "📝 While the primary focus is JAMB UTME, the exam engine is flexible enough to run WAEC, NECO, or Post-UTME styled exams — you just need to supply the appropriate questions in the question bank.";
        if (Has(q, "subject", "subjects", "available subjects", "which subjects"))
            return "📚 JAMB subjects supported include:\nEnglish Language, Mathematics, Physics, Chemistry, Biology, Agricultural Science, Government, Economics, Literature in English, Geography, Commerce, Civic Education, CRS/IRS, Accounting, Further Mathematics, and more. Coverage depends on your question bank.";

        // ── Math ──────────────────────────────────────────────────────────────
        var math = TryMath(q);
        if (math != null) return math;

        // ── Meta ──────────────────────────────────────────────────────────────
        if (Has(q, "who made", "who built", "developer", "creator", "who created", "author", "company", "anobyte"))
            return "👨‍💻 This system was engineered by **Anobyte Technologies** — a professional software development company.\n\n🌐 Website: anobyte.online\n📱 WhatsApp: +234 810 120 9470\n📱 WhatsApp: +234 901 647 1351\n\nUse the **Contact Developer** option in the Help menu to reach the team directly from within the app.";
        if (Has(q, "version", "app version", "which version"))
            return "ℹ️ Version information is available in the **Settings** page under System Info. For the most current build details, check the publish date on `CbtExam.exe`.";
        if (Has(q, "help", "what can you do", "your capabilities", "commands", "what do you know"))
            return "💡 Here's what I can help with:\n\n📋 **Software** — Exams, Students, Sessions, Monitor, Results, Reports, Settings, Server, Devices, Question Bank, Notifications, Build\n\n🕐 **General** — Current time, date, calculations, general questions\n\n⌨️ **Tip** — Press T+C to reopen me if you ever close the chat bubble.";

        // ── Fallback — sophisticated, not dismissive ───────────────────────────
        var fallbacks = new[]
        {
            $"That's an interesting query — I don't currently have specific information on \"{input.Trim()}\". For best results, try rephrasing or ask about a specific feature like exams, sessions, students, or settings. I'm most effective when the question relates to this system.",
            $"I want to give you an accurate answer, so I'll be transparent: I don't have sufficient context on that particular topic right now. If it relates to the CBT system, try narrowing the question — or type **help** to see everything I'm equipped to handle.",
            $"My knowledge base doesn't cover \"{input.Trim()}\" at the depth needed to give you a reliable response. I'd rather acknowledge that gap than risk misleading you. If this is a software question, feel free to rephrase — I may recognise it under a different angle.",
        };
        return fallbacks[Math.Abs(input.GetHashCode()) % fallbacks.Length];
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static bool Has(string input, params string[] keywords)
        => keywords.Any(k => input.Contains(k, StringComparison.OrdinalIgnoreCase));

    private static string? TryMath(string q)
    {
        var m = Regex.Match(q, @"(\d+(?:\.\d+)?)\s*([\+\-\*\/x×÷%])\s*(\d+(?:\.\d+)?)");
        if (!m.Success) return null;
        if (!double.TryParse(m.Groups[1].Value, out var a)) return null;
        if (!double.TryParse(m.Groups[3].Value, out var b)) return null;
        var op = m.Groups[2].Value;
        double result = op switch
        {
            "+"         => a + b,
            "-"         => a - b,
            "*" or "x" or "×" => a * b,
            "/" or "÷"  => b == 0 ? double.NaN : a / b,
            "%"         => b == 0 ? double.NaN : a % b,
            _           => double.NaN
        };
        if (double.IsNaN(result)) return "⚠️ Cannot divide by zero.";
        var opLabel = op switch { "x" => "×", _ => op };
        return $"🧮 {a} {opLabel} {b} = **{result}**";
    }

    // ── Position persistence ──────────────────────────────────────────────────
    private void SavePosition()
    {
        try
        {
            var json = JsonSerializer.Serialize(new
            {
                Right      = BubbleRight,
                Bottom     = BubbleBottom,
                FabVisible = IsFabVisible
            });
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
            if (doc.RootElement.TryGetProperty("Right",      out var r)) BubbleRight  = r.GetDouble();
            if (doc.RootElement.TryGetProperty("Bottom",     out var b)) BubbleBottom = b.GetDouble();
            if (doc.RootElement.TryGetProperty("FabVisible", out var f)) _isFabVisible = f.GetBoolean();
        }
        catch { }
    }
}
