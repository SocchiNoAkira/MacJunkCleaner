using System.Diagnostics;
using MacJunkCleaner.Core;

namespace MacJunkCleaner.App;

internal sealed class MainForm : Form
{
    private static readonly Color PageBackground = Color.FromArgb(244, 245, 247);
    private static readonly Color CardBackground = Color.White;
    private static readonly Color LineColor = Color.FromArgb(223, 226, 231);
    private static readonly Color PrimaryColor = Color.FromArgb(37, 99, 235);
    private static readonly Color DangerColor = Color.FromArgb(200, 38, 38);
    private static readonly Color DisabledFill = Color.FromArgb(203, 213, 225);
    private static readonly Color TextPrimary = Color.FromArgb(28, 32, 38);
    private static readonly Color TextSecondary = Color.FromArgb(108, 116, 127);
    private static readonly Color AlternateRow = Color.FromArgb(250, 251, 252);

    private const int ButtonHeight = 30;
    private const int NarrowButtonWidth = 96;
    private const int WideButtonWidth = 112;

    private readonly TextBox _pathBox = new();
    private readonly Button _browseButton;
    private readonly Button _scanButton;
    private readonly Button _cancelButton;
    private readonly CheckBox _verifyHeaderCheck = new();
    private readonly CheckBox _showSkippedCheck = new();
    private readonly FlowLayoutPanel _kindPanel = new();
    private readonly Dictionary<JunkKind, CheckBox> _kindChecks = new();
    private readonly Label _summaryLabel = new();
    private readonly ListView _listView = new();
    private readonly ProgressBar _progressBar = new();
    private readonly Label _statusLabel = new();
    private readonly Button _deleteButton;
    private readonly Button _exportButton;
    private readonly ToolTip _toolTip = new();

    /// <summary>截图模式下使用：让窗口显示时不抢焦点。</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool SuppressActivation { get; set; }

    protected override bool ShowWithoutActivation => SuppressActivation;

    private List<ScanItem> _allItems = new();
    private List<ScanItem> _visibleItems = new();
    private CancellationTokenSource? _cts;
    private CleanupLogger? _logger;
    private ScanOutcome? _outcome;
    private DeleteReport? _lastReport;
    private bool _busy;

    public MainForm()
    {
        Text = "macOS 残留文件清理工具";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1200, 780);
        MinimumSize = new Size(960, 640);
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = PageBackground;
        AllowDrop = true;

        _browseButton = CreateButton("浏览…", NarrowButtonWidth);
        _scanButton = CreateButton("开始扫描", WideButtonWidth, PrimaryColor);
        _cancelButton = CreateButton("取消", NarrowButtonWidth);
        _exportButton = CreateButton("导出报告", WideButtonWidth);
        _deleteButton = CreateButton("永久删除", WideButtonWidth, DangerColor);

        _browseButton.Click += (_, _) => BrowseFolder();
        _scanButton.Click += async (_, _) => await ScanAsync();
        _cancelButton.Click += (_, _) => _cts?.Cancel();
        _exportButton.Click += (_, _) => ExportReport();
        _deleteButton.Click += async (_, _) => await DeleteAsync();

        BuildLayout();

        _scanButton.Enabled = false;
        AcceptButton = _scanButton;
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;
        FormClosing += (_, _) => _logger?.Dispose();

