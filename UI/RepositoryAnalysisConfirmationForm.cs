namespace BaChenAiLauncher;

internal sealed class RepositoryAnalysisConfirmationForm : Form
{
    private readonly ComboBox _launchOption = new();
    private readonly TextBox _arguments = new();
    private readonly GitHubRepositoryAnalysis _analysis;

    public RepositoryLaunchOption SelectedLaunchOption => (RepositoryLaunchOption)_launchOption.SelectedItem!;

    public RepositoryAnalysisConfirmationForm(
        GitHubRepositoryAnalysis analysis,
        string repository,
        string branch,
        string installDirectory,
        int port,
        bool useEnglish)
    {
        _analysis = analysis;
        Text = useEnglish ? "Confirm automatic configuration" : "确认自动配置";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(860, 760);
        MinimumSize = new Size(780, 700);
        Font = new Font("Microsoft YaHei UI", 10F);
        BackColor = Color.FromArgb(242, 246, 244);
        AutoScaleMode = AutoScaleMode.Dpi;

        var header = new Panel { Dock = DockStyle.Top, Height = 92, BackColor = Color.FromArgb(21, 77, 69) };
        header.Controls.Add(new Label
        {
            Text = useEnglish ? "REPOSITORY ANALYSIS" : "仓库分析结果",
            ForeColor = Color.White,
            Font = new Font(Font, FontStyle.Bold),
            Location = new Point(26, 18),
            AutoSize = true
        });
        header.Controls.Add(new Label
        {
            Text = useEnglish ? "Review once, then the launcher installs and configures the plugin." : "确认一次后，启动器将自动安装环境并配置插件。",
            ForeColor = Color.FromArgb(190, 226, 218),
            Location = new Point(26, 50),
            AutoSize = true
        });
        Controls.Add(header);

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24, 18, 24, 12),
            ColumnCount = 2,
            RowCount = 11,
            BackColor = BackColor
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        AddValue(table, 0, useEnglish ? "Project" : "项目", $"{analysis.DisplayName}  ({repository})");
        AddValue(table, 1, useEnglish ? "Branch / version" : "分支 / 版本", branch);
        AddValue(table, 2, useEnglish ? "Install directory" : "安装目录", installDirectory, true);
        AddValue(table, 3, useEnglish ? "Category" : "分类", analysis.Category);
        AddValue(table, 4, useEnglish ? "Runtime" : "运行环境", $"{analysis.Runtime} {analysis.RuntimeVersion} / {analysis.EnvironmentManager}");
        AddValue(table, 5, useEnglish ? "Executable" : "启动程序", analysis.Executable);

        AddLabel(table, 6, useEnglish ? "Launch option" : "启动入口");
        _launchOption.Dock = DockStyle.Fill;
        _launchOption.DropDownStyle = ComboBoxStyle.DropDownList;
        _launchOption.Margin = new Padding(6);
        _launchOption.DisplayMember = nameof(RepositoryLaunchOption.DisplayName);
        _launchOption.Items.AddRange(analysis.LaunchOptions.Cast<object>().ToArray());
        _launchOption.SelectedIndex = Math.Max(0, Array.FindIndex(analysis.LaunchOptions, option => option.IsRecommended));
        _launchOption.SelectedIndexChanged += (_, _) => RefreshArguments();
        table.Controls.Add(_launchOption, 1, 6);

        AddLabel(table, 7, useEnglish ? "Arguments" : "启动参数");
        _arguments.Dock = DockStyle.Fill;
        _arguments.ReadOnly = true;
        _arguments.Multiline = true;
        _arguments.WordWrap = true;
        _arguments.Margin = new Padding(6);
        table.Controls.Add(_arguments, 1, 7);
        AddValue(table, 8, useEnglish ? "Port / resources" : "端口 / 资源", $"{port}  |  VRAM {FormatGiB(analysis.RecommendedVramMiB)}  |  RAM {FormatGiB(analysis.RecommendedSystemMemoryMiB)}");
        AddValue(table, 9, useEnglish ? "Description" : "说明", analysis.Description, true);

        AddLabel(table, 10, useEnglish ? "Analysis notes" : "分析说明");
        var notes = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Margin = new Padding(6),
            Text = string.Join(Environment.NewLine, analysis.Notes.Select(note => "- " + LocalizeNote(note, useEnglish))) + Environment.NewLine +
                (useEnglish ? $"Confidence: {analysis.Confidence}" : $"识别置信度：{LocalizeConfidence(analysis.Confidence)}")
        };
        table.Controls.Add(notes, 1, 10);
        Controls.Add(table);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 66,
            BackColor = Color.White,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 14, 18, 0)
        };
        var cancel = new Button
        {
            Text = useEnglish ? "Cancel" : "取消",
            DialogResult = DialogResult.Cancel,
            Size = new Size(110, 38),
            Margin = new Padding(8, 0, 0, 0)
        };
        var confirm = new Button
        {
            Text = useEnglish ? "Confirm and install" : "确认并安装",
            DialogResult = DialogResult.OK,
            Size = new Size(134, 38),
            Margin = new Padding(8, 0, 0, 0),
            BackColor = Color.FromArgb(34, 124, 105),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        footer.Controls.Add(confirm);
        footer.Controls.Add(cancel);
        Controls.Add(footer);
        AcceptButton = confirm;
        CancelButton = cancel;
        RefreshArguments();
    }

    private void RefreshArguments()
    {
        if (_launchOption.SelectedItem is RepositoryLaunchOption option) _arguments.Text = option.Arguments;
    }

    private static void AddLabel(TableLayoutPanel table, int row, string text)
        => table.Controls.Add(new Label { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(66, 83, 78) }, 0, row);

    private static void AddValue(TableLayoutPanel table, int row, string label, string value, bool multiline = false)
    {
        AddLabel(table, row, label);
        table.Controls.Add(new TextBox
        {
            Text = value,
            Dock = DockStyle.Fill,
            ReadOnly = true,
            Multiline = multiline,
            Margin = new Padding(6),
            BackColor = Color.White
        }, 1, row);
    }

    private static string FormatGiB(int mib) => mib <= 0 ? "N/A" : $"{mib / 1024d:0.#} GiB";

    private static string LocalizeConfidence(string confidence)
        => confidence.Equals("High", StringComparison.OrdinalIgnoreCase) ? "高" : confidence.Equals("Medium", StringComparison.OrdinalIgnoreCase) ? "中" : "低";

    private static string LocalizeNote(string note, bool useEnglish)
    {
        if (useEnglish) return note;
        var launchMatch = System.Text.RegularExpressions.Regex.Match(note, @"^Detected (\d+) launch option\(s\)\.$");
        if (launchMatch.Success) return $"检测到 {launchMatch.Groups[1].Value} 个启动入口。";
        if (note.StartsWith("Detected uv project; selected CUDA", StringComparison.Ordinal)) return "检测到 uv 项目，已选择 CUDA 依赖方案。";
        if (note.StartsWith("Detected uv project; selected CPU", StringComparison.Ordinal)) return "检测到 uv 项目，已选择 CPU 依赖方案。";
        if (note.Equals("Detected managed Python environment.", StringComparison.Ordinal)) return "检测到托管 Python 环境。";
        if (note.StartsWith("The repository mentions Hugging Face", StringComparison.Ordinal)) return "仓库提到了 Hugging Face；安装后可能需要模型访问权限或令牌。";
        if (note.StartsWith("The repository references external model weights", StringComparison.Ordinal)) return "仓库引用了外部模型权重；首次启动前需要检查下载或授权步骤。";
        return note;
    }
}
