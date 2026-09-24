using KT.Downloader.Cli.Bulk;
using KT.Downloader.Cli.Downloading;
using KT.Downloader.Cli.Manifests;
using KT.Downloader.Cli.Progress;

namespace KT.Downloader.WinForms;

public sealed class MainForm : Form
{
    // 底层每写 64 KB 就报一次进度，照单全刷会把 UI 线程淹掉，按时间片节流。
    private static readonly TimeSpan RenderInterval = TimeSpan.FromMilliseconds(100);

    private const int StatusColumn = 2;
    private const int ProgressColumn = 3;

    private readonly HttpClient _httpClient = new();
    private readonly Dictionary<int, DateTime> _lastRendered = [];

    private readonly TextBox _manifestUrl = new() { Text = "http://192.168.0.189:9526/files.json" };
    private readonly TextBox _targetDirectory = new();
    private readonly Button _browse = new() { Text = "浏览...", AutoSize = true };
    private readonly Button _load = new() { Text = "加载清单", AutoSize = true };
    private readonly DataGridView _files = new();
    private readonly Button _start = new() { Text = "开始下载", AutoSize = true };
    private readonly Button _stop = new() { Text = "停止", AutoSize = true };
    private readonly ProgressBar _overall = new() { Maximum = 1 };
    private readonly Label _status = new() { Text = "填好清单地址，选一个下载目录，然后点「加载清单」。", AutoSize = true };

    private IReadOnlyList<RemoteFileEntry> _entries = [];
    private CancellationTokenSource? _cancellation;

    public MainForm()
    {
        Text = "KT 批量下载器";
        MinimumSize = new Size(780, 480);
        Size = new Size(940, 580);
        StartPosition = FormStartPosition.CenterScreen;

        BuildLayout();

        _browse.Click += OnBrowseClick;
        _load.Click += OnLoadClick;
        _start.Click += OnStartClick;
        _stop.Click += OnStopClick;
        FormClosing += (_, _) => _cancellation?.Cancel();

        _targetDirectory.Text = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "ktdl");