        UpdateSummary();
    }

    #region 布局

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = PageBackground,
            Padding = new Padding(14),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(BuildPathCard(), 0, 0);
        root.Controls.Add(BuildScopeCard(), 0, 1);
        root.Controls.Add(BuildPreviewCard(), 0, 2);
        root.Controls.Add(BuildFooter(), 0, 3);

        Controls.Add(root);
    }

    /// <summary>
    /// 卡片直接用 TableLayoutPanel 承载内容：Panel.AutoSize 的高度会残留旧值，
    /// 而 TableLayoutPanel 的 AutoSize 是按行内容精确计算的。
    /// </summary>
    private static TableLayoutPanel CreateCard(int rows, bool fill = false)
    {
        var card = new TableLayoutPanel
        {
            BackColor = CardBackground,
            Padding = new Padding(14, 12, 14, 12),
            Margin = new Padding(0, 0, 0, 10),
            ColumnCount = 1,
            RowCount = rows,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
        };

        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        for (var i = 0; i < rows; i++) card.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        if (fill)
        {
            card.Anchor = AnchorStyles.None;
            card.AutoSize = false;
            card.Dock = DockStyle.Fill;
            card.RowStyles[rows - 1] = new RowStyle(SizeType.Percent, 100F);
        }

        card.Paint += (_, e) =>
        {
            ControlPaint.DrawBorder(e.Graphics, card.ClientRectangle, LineColor, ButtonBorderStyle.Solid);
        };

        return card;
    }

    private static Button CreateButton(string text, int width, Color? fill = null)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = false,
            Size = new Size(width, ButtonHeight),
            Margin = new Padding(8, 0, 0, 0),
            Anchor = AnchorStyles.None,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
            TabStop = true,
        };

        if (fill is { } background)
        {
            button.BackColor = background;
            button.ForeColor = Color.White;
            button.FlatAppearance.BorderSize = 0;
            button.EnabledChanged += (_, _) =>
            {
                button.BackColor = button.Enabled ? background : DisabledFill;
                button.ForeColor = button.Enabled ? Color.White : Color.FromArgb(250, 250, 250);
            };
        }
        else
        {
            button.BackColor = Color.White;
            button.ForeColor = TextPrimary;
            button.FlatAppearance.BorderColor = LineColor;
            button.FlatAppearance.BorderSize = 1;
        }

        return button;
    }

    private static Label CreateSectionTitle(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = TextSecondary,
        Margin = new Padding(0, 0, 0, 8),
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
    };

    private static Label CreateFieldLabel(string text, int width) => new()
    {
        Text = text,
        AutoSize = false,
        Width = width,
        Height = 22,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = TextSecondary,
        Margin = new Padding(0, 2, 10, 2),
    };

    private Control BuildPathCard()
    {
        var card = CreateCard(2);

        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 3,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0),
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _pathBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _pathBox.Margin = new Padding(0, 0, 4, 0);
        _pathBox.PlaceholderText = "选择或拖入要清理的目录，例如 D:\\从Mac拷贝的资料";
        _pathBox.AllowDrop = true;
        _pathBox.DragEnter += OnDragEnter;
        _pathBox.DragDrop += OnDragDrop;
        _pathBox.TextChanged += (_, _) => _scanButton.Enabled = !_busy && _pathBox.Text.Trim().Length > 0;
        _pathBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter || !_scanButton.Enabled) return;
            e.SuppressKeyPress = true;
            _scanButton.PerformClick();
        };

        row.Controls.Add(_pathBox, 0, 0);
        row.Controls.Add(_browseButton, 1, 0);
        row.Controls.Add(_scanButton, 2, 0);

        card.Controls.Add(CreateSectionTitle("目标目录"), 0, 0);
        card.Controls.Add(row, 0, 1);
        return card;
    }

    private Control BuildScopeCard()
    {
        var card = CreateCard(2);

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0),
        };
        // 注意：AutoSize 容器里不能用 Percent 列，否则 PreferredSize 会把内容按宽度 0 估算，
        // 导致整张卡片虚高上百像素、把底部状态栏挤出窗口。
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _verifyHeaderCheck.Text = "校验文件头魔数（推荐）";
        _verifyHeaderCheck.Checked = true;
        _verifyHeaderCheck.AutoSize = true;
        _verifyHeaderCheck.Margin = new Padding(0, 2, 22, 2);
        _toolTip.SetToolTip(_verifyHeaderCheck,
            "对 .DS_Store 与 ._* 读取文件头确认身份。\n关闭后按文件名直接判定，速度略快，\n但可能误删你自己命名的同名文件。");

        _showSkippedCheck.Text = "显示被跳过的条目";
        _showSkippedCheck.Checked = false;
        _showSkippedCheck.AutoSize = true;
        _showSkippedCheck.Margin = new Padding(0, 2, 22, 2);
        _showSkippedCheck.CheckedChanged += (_, _) => RefreshView();
        _toolTip.SetToolTip(_showSkippedCheck, "列出\"名字像但内容不是\"的文件，便于你复核是否误判。");

        // WrapContents 必须为 false：AutoSize 父容器计算 preferred size 时会假设宽度为 0，
        // 允许换行会让高度被估成"每个选项各占一行"，卡片凭空高出上百像素。
        var safeFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = new Padding(0),
        };
        safeFlow.Controls.Add(_verifyHeaderCheck);
        safeFlow.Controls.Add(_showSkippedCheck);

        _kindPanel.Dock = DockStyle.Top;
        _kindPanel.AutoSize = true;
        _kindPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _kindPanel.WrapContents = false;
        _kindPanel.Margin = new Padding(0);

        foreach (var kind in JunkKindInfo.All)
        {
            var check = new CheckBox
            {
                Text = JunkKindInfo.Display(kind),
                Checked = true,
                AutoSize = true,
                Margin = new Padding(0, 2, 22, 2),
            };
            _toolTip.SetToolTip(check, JunkKindInfo.Hint(kind));

            if (kind == JunkKind.MacTrash)
            {
                check.ForeColor = Color.FromArgb(178, 88, 0);
                check.Text += "（可能含真实文件）";
            }

            check.CheckedChanged += (_, _) => RefreshView();
            _kindChecks[kind] = check;
            _kindPanel.Controls.Add(check);
        }

        grid.Controls.Add(CreateFieldLabel("安全选项", 64), 0, 0);
        grid.Controls.Add(safeFlow, 1, 0);
        grid.Controls.Add(CreateFieldLabel("清理范围", 64), 0, 1);
        grid.Controls.Add(_kindPanel, 1, 1);

        card.Controls.Add(CreateSectionTitle("清理设置"), 0, 0);
        card.Controls.Add(grid, 0, 1);
        return card;
    }

    private Control BuildPreviewCard()
    {
        var card = CreateCard(2, fill: true);

        _summaryLabel.AutoSize = true;
        _summaryLabel.Dock = DockStyle.Top;
        _summaryLabel.ForeColor = TextPrimary;
        _summaryLabel.Padding = new Padding(0, 0, 0, 10);
        _summaryLabel.Text = "尚未扫描。";

        _listView.Dock = DockStyle.Fill;
        _listView.View = View.Details;
        _listView.FullRowSelect = true;
        _listView.GridLines = false;
        _listView.HideSelection = false;
        _listView.MultiSelect = true;
        _listView.VirtualMode = true;
        _listView.BorderStyle = BorderStyle.FixedSingle;
        _listView.Margin = new Padding(0);
        _listView.RetrieveVirtualItem += OnRetrieveVirtualItem;
        _listView.Resize += (_, _) => FitLastColumn();
        _listView.DoubleClick += OnItemDoubleClick;

        _listView.Columns.Add("动作", 76);
        _listView.Columns.Add("类型", 156);
        _listView.Columns.Add("大小", 90, HorizontalAlignment.Right);
        _listView.Columns.Add("内含文件", 84, HorizontalAlignment.Right);
        _listView.Columns.Add("完整路径", 700);

        card.Controls.Add(_summaryLabel, 0, 0);
        card.Controls.Add(_listView, 0, 1);
        return card;
    }

    private Control BuildFooter()
    {
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 4,
            RowCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(2, 0, 2, 0),
            Padding = new Padding(0),
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
        footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _progressBar.Dock = DockStyle.Fill;
        _progressBar.Margin = new Padding(0, 6, 0, 6);
        _progressBar.Maximum = 100;

        _statusLabel.AutoSize = false;
        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.AutoEllipsis = true;
        _statusLabel.ForeColor = TextSecondary;
        _statusLabel.Margin = new Padding(0, 0, 12, 0);
        _statusLabel.Text = "就绪";

        _exportButton.Enabled = false;
        _cancelButton.Enabled = false;
        _deleteButton.Enabled = false;

        footer.Controls.Add(_progressBar, 0, 0);
        footer.SetColumnSpan(_progressBar, 4);
        footer.Controls.Add(_statusLabel, 0, 1);
        footer.Controls.Add(_exportButton, 1, 1);
        footer.Controls.Add(_cancelButton, 2, 1);
        footer.Controls.Add(_deleteButton, 3, 1);
        return footer;
    }

    private void FitLastColumn()
    {
        if (_listView.Columns.Count < 5) return;

        var used = 0;
        for (var i = 0; i < 4; i++) used += _listView.Columns[i].Width;
        var available = _listView.ClientSize.Width - used - 4;
        _listView.Columns[4].Width = Math.Max(240, available);
    }

    #endregion

    #region 交互

    private void BrowseFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择要清理 macOS 残留文件的目录",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
        };

        if (Directory.Exists(_pathBox.Text.Trim().Trim('"')))
            dialog.SelectedPath = _pathBox.Text.Trim().Trim('"');

        if (dialog.ShowDialog(this) == DialogResult.OK)
            _pathBox.Text = dialog.SelectedPath;
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (_busy) return;
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
            e.Effect = DragDropEffects.Copy;
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (_busy) return;
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;

        var path = paths[0];
        if (File.Exists(path)) path = Path.GetDirectoryName(path) ?? path;
        _pathBox.Text = path;
    }

    private void OnRetrieveVirtualItem(object? sender, RetrieveVirtualItemEventArgs e)
    {
        if (e.ItemIndex < 0 || e.ItemIndex >= _visibleItems.Count)
        {
            e.Item = new ListViewItem(string.Empty);
            return;
        }

        var source = _visibleItems[e.ItemIndex];
        var row = new ListViewItem(source.IsDeletable ? "将删除" : "跳过");
        row.SubItems.Add(JunkKindInfo.Display(source.Kind));
        row.SubItems.Add(ReportWriter.FormatSize(source.SizeBytes));
        row.SubItems.Add(source.IsDirectory ? source.FileCount.ToString("N0") : "—");
        row.SubItems.Add(source.FullPath);
        row.BackColor = e.ItemIndex % 2 == 0 ? Color.White : AlternateRow;

        if (!source.IsDeletable)
            row.ForeColor = Color.FromArgb(150, 156, 164);

        e.Item = row;
    }

    private void OnItemDoubleClick(object? sender, EventArgs e)
    {
        if (_listView.SelectedIndices.Count == 0) return;
        var index = _listView.SelectedIndices[0];
        if (index < 0 || index >= _visibleItems.Count) return;

        var path = _visibleItems[index].FullPath;
        try
        {
            var target = File.Exists(path) || Directory.Exists(path) ? path : Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(target))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{target}\"") { UseShellExecute = true });
        }
        catch
        {
            // 资源管理器调用失败不影响主流程
        }
    }

    #endregion

    #region 扫描与删除

    private CleanupOptions BuildOptions() => new()
    {
        VerifyFileHeaders = _verifyHeaderCheck.Checked,
        MaxDegreeOfParallelism = 4,
    };

    private HashSet<JunkKind> EnabledKinds()
        => _kindChecks.Where(p => p.Value.Checked).Select(p => p.Key).ToHashSet();

    private async Task ScanAsync()
    {
        var raw = _pathBox.Text.Trim().Trim('"');
        var validation = PathGuard.Validate(raw);
        if (!validation.CanProceed)
        {
            MessageBox.Show(validation.Message ?? "路径无效。", "无法扫描", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (validation.Risk == RootRisk.Warn)
        {
            var proceed = MessageBox.Show(
                $"{validation.Message}\n\n确定继续扫描吗？",
                "请确认扫描范围",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (proceed != DialogResult.Yes) return;
        }

        var root = Path.GetFullPath(raw);
        SetBusy(true);
        _allItems = new List<ScanItem>();
        _visibleItems = new List<ScanItem>();
        _outcome = null;
        _lastReport = null;
        RefreshView();

        _logger ??= CleanupLogger.TryCreate();
        _logger?.Info($"开始扫描 {root}");

        _progressBar.Style = ProgressBarStyle.Marquee;
        _statusLabel.Text = "正在扫描…";
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var options = BuildOptions();

        var progress = new Progress<ScanProgress>(p =>
            _statusLabel.Text = $"正在扫描：已访问 {p.FoldersVisited:N0} 个目录，命中 {p.CandidatesFound:N0} 项 · {p.CurrentDirectory}");

        try
        {
            var scanner = new DirectoryScanner();
            var outcome = await Task.Run(() => scanner.Scan(root, options, progress, token), token);

            _outcome = outcome;
            _allItems = outcome.Items.ToList();
            _logger?.Info($"扫描完成：{outcome.FoldersVisited} 个目录，命中 {outcome.Items.Count} 项，异常 {outcome.ErrorCount} 个");
            RefreshView();

            _statusLabel.Text = outcome.Items.Count == 0
                ? $"扫描完成：{outcome.FoldersVisited:N0} 个目录，未发现 macOS 残留文件（耗时 {outcome.Duration.TotalSeconds:0.##} 秒）"
                : $"扫描完成：{outcome.FoldersVisited:N0} 个目录，命中 {outcome.Items.Count:N0} 项（耗时 {outcome.Duration.TotalSeconds:0.##} 秒）"
                  + (outcome.ErrorCount > 0 ? $"，{outcome.ErrorCount:N0} 个目录无权限已跳过" : string.Empty);
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = "扫描已取消";
        }
        catch (Exception ex)
        {
            _logger?.Error($"扫描失败：{ex}");
            MessageBox.Show($"扫描失败：\n{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _progressBar.Style = ProgressBarStyle.Blocks;
            _progressBar.Value = 0;
            SetBusy(false);
            _cts?.Dispose();
            _cts = null;
        }
    }

    private async Task DeleteAsync()
    {
        var enabled = EnabledKinds();
        var targets = _allItems.Where(i => i.IsDeletable && enabled.Contains(i.Kind)).ToList();
        if (targets.Count == 0) return;

        var fileCount = targets.Count(t => !t.IsDirectory);
        var dirCount = targets.Count(t => t.IsDirectory);
        var totalSize = targets.Sum(t => t.SizeBytes);

        var confirm = MessageBox.Show(
            $"即将永久删除以下内容（不进回收站，无法恢复）：\n\n" +
            $"目录：{_pathBox.Text.Trim()}\n" +
            $"普通文件 {fileCount:N0} 个，元数据目录 {dirCount:N0} 个\n" +
            $"合计占用 {ReportWriter.FormatSize(totalSize)}\n\n" +
            "确定要永久删除吗？",
            "确认永久删除",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (confirm != DialogResult.Yes) return;

        SetBusy(true);
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var logger = _logger ??= CleanupLogger.TryCreate();
        var options = BuildOptions();

        logger?.Info($"开始删除：文件 {fileCount}，目录 {dirCount}，合计 {ReportWriter.FormatSize(totalSize)}");

        _progressBar.Style = ProgressBarStyle.Blocks;
        _progressBar.Maximum = Math.Max(1, targets.Count);
        _progressBar.Value = 0;

        var progress = new Progress<DeleteProgress>(p =>
        {
            _progressBar.Value = Math.Clamp(p.Completed, 0, _progressBar.Maximum);
            _statusLabel.Text = $"正在删除 {p.Completed:N0}/{p.Total:N0}…";
        });

        try
        {
            var engine = new DeleteEngine();
            var report = await Task.Run(() => engine.Execute(targets, options, progress, logger, token), token);
            _lastReport = report;

            var removed = await Task.Run(
                () => targets.Where(t => !File.Exists(t.FullPath) && !Directory.Exists(t.FullPath)).ToList(),
                CancellationToken.None);

            var removedPaths = new HashSet<string>(removed.Select(t => t.FullPath), StringComparer.OrdinalIgnoreCase);
            _allItems = _allItems.Where(i => !removedPaths.Contains(i.FullPath)).ToList();
            RefreshView();

            _statusLabel.Text =
                $"删除完成：文件 {report.DeletedFiles:N0} 个，目录 {report.DeletedDirectories:N0} 个，释放 {ReportWriter.FormatSize(report.FreedBytes)}"
                + (report.FailedCount > 0 ? $"，失败 {report.FailedCount:N0} 项" : string.Empty);

            MessageBox.Show(
                $"清理完成\n\n删除文件：{report.DeletedFiles:N0}\n删除目录：{report.DeletedDirectories:N0}\n释放空间：{ReportWriter.FormatSize(report.FreedBytes)}\n耗时：{report.Duration.TotalSeconds:0.##} 秒"
                + (report.FailedCount > 0 ? $"\n失败：{report.FailedCount:N0} 项（详见日志）" : string.Empty)
                + $"\n\n日志：{logger?.FilePath}",
                "清理完成",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = "删除已取消";
        }
        catch (Exception ex)
        {
            logger?.Error($"删除失败：{ex}");
            MessageBox.Show($"删除过程出错：\n{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _progressBar.Value = 0;
            SetBusy(false);
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void ExportReport()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "保存清理报告",
            Filter = "文本文件 (*.txt)|*.txt",
            FileName = $"mac清理报告-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var text = ReportWriter.BuildText(
                _outcome?.RootPath ?? _pathBox.Text.Trim(),
                _allItems,
                _lastReport,
                _logger?.FilePath,
                _outcome?.ErrorCount ?? 0);

            File.WriteAllText(dialog.FileName, text);
            MessageBox.Show($"报告已保存：\n{dialog.FileName}", "导出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导出失败：\n{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    #endregion

    #region 状态刷新

    private void RefreshView()
    {
        var enabled = EnabledKinds();
        var showSkipped = _showSkippedCheck.Checked;

        _visibleItems = _allItems
            .Where(i => enabled.Contains(i.Kind))
            .Where(i => showSkipped || i.IsDeletable)
            .OrderBy(i => (int)i.Kind)
            .ThenBy(i => i.FullPath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _listView.VirtualListSize = 0;
        _listView.VirtualListSize = _visibleItems.Count;
        _listView.Invalidate();

        UpdateSummary();
    }

    private void UpdateSummary()
    {
        if (_allItems.Count == 0)
        {
            _summaryLabel.Text = _outcome is null ? "尚未扫描。" : "未发现 macOS 残留文件，目录很干净。";
            _deleteButton.Enabled = false;
            _exportButton.Enabled = false;
            return;
        }

        var enabled = EnabledKinds();
        var targets = _allItems.Where(i => i.IsDeletable && enabled.Contains(i.Kind)).ToList();
        var fileCount = targets.Count(t => !t.IsDirectory);
        var dirCount = targets.Count(t => t.IsDirectory);
        var size = targets.Sum(t => t.SizeBytes);
        var skipped = _allItems.Count(i => !i.IsDeletable);

        var parts = new List<string>
        {
            $"待删除 文件 {fileCount:N0} 个 + 目录 {dirCount:N0} 个，合计 {ReportWriter.FormatSize(size)}",
        };

        var detail = JunkKindInfo.All
            .Select(kind => new { kind, count = targets.Count(t => t.Kind == kind) })
            .Where(x => x.count > 0)
            .Select(x => $"{JunkKindInfo.Display(x.kind)} {x.count:N0}")
            .ToList();

        if (detail.Count > 0) parts.Add(string.Join(" · ", detail));
        if (skipped > 0) parts.Add($"跳过 {skipped:N0} 项");

        _summaryLabel.Text = string.Join("　|　", parts);
        _deleteButton.Enabled = !_busy && targets.Count > 0;
        _exportButton.Enabled = !_busy && _allItems.Count > 0;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _browseButton.Enabled = !busy;
        _scanButton.Enabled = !busy && _pathBox.Text.Trim().Length > 0;
        _cancelButton.Enabled = busy;
        _pathBox.ReadOnly = busy;
        _verifyHeaderCheck.Enabled = !busy;
        _showSkippedCheck.Enabled = !busy;
        foreach (var check in _kindChecks.Values) check.Enabled = !busy;

        UpdateSummary();
    }

    #endregion
}
