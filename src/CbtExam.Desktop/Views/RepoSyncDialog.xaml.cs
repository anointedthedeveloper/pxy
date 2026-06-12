using CbtExam.Desktop.Services;
using CbtExam.Desktop.ViewModels;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;

namespace CbtExam.Desktop.Views;

public partial class RepoSyncDialog : Window
{
    private readonly ApiClient _api;
    private readonly string    _imagesDir;

    private List<SubjectItem> _subjects    = [];
    private string            _proxyBase   = "";
    private bool              _downloading = false;

    public bool Completed     { get; private set; }
    public int  TotalImported { get; private set; }
    public string SavedProxyUrl { get; private set; }
    public string SavedApiKey   { get; private set; }

    public RepoSyncDialog(ApiClient api, string imagesDir, string savedProxyUrl, string savedApiKey)
    {
        InitializeComponent();
        _api       = api;
        _imagesDir = imagesDir;
        SavedProxyUrl = savedProxyUrl;
        SavedApiKey   = savedApiKey;
        ProxyUrlBox.Text   = savedProxyUrl;
        ApiKeyBox.Password = savedApiKey;
        Loaded += async (_, _) =>
        {
            (Application.Current as App)?.ApplyTitleBarToWindow(this);
            // Auto-fetch if we already have saved credentials
            if (!string.IsNullOrWhiteSpace(savedProxyUrl) && !string.IsNullOrWhiteSpace(savedApiKey))
                await DoFetchAsync(savedProxyUrl, savedApiKey);
        };
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        base.OnMouseDown(e);
    }

    // ── Step 1: Fetch subject list from proxy ─────────────────────────
    private async void FetchBtn_Click(object sender, RoutedEventArgs e)
    {
        var proxyUrl = ProxyUrlBox.Text.Trim().TrimEnd('/');
        var apiKey   = ApiKeyBox.Password.Trim();
        await DoFetchAsync(proxyUrl, apiKey);
    }