        UpdateButtons();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildFileGrid(), 0, 1);
        root.Controls.Add(BuildFooter(), 0, 2);

        Controls.Add(root);
    }

    private Control BuildHeader()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 2,
            AutoSize = true,
            Padding = new Padding(12, 12, 12, 6),
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        header.Controls.Add(FieldLabel("清单地址："), 0, 0);
        header.Controls.Add(Fill(_manifestUrl), 1, 0);
        header.Controls.Add(_load, 2, 0);

        header.Controls.Add(FieldLabel("下载到："), 0, 1);
        header.Controls.Add(Fill(_targetDirectory), 1, 1);
        header.Controls.Add(_browse, 2, 1);

        return header;
    }

    private Control BuildFileGrid()
    {
        _files.Dock = DockStyle.Fill;
        _files.Margin = new Padding(12, 6, 12, 6);
        _files.AllowUserToAddRows = false;
        _files.AllowUserToDeleteRows = false;
        _files.AllowUserToResizeRows = false;
        _files.ReadOnly = true;
        _files.RowHeadersVisible = false;
        _files.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _files.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        // 选中行换成浅底深字，否则默认的蓝底白字会把状态色盖掉
        _files.DefaultCellStyle.SelectionBackColor = Color.FromArgb(213, 232, 252);
        _files.DefaultCellStyle.SelectionForeColor = Color.FromArgb(20, 20, 20);

        _files.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "文件", FillWeight = 62 });
        _files.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "大小", FillWeight = 12 });
        _files.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "状态", FillWeight = 14 });
        _files.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "进度", FillWeight = 12 });

        return _files;
    }

    private Control BuildFooter()
    {
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            AutoSize = true,
            Padding = new Padding(12, 6, 12, 12),
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var actions = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0), WrapContents = false };
        actions.Controls.Add(_start);
        actions.Controls.Add(_stop);

        _overall.Dock = DockStyle.Fill;
        _overall.Margin = new Padding(12, 4, 0, 0);

        footer.Controls.Add(actions, 0, 0);
        footer.Controls.Add(_overall, 1, 0);
        footer.Controls.Add(_status, 0, 1);
        footer.SetColumnSpan(_status, 2);

        return footer;
    }

    private static Label FieldLabel(string text)
        => new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 6, 0) };

    private static Control Fill(Control control)
    {
        control.Dock = DockStyle.Fill;
        return control;
    }

    private async void OnLoadClick(object? sender, EventArgs e)
    {
        if (!Uri.TryCreate(_manifestUrl.Text.Trim(), UriKind.Absolute, out var manifestUrl))
        {
            SetStatus("清单地址不是一个合法的 URL。");
            return;
        }

        SetStatus("正在加载清单...");

        try
        {
            _entries = await new HttpRemoteManifestLoader(_httpClient).LoadAsync(manifestUrl, CancellationToken.None);
            FillGrid(_entries);
            SetStatus($"清单里有 {_entries.Count} 个文件，可以开始了。");
        }
        catch (Exception ex)
        {
            SetStatus($"加载清单失败：{ex.Message}");
        }
        finally
        {
            UpdateButtons();
        }
    }

    private void OnBrowseClick(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择下载目录",
            UseDescriptionForTitle = true,
        };

        if (Directory.Exists(_targetDirectory.Text))
            dialog.SelectedPath = _targetDirectory.Text;

        if (dialog.ShowDialog(this) == DialogResult.OK)
            _targetDirectory.Text = dialog.SelectedPath;
    }

    private async void OnStartClick(object? sender, EventArgs e)
    {
        if (_entries.Count == 0)
        {
            SetStatus("先加载清单。");
            return;
        }

        var target = _targetDirectory.Text.Trim();
        if (string.IsNullOrWhiteSpace(target))
        {
            SetStatus("先选一个下载目录。");
            return;
        }

        _cancellation = new CancellationTokenSource();
        _lastRendered.Clear();
        ResetGrid();
        UpdateButtons();

        var batch = new BatchDownloader(new HttpFileDownloader(_httpClient), new LocalPathMapper(target));

        try
        {
            // 这里的 Progress<T> 是正解：它在 UI 线程上创建，会把 Report 排回 UI 线程。
            // （BatchDownloader 内部刻意避开它，是因为那边需要同步转发，原因不同。）
            var results = await batch.DownloadAsync(
                _entries,
                _cancellation.Token,
                new Progress<BatchProgress>(OnProgress),
                new Progress<BatchItemResult>(OnFileFinished));

            if (IsDisposed)
                return;

            SetStatus(Summarize(results));
        }
        catch (Exception ex)
        {
            SetStatus($"批量下载中断：{ex.Message}");
        }
        finally
        {
            _cancellation.Dispose();
            _cancellation = null;
            UpdateButtons();
        }
    }

    private void OnStopClick(object? sender, EventArgs e)
    {
        if (_cancellation is null)
            return;

        _cancellation.Cancel();
        SetStatus("正在停止...(当前这个文件会保留 .part，下次接着下)");
    }

    private void OnProgress(BatchProgress progress)
    {
        if (IsDisposed || progress.Index >= _files.Rows.Count)
            return;

        // 时间片节流：同一行 100 ms 内只重绘一次。
        if (_lastRendered.TryGetValue(progress.Index, out var lastRendered)
            && DateTime.UtcNow - lastRendered < RenderInterval)
        {
            return;
        }

        _lastRendered[progress.Index] = DateTime.UtcNow;

        // 这里只管「下到哪儿了」。有没有下完交给 OnFileFinished ——
        // 进度回调不保证会走到 100%（节流会丢帧，失败的文件也永远到不了）。
        _files.Rows[progress.Index].Cells[ProgressColumn].Value = DescribeProgress(progress.Download);
        PaintRow(progress.Index, "下载中", RowStatus.Downloading);
    }

    private static string DescribeProgress(DownloadProgress progress)
    {
        if (progress.TotalBytes is not { } total || total <= 0)
            return ProgressLine.FormatBytes(progress.BytesReceived);

        return $"{(double)progress.BytesReceived / total * 100:0.0}%";
    }

    private void FillGrid(IReadOnlyList<RemoteFileEntry> entries)
    {
        _files.Rows.Clear();

        foreach (var entry in entries)
        {
            _files.Rows.Add(
                entry.RelativePath,
                entry.Size is { } size ? ProgressLine.FormatBytes(size) : "未知",
                "等待",
                "—");
        }

        ResetGrid();
    }

    private void ResetGrid()
    {
        for (var index = 0; index < _files.Rows.Count; index++)
        {
            _files.Rows[index].Cells[ProgressColumn].Value = "—";
            PaintRow(index, "等待", RowStatus.Waiting);
        }

        _overall.Value = 0;
    }

    // 每个文件一收工就落定它那一行 —— 不用等整批下完，也不用拿进度去猜。
    private void OnFileFinished(BatchItemResult item)
    {
        if (IsDisposed || item.Index >= _files.Rows.Count)
            return;

        var row = _files.Rows[item.Index];

        row.Cells[ProgressColumn].Value = item.Result is DownloadResult.Completed done
            ? ProgressLine.FormatBytes(done.BytesWritten)
            : "—";

        var (text, status) = Describe(item);
        PaintRow(item.Index, text, status);

        // 总进度按「清单里一共多少个」算，取消时才看得出停在了哪里。
        _overall.Maximum = Math.Max(1, _entries.Count);

        if (item.Result is DownloadResult.Completed)
            _overall.Value = Math.Min(_overall.Value + 1, _overall.Maximum);
    }

    private static (string Text, RowStatus Status) Describe(BatchItemResult item) => item switch
    {
        { Skipped: true } => ("已存在", RowStatus.Skipped),
        { Result: DownloadResult.Completed } => ("完成", RowStatus.Completed),
        { Result: DownloadResult.Canceled } => ("已取消", RowStatus.Canceled),
        { Result: DownloadResult.Failed failed } => ($"失败：{failed.Message}", RowStatus.Failed),
        _ => ("未知", RowStatus.Waiting),
    };

    // 状态和进度两列一起上色：扫一眼就知道哪几行在动、哪几行已经好了、哪几行挂了。
    private void PaintRow(int index, string text, RowStatus status)
    {
        if (IsDisposed || index >= _files.Rows.Count)
            return;

        var color = ColorOf(status);
        var row = _files.Rows[index];

        row.Cells[StatusColumn].Value = text;
        row.Cells[StatusColumn].Style.ForeColor = color;
        row.Cells[ProgressColumn].Style.ForeColor = color;
    }

    private static Color ColorOf(RowStatus status) => status switch
    {
        RowStatus.Downloading => Color.RoyalBlue,
        RowStatus.Completed => Color.ForestGreen,
        RowStatus.Skipped => Color.Teal,
        RowStatus.Failed => Color.Crimson,
        RowStatus.Canceled => Color.DarkOrange,
        _ => Color.DimGray,
    };

    private enum RowStatus
    {
        Waiting,
        Downloading,
        Completed,
        Skipped,
        Failed,
        Canceled,
    }

    private static string Summarize(IReadOnlyList<BatchItemResult> results)
    {
        var downloaded = results.Count(item => !item.Skipped && item.Result is DownloadResult.Completed);
        var skipped = results.Count(item => item.Skipped);
        var failed = results.Count(item => item.Result is DownloadResult.Failed);
        var canceled = results.Count(item => item.Result is DownloadResult.Canceled);

        return $"结束：下载 {downloaded} 个，跳过 {skipped} 个，失败 {failed} 个，取消 {canceled} 个。";
    }

    private void UpdateButtons()
    {
        if (IsDisposed)
            return;

        var running = _cancellation is not null;

        _load.Enabled = !running;
        _start.Enabled = !running && _entries.Count > 0;
        _stop.Enabled = running;
    }

    // 关窗口时后台任务可能还在跑，回调进来时控件已经没了。
    private void SetStatus(string message)
    {
        if (!IsDisposed)
            _status.Text = message;
    }
}
