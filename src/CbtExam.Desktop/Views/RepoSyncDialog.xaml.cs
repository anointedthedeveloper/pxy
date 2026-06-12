using CbtExam.Desktop.Services;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;

namespace CbtExam.Desktop.Views;

/// <summary>
/// Subject-selection dialog for syncing the private question repository.
/// Flow:
///   1. User enters repo URL (and optionally overrides the bundled token).
///   2. Click "Fetch Available Subjects" → hits GitHub API, lists all .json files.
///   3. All subjects are pre-selected. User can deselect some.
///   4. Click "Download Selected" → downloads + imports each subject in order.
/// </summary>
public partial class RepoSyncDialog : Window
{
    // ── Injected dependencies ──────────────────────────────────────────
    private readonly ApiClient _api;
    private readonly string    _imagesDir;

    // ── State ──────────────────────────────────────────────────────────
    private List<SubjectItem>  _subjects   = [];
    private string             _owner      = "";
    private string             _repo       = "";
    private string             _branch     = "";
    private bool               _downloading = false;

    public bool Completed { get; private set; }
    public int  TotalImported { get; private set; }

    // ── Known subject name mappings (filename → pretty name) ──────────
    private static readonly Dictionary<string, string> KnownNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["civiledu"]       = "Civic Education",
            ["crk"]            = "Christian Religious Knowledge",
            ["irk"]            = "Islamic Religious Knowledge",
            ["englishlit"]     = "Literature In English",
            ["currentaffairs"] = "Current Affairs",
            ["english"]        = "English Language",
        };

    public RepoSyncDialog(ApiClient api, string imagesDir, string savedRepoUrl)
    {
        InitializeComponent();
        _api       = api;
        _imagesDir = imagesDir;

        RepoUrlBox.Text = savedRepoUrl;
        // Token box is pre-filled visually — actual value comes from bundled token
        TokenBox.Password = "● bundled token ●";

        Loaded += (_, _) => (Application.Current as App)?.ApplyTitleBarToWindow(this);
    }

    // ── Drag ──────────────────────────────────────────────────────────
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        base.OnMouseDown(e);
    }

    // ── Step 1: Fetch file list from GitHub ──────────────────────────
    private async void FetchBtn_Click(object sender, RoutedEventArgs e)
    {
        FetchStatusText.Text = "";
        FetchStatusText.Foreground = System.Windows.Media.Brushes.Red;
        SetFetchBtnBusy();

        var url = RepoUrlBox.Text.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(url))
        {
            ShowFetchError("Please enter the repository URL.");
            ResetFetchBtn();
            return;
        }

        // Resolve token: if user typed something that is NOT the placeholder, use it
        var userToken = TokenBox.Password.Trim();
        var token = (string.IsNullOrWhiteSpace(userToken) || userToken.StartsWith("●"))
            ? RepoTokenProvider.GetToken()
            : userToken;

        // Parse owner / repo / branch
        var normalised = url
            .Replace("https://raw.githubusercontent.com/", "")
            .Replace("https://github.com/", "")
            .TrimEnd('/');
        var parts = normalised.Split('/');
        if (parts.Length < 2)
        {
            ShowFetchError("Invalid URL. Expected: https://raw.githubusercontent.com/owner/repo/branch");
            ResetFetchBtn();
            return;
        }

        _owner  = parts[0];
        _repo   = parts[1];
        _branch = parts.Length >= 3 ? parts[2] : "main";

        using var client = BuildClient(token);

        try
        {
            var apiUrl   = $"https://api.github.com/repos/{_owner}/{_repo}/git/trees/{_branch}?recursive=1";
            var treeJson = await client.GetStringAsync(apiUrl);
            var tree     = JsonSerializer.Deserialize<JsonElement>(treeJson);

            // GitHub returns {"message":"..."} on error (401, 404, etc.)
            if (tree.TryGetProperty("message", out var msg))
            {
                var m = msg.GetString() ?? "Unknown error";
                ShowFetchError($"GitHub: {m}\nCheck repo URL and token permissions.");
                ResetFetchBtn();
                return;
            }

            var jsonFiles = tree.GetProperty("tree").EnumerateArray()
                .Where(f =>
                    f.GetProperty("path").GetString()?.EndsWith(".json", StringComparison.OrdinalIgnoreCase) == true &&
                    f.GetProperty("type").GetString() == "blob")
                .Select(f => f.GetProperty("path").GetString()!)
                .ToList();

            if (jsonFiles.Count == 0)
            {
                ShowFetchError("No JSON subject files found in this repository.");
                ResetFetchBtn();
                return;
            }

            // Build subject list — all selected by default
            _subjects = jsonFiles
                .Select(path =>
                {
                    var fn      = System.IO.Path.GetFileNameWithoutExtension(path);
                    var display = KnownNames.TryGetValue(fn, out var n) ? n : ToTitleCase(fn);
                    return new SubjectItem(path, display);
                })
                .OrderBy(s => s.DisplayName)
                .ToList();

            SubjectList.ItemsSource = _subjects;
            UpdateSelectionCount();

            // Transition to phase 2
            PhaseSelect.Visibility  = Visibility.Visible;
            DownloadBtn.IsEnabled   = true;
            FetchStatusText.Text    = $"✓ Found {_subjects.Count} subject(s). Select which to download.";
            FetchStatusText.Foreground = System.Windows.Media.Brushes.Green;
        }
        catch (Exception ex)
        {
            ShowFetchError($"Connection error: {ex.Message}");
        }

        ResetFetchBtn();
    }

    // ── Step 2: Select / deselect all ────────────────────────────────
    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var s in _subjects) s.IsSelected = true;
        SubjectList.Items.Refresh();
        UpdateSelectionCount();
    }

    private void SelectNone_Click(object sender, RoutedEventArgs e)
    {
        foreach (var s in _subjects) s.IsSelected = false;
        SubjectList.Items.Refresh();
        UpdateSelectionCount();
    }

    private void UpdateSelectionCount()
    {
        var sel = _subjects.Count(s => s.IsSelected);
        SelectionCountText.Text = $"{sel} of {_subjects.Count} subject(s) selected";
        DownloadBtn.IsEnabled   = sel > 0 && !_downloading;
    }

    // ── Step 3: Download selected subjects ───────────────────────────
    private async void DownloadBtn_Click(object sender, RoutedEventArgs e)
    {
        var selected = _subjects.Where(s => s.IsSelected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("Please select at least one subject.", "No Selection",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _downloading     = true;
        DownloadBtn.IsEnabled = false;
        CancelBtn.IsEnabled   = false;
        PhaseProgress.Visibility = Visibility.Visible;

        var userToken = TokenBox.Password.Trim();
        var token = (string.IsNullOrWhiteSpace(userToken) || userToken.StartsWith("●"))
            ? RepoTokenProvider.GetToken()
            : userToken;

        System.IO.Directory.CreateDirectory(_imagesDir);

        int totalImported = 0, totalSkipped = 0;
        using var client = BuildClient(token);

        for (int i = 0; i < selected.Count; i++)
        {
            var item    = selected[i];
            var pct     = (int)Math.Round((double)i / selected.Count * 100);
            UpdateProgress(pct, $"Downloading {item.DisplayName}…", $"({i + 1} of {selected.Count})");

            try
            {
                string raw;
                if (!string.IsNullOrWhiteSpace(token))
                {
                    // Private: use Contents API → base64 decode
                    var contentUrl  = $"https://api.github.com/repos/{_owner}/{_repo}/contents/{item.Path}?ref={_branch}";
                    var contentJson = await client.GetStringAsync(contentUrl);
                    var contentEl   = JsonSerializer.Deserialize<JsonElement>(contentJson);
                    var encoded     = contentEl.TryGetProperty("content", out var c)
                                      ? c.GetString() ?? "" : "";
                    encoded = encoded.Replace("\n", "").Replace("\r", "");
                    raw = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                }
                else
                {
                    var rawUrl = $"https://raw.githubusercontent.com/{_owner}/{_repo}/{_branch}/{item.Path}";
                    raw = await client.GetStringAsync(rawUrl);
                }

                var trimmed = raw.TrimStart();
                if (trimmed.StartsWith("<") || (!trimmed.StartsWith("{") && !trimmed.StartsWith("[")))
                    continue; // not JSON, skip

                var questions = ExtractJsonObjects(raw);
                if (questions.Count == 0) continue;

                UpdateProgress(pct, $"Processing images for {item.DisplayName}…", $"({i + 1} of {selected.Count})");

                var processed = new List<object>();
                foreach (var q in questions)
                {
                    if (!q.TryGetProperty("question", out var qText) || string.IsNullOrWhiteSpace(qText.GetString())) continue;
                    if (!q.TryGetProperty("option", out var optProp)) continue;
                    if (!q.TryGetProperty("answer", out var answerProp) || string.IsNullOrWhiteSpace(answerProp.GetString())) continue;
                    var ansLetter = answerProp.GetString()?.Trim().ToUpper() ?? "";
                    if (ansLetter != "A" && ansLetter != "B" && ansLetter != "C" && ansLetter != "D") continue;
                    var optA = optProp.TryGetProperty("a", out var pa) ? pa.GetString() : null;
                    var optB = optProp.TryGetProperty("b", out var pb) ? pb.GetString() : null;
                    if (string.IsNullOrWhiteSpace(optA) || string.IsNullOrWhiteSpace(optB)) continue;

                    string imageUrl = string.Empty;
                    if (q.TryGetProperty("image", out var imgProp))
                    {
                        var imgRaw = imgProp.GetString() ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(imgRaw) && imgRaw.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                var uri       = new Uri(imgRaw);
                                var localName = System.IO.Path.GetFileName(uri.LocalPath);
                                if (string.IsNullOrWhiteSpace(localName))
                                    localName = Guid.NewGuid().ToString("N") + ".jpg";
                                var localPath = System.IO.Path.Combine(_imagesDir, localName);
                                if (!System.IO.File.Exists(localPath))
                                {
                                    var imgBytes = await client.GetByteArrayAsync(imgRaw);
                                    await System.IO.File.WriteAllBytesAsync(localPath, imgBytes);
                                }
                                imageUrl = $"/images/questions/{localName}";
                            }
                            catch { imageUrl = imgRaw; }
                        }
                    }

                    var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(q.GetRawText())!;
                    var patched = new Dictionary<string, object?>();
                    foreach (var kv in dict)
                    {
                        patched[kv.Key] = kv.Value.ValueKind switch
                        {
                            JsonValueKind.String => kv.Value.GetString(),
                            JsonValueKind.Number => (object?)kv.Value.GetInt32(),
                            _                   => (object?)kv.Value
                        };
                    }
                    patched["image"] = imageUrl;
                    processed.Add(patched);
                }

                if (processed.Count == 0) continue;

                var resp = await _api.ImportRepoQuestionsAsync(item.DisplayName, processed);
                if (resp.IsSuccessStatusCode)
                {
                    var resultJson = await resp.Content.ReadAsStringAsync();
                    var result     = JsonSerializer.Deserialize<Dictionary<string, int>>(resultJson,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    totalImported += result?.GetValueOrDefault("imported") ?? 0;
                    totalSkipped  += result?.GetValueOrDefault("skipped")  ?? 0;
                }
            }
            catch (Exception ex)
            {
                App.Log($"Error downloading {item.DisplayName}", ex);
            }
        }

        // Done
        UpdateProgress(100, "Complete!", "");
        TotalImported = totalImported;
        Completed     = true;

        var msg = totalSkipped > 0
            ? $"✓ Done!  {totalImported} imported, {totalSkipped} already existed."
            : $"✓ Done!  {totalImported} questions saved offline.";

        FooterStatus.Text = msg;
        CancelBtn.Content = "Close";
        CancelBtn.IsEnabled = true;
    }

    // ── Helpers ──────────────────────────────────────────────────────
    private static HttpClient BuildClient(string token)
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        client.DefaultRequestHeaders.Add("User-Agent", "CbtExam");
        if (!string.IsNullOrWhiteSpace(token))
            client.DefaultRequestHeaders.Add("Authorization", $"token {token}");
        return client;
    }

    private void UpdateProgress(int pct, string main, string detail)
    {
        Dispatcher.Invoke(() =>
        {
            ProgressBar.Value    = pct;
            ProgressText.Text    = main;
            ProgressDetail.Text  = detail;
        });
    }

    private void ShowFetchError(string msg)
    {
        FetchStatusText.Text = msg;
        FetchStatusText.Foreground = System.Windows.Media.Brushes.Red;
    }

    private void ResetFetchBtn()
    {
        FetchBtn.IsEnabled  = true;
        FetchBtnIcon.Text   = "&#xE72D;";
        FetchBtnText.Text   = "Fetch Available Subjects";
    }

    // Called from FetchBtn_Click before awaiting to give visual feedback
    private void SetFetchBtnBusy()
    {
        FetchBtn.IsEnabled  = false;
        FetchBtnIcon.Text   = "&#xE712;"; // spinning / loading icon
        FetchBtnText.Text   = "Fetching…";
    }

    private static string ToTitleCase(string s) =>
        System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(s.Trim().ToLower());

    // Extracts valid JSON objects one by one — handles merge conflicts + malformed arrays
    private static List<JsonElement> ExtractJsonObjects(string raw)
    {
        var results = new List<JsonElement>();
        var seen    = new HashSet<string>();
        int depth = 0, start = -1;

        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            if (c == '{') { if (depth == 0) start = i; depth++; }
            else if (c == '}')
            {
                depth--;
                if (depth == 0 && start >= 0)
                {
                    var chunk = raw.Substring(start, i - start + 1);
                    try
                    {
                        var el    = JsonSerializer.Deserialize<JsonElement>(chunk);
                        var idKey = el.TryGetProperty("id",      out var id) ? id.GetInt32().ToString() : "";
                        var yrKey = el.TryGetProperty("examyear", out var yr) ? yr.GetString() ?? "" : "";
                        var key   = $"{idKey}|{yrKey}";
                        if (!string.IsNullOrEmpty(idKey) && seen.Add(key))
                            results.Add(el);
                    }
                    catch { /* skip malformed */ }
                    start = -1;
                }
            }
        }
        return results;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();
}

/// <summary>One row in the subject checklist.</summary>
public class SubjectItem : System.ComponentModel.INotifyPropertyChanged
{
    public string Path        { get; }
    public string DisplayName { get; }

    private bool _isSelected = true;
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); }
    }

    public SubjectItem(string path, string displayName)
    {
        Path        = path;
        DisplayName = displayName;
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}