    private async Task DoFetchAsync(string proxyUrl, string apiKey)
    {
        FetchStatusText.Text       = "";
        FetchStatusText.Foreground = System.Windows.Media.Brushes.Red;
        SetFetchBtnBusy();

        if (string.IsNullOrWhiteSpace(proxyUrl)) { ShowFetchError("Please enter the proxy server URL."); ResetFetchBtn(); return; }
        if (string.IsNullOrWhiteSpace(apiKey))   { ShowFetchError("Please enter the API key.");           ResetFetchBtn(); return; }

        _proxyBase    = proxyUrl;
        SavedProxyUrl = proxyUrl;
        SavedApiKey   = apiKey;

        using var client = BuildClient(apiKey);
        try
        {
            FetchStatusText.Text       = "Connecting to proxy…";
            FetchStatusText.Foreground = System.Windows.Media.Brushes.Gray;

            var resp = await client.GetAsync($"{proxyUrl}/subjects");
            var body = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode) { ShowFetchError($"Proxy error {(int)resp.StatusCode}: {body}"); ResetFetchBtn(); return; }

            var list = JsonSerializer.Deserialize<List<ProxySubjectDto>>(body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

            if (list.Count == 0) { ShowFetchError("No subject files found in the repository."); ResetFetchBtn(); return; }

            _subjects = list
                .Select(s => new SubjectItem(s.Path, s.Name, s.Count))
                .OrderBy(s => s.DisplayName)
                .ToList();

            SubjectList.ItemsSource = _subjects;
            UpdateSelectionCount();

            PhaseSelect.Visibility     = Visibility.Visible;
            DownloadBtn.IsEnabled      = true;
            FetchStatusText.Text       = $"✓ Found {_subjects.Count} subject(s).";
            FetchStatusText.Foreground = System.Windows.Media.Brushes.Green;
        }
        catch (Exception ex) { ShowFetchError($"Connection error: {ex.Message}"); }

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

    // Command used by MouseBinding in the subject row DataTemplate
    public System.Windows.Input.ICommand ToggleSubjectCommand => new RelayCommand<SubjectItem>(s =>
    {
        if (s is null) return;
        s.IsSelected = !s.IsSelected;
        UpdateSelectionCount();
    });

    private void UpdateSelectionCount()
    {
        var sel     = _subjects.Count(s => s.IsSelected);
        var totalQs = _subjects.Where(s => s.IsSelected).Sum(s => s.Count);
        var qLabel  = totalQs > 0 ? $"  ·  {totalQs:N0} questions" : "";
        SelectionCountText.Text = $"{sel} of {_subjects.Count} subject(s) selected{qLabel}";
        DownloadBtn.IsEnabled   = sel > 0 && !_downloading;
    }

    // ── Step 3: Download selected ─────────────────────────────────────
    private async void DownloadBtn_Click(object sender, RoutedEventArgs e)
    {
        var selected = _subjects.Where(s => s.IsSelected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("Select at least one subject.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _downloading          = true;
        DownloadBtn.IsEnabled = false;
        CancelBtn.IsEnabled   = false;
        PhaseProgress.Visibility = Visibility.Visible;

        var apiKey = ApiKeyBox.Password.Trim();
        System.IO.Directory.CreateDirectory(_imagesDir);

        int totalImported = 0, totalSkipped = 0;
        using var client = BuildClient(apiKey);

        for (int i = 0; i < selected.Count; i++)
        {
            var item = selected[i];
            var pct  = (int)Math.Round((double)i / selected.Count * 100);
            UpdateProgress(pct, $"Downloading {item.DisplayName}…", $"({i + 1} of {selected.Count})");

            try
            {
                var fileResp = await client.GetAsync($"{_proxyBase}/file?path={Uri.EscapeDataString(item.Path)}");
                if (!fileResp.IsSuccessStatusCode) continue;

                var raw = await fileResp.Content.ReadAsStringAsync();
                var t   = raw.TrimStart();
                if (t.StartsWith("<") || (!t.StartsWith("{") && !t.StartsWith("["))) continue;

                var questions = ExtractJsonObjects(raw);
                if (questions.Count == 0) continue;

                UpdateProgress(pct, $"Processing images for {item.DisplayName}…", $"({i + 1} of {selected.Count})");

                var processed = await BuildProcessedListAsync(questions, client);
                if (processed.Count == 0) continue;

                var importResp = await _api.ImportRepoQuestionsAsync(item.DisplayName, processed);
                if (importResp.IsSuccessStatusCode)
                {
                    var resultJson = await importResp.Content.ReadAsStringAsync();
                    var result     = JsonSerializer.Deserialize<Dictionary<string, int>>(resultJson,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    totalImported += result?.GetValueOrDefault("imported") ?? 0;
                    totalSkipped  += result?.GetValueOrDefault("skipped")  ?? 0;
                }
            }
            catch (Exception ex) { App.Log($"Error downloading {item.DisplayName}", ex); }
        }

        UpdateProgress(100, "Complete!", "");
        TotalImported = totalImported;
        Completed     = true;

        FooterStatus.Text = totalSkipped > 0
            ? $"✓ Done!  {totalImported} imported, {totalSkipped} already existed."
            : $"✓ Done!  {totalImported} questions saved offline.";

        CancelBtn.Content   = "Close";
        CancelBtn.IsEnabled = true;

        // Auto-close after 2 seconds
        await Task.Delay(2000);
        Close();
    }

    // ── Helpers ──────────────────────────────────────────────────────
    private static HttpClient BuildClient(string apiKey)
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        client.DefaultRequestHeaders.Add("User-Agent", "CbtExam");
        if (!string.IsNullOrWhiteSpace(apiKey))
            client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        return client;
    }

    private async Task<List<object>> BuildProcessedListAsync(List<JsonElement> questions, HttpClient client)
    {
        var processed = new List<object>();
        foreach (var q in questions)
        {
            if (!q.TryGetProperty("question",  out var qText)  || string.IsNullOrWhiteSpace(qText.GetString()))  continue;
            if (!q.TryGetProperty("option",    out var optProp))  continue;
            if (!q.TryGetProperty("answer",    out var ansProp)  || string.IsNullOrWhiteSpace(ansProp.GetString())) continue;
            var ans = ansProp.GetString()?.Trim().ToUpper() ?? "";
            if (ans != "A" && ans != "B" && ans != "C" && ans != "D") continue;
            var optA = optProp.TryGetProperty("a", out var pa) ? pa.GetString() : null;
            var optB = optProp.TryGetProperty("b", out var pb) ? pb.GetString() : null;
            if (string.IsNullOrWhiteSpace(optA) || string.IsNullOrWhiteSpace(optB)) continue;

            string imageUrl = string.Empty;
            if (q.TryGetProperty("image", out var imgProp))
            {
                var imgRaw = imgProp.GetString() ?? "";
                if (!string.IsNullOrWhiteSpace(imgRaw) && imgRaw.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var uri       = new Uri(imgRaw);
                        var localName = System.IO.Path.GetFileName(uri.LocalPath);
                        if (string.IsNullOrWhiteSpace(localName)) localName = Guid.NewGuid().ToString("N") + ".jpg";
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

            var dict    = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(q.GetRawText())!;
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
        return processed;
    }

    private void UpdateProgress(int pct, string main, string detail)
    {
        Dispatcher.Invoke(() =>
        {
            ProgressBar.Value   = pct;
            ProgressText.Text   = main;
            ProgressDetail.Text = detail;
        });
    }

    private void ShowFetchError(string msg)
    {
        FetchStatusText.Text       = msg;
        FetchStatusText.Foreground = System.Windows.Media.Brushes.Red;
    }

    private void ResetFetchBtn()
    {
        FetchBtn.IsEnabled = true;
        FetchBtnIcon.Text  = "\uE72D";
        FetchBtnText.Text  = "Fetch Available Subjects";
    }

    private void SetFetchBtnBusy()
    {
        FetchBtn.IsEnabled = false;
        FetchBtnIcon.Text  = "\uE712";
        FetchBtnText.Text  = "Fetching…";
    }

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
                        var idKey = el.TryGetProperty("id",       out var id) ? id.GetInt32().ToString() : "";
                        var yrKey = el.TryGetProperty("examyear", out var yr) ? yr.GetString() ?? "" : "";
                        var key   = $"{idKey}|{yrKey}";
                        if (!string.IsNullOrEmpty(idKey) && seen.Add(key)) results.Add(el);
                    }
                    catch { }
                    start = -1;
                }
            }
        }
        return results;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();
}

// ── DTOs ──────────────────────────────────────────────────────────────────
internal record ProxySubjectDto(string Path, string Name, int Count = 0);

public class SubjectItem : System.ComponentModel.INotifyPropertyChanged
{
    public string Path        { get; }
    public string DisplayName { get; }
    public int    Count       { get; }
    public string CountLabel  => Count > 0 ? $"{Count:N0} Qs" : "—";

    private bool _isSelected = true;
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); }
    }

    public SubjectItem(string path, string displayName, int count = 0)
    {
        Path        = path;
        DisplayName = displayName;
        Count       = count;
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}