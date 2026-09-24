using System.Text;
using KT.Downloader.Cli.Manifests;
using KT.Downloader.Cli.Progress;

namespace KT.Downloader.ManifestBuilder;

public sealed class MainForm : Form
{
    private readonly LocalManifestBuilder _builder = new();

    private readonly TextBox _rootDirectory = new();
    private readonly Button _browse = new() { Text = "浏览...", AutoSize = true };
    private readonly TextBox _manifestFileName = new() { Text = LocalManifestBuilder.DefaultFileName };
    private readonly Button _scan = new() { Text = "扫描", AutoSize = true };
    private readonly Button _generate = new() { Text = "生成清单", AutoSize = true };
    private readonly DataGridView _files = new();
    private readonly Label _status = new()
    {
        Text = "选好站点根目录，点「扫描」看看里面有哪些文件。",
        AutoSize = true,
    };

    // 扫描结果和当时的参数捆在一起存。用户改完目录不重扫就点「生成」，
    // 清单就会写进一个跟他看到的预览不一致的地方，所以这里要能对着。
    private (string Root, string FileName, IReadOnlyList<ManifestFileEntry> Entries)? _scanned;
    private bool _scanning;

    public MainForm()
    {
        Text = "KT 清单生成器";
        MinimumSize = new Size(720, 460);
        Size = new Size(880, 560);
        StartPosition = FormStartPosition.CenterScreen;

        BuildLayout();

        _browse.Click += OnBrowseClick;
        _scan.Click += OnScanClick;
        _generate.Click += OnGenerateClick;

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
            RowCount = 3,
            AutoSize = true,
            Padding = new Padding(12, 12, 12, 6),
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        header.Controls.Add(FieldLabel("站点根目录："), 0, 0);
        header.Controls.Add(Fill(_rootDirectory), 1, 0);
        header.Controls.Add(_browse, 2, 0);

        header.Controls.Add(FieldLabel("清单文件名："), 0, 1);
        header.Controls.Add(Fill(_manifestFileName), 1, 1);
        header.Controls.Add(_scan, 2, 1);

        var hint = new Label
        {
            Text = "站点根目录就是 IIS 里那个物理路径（比如 C:\\Uploads\\images）。"
                + "清单会写进这个目录，里面的路径相对它自己算。",
            AutoSize = true,
            ForeColor = Color.DimGray,
        };
        header.Controls.Add(hint, 1, 2);
        header.SetColumnSpan(hint, 2);

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

        _files.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "相对路径", FillWeight = 78 });
        _files.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "大小", FillWeight = 22 });

        return _files;
    }

    private Control BuildFooter()
    {
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            AutoSize = true,
            Padding = new Padding(12, 6, 12, 12),
        };

        var actions = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0), WrapContents = false };
        actions.Controls.Add(_generate);

        footer.Controls.Add(actions, 0, 0);
        footer.Controls.Add(_status, 0, 1);

        return footer;
    }

    private static Label FieldLabel(string text)
        => new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 6, 0) };

    private static Control Fill(Control control)
    {
        control.Dock = DockStyle.Fill;
        return control;
    }

    private async void OnScanClick(object? sender, EventArgs e)
    {
        var root = RootDirectory();
        var fileName = ManifestFileName();

        if (!Directory.Exists(root))
        {
            SetStatus("站点根目录不存在，先选一个。");
            return;
        }

        _scanning = true;
        _scanned = null;
        _files.Rows.Clear();
        UpdateButtons();
        SetStatus("正在扫描...");

        try
        {
            // 目录大起来枚举要花几秒，别把消息循环堵住。
            var entries = await Task.Run(() => _builder.Scan(root, fileName));

            _scanned = (root, fileName, entries);
            FillGrid(entries);

            SetStatus($"扫到 {entries.Count} 个文件，合计 {ProgressLine.FormatBytes(entries.Sum(entry => entry.Size))}。");
        }
        catch (Exception ex)
        {
            SetStatus($"扫描失败：{ex.Message}");
        }
        finally
        {
            _scanning = false;
            UpdateButtons();
        }
    }

    private void OnGenerateClick(object? sender, EventArgs e)
    {
        if (_scanned is not { } scan)
            return;

        if (IsStale(scan))
        {
            SetStatus("目录或文件名改过了，重新点一次「扫描」。");
            return;
        }

        try
        {
            var path = Path.Combine(scan.Root, scan.FileName);

            // 不带 BOM：这份 JSON 是要被 HTTP 原样发出去的，JSON 规范里也没有 BOM 的位置。
            File.WriteAllText(path, LocalManifestBuilder.ToJson(scan.Entries), new UTF8Encoding(false));

            SetStatus($"已写入 {path}，{scan.Entries.Count} 个文件。下载端填这个地址就能拉清单了。");
        }
        catch (Exception ex)
        {
            SetStatus($"生成失败：{ex.Message}");
        }
    }

    private void OnBrowseClick(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择站点根目录（IIS 里的物理路径）",
            UseDescriptionForTitle = true,
        };

        if (Directory.Exists(RootDirectory()))
            dialog.SelectedPath = RootDirectory();

        if (dialog.ShowDialog(this) == DialogResult.OK)
            _rootDirectory.Text = dialog.SelectedPath;
    }

    private void FillGrid(IReadOnlyList<ManifestFileEntry> entries)
    {
        _files.Rows.Clear();

        foreach (var entry in entries)
            _files.Rows.Add(entry.RelativePath, ProgressLine.FormatBytes(entry.Size));
    }

    private string RootDirectory() => _rootDirectory.Text.Trim();

    private string ManifestFileName() => _manifestFileName.Text.Trim();

    // 末尾的分隔符不算改动 —— 手输的时候有没有反斜杠全看运气。
    private bool IsStale((string Root, string FileName, IReadOnlyList<ManifestFileEntry> Entries) scan)
        => !string.Equals(Normalize(scan.Root), Normalize(RootDirectory()), StringComparison.OrdinalIgnoreCase)
            || !string.Equals(scan.FileName, ManifestFileName(), StringComparison.Ordinal);

    private static string Normalize(string path) => path.TrimEnd('\\', '/');

    private void UpdateButtons()
    {
        if (IsDisposed)
            return;

        _browse.Enabled = !_scanning;
        _scan.Enabled = !_scanning;
        _generate.Enabled = !_scanning && _scanned is not null;
    }

    private void SetStatus(string message)
    {
        if (!IsDisposed)
            _status.Text = message;
    }
}
