namespace BaChenAiLauncher;

internal sealed record FirstRunInstallOutcome(LauncherModelDefinition Definition, IReadOnlyList<DependencyCheckResult> Checks);

internal sealed class FirstRunWizardForm : Form
{
    private readonly LauncherSettings _settings;
    private readonly IReadOnlyList<PluginPackageManifest> _plugins;
    private readonly bool _useEnglish;
    private readonly Func<string, string[], int, Task> _persistState;
    private readonly Func<PluginPackageManifest, IProgress<string>, IProgress<PluginDownloadProgress>, CancellationToken, Task<FirstRunInstallOutcome>> _install;
    private readonly Panel _content = new() { BackColor = Color.White };
    private readonly Label _stepLabel = new();
    private readonly Button _back = new();
    private readonly Button _next = new();
    private readonly Button _cancel = new();
    private readonly List<FirstRunInstallOutcome> _outcomes = [];
    private TextBox? _storagePath;
    private CheckedListBox? _pluginList;
    private CheckBox? _licenseAccepted;
    private ProgressBar? _overallProgress;
    private ProgressBar? _currentProgress;
    private Label? _overallStatus;
    private Label? _progressStatus;
    private TextBox? _progressHistory;
    private CancellationTokenSource? _installationCancellation;
    private int _step;
    private bool _installing;

    public FirstRunWizardForm(
        LauncherSettings settings,
        IReadOnlyList<PluginPackageManifest> plugins,
        bool useEnglish,
        Func<string, string[], int, Task> persistState,
        Func<PluginPackageManifest, IProgress<string>, IProgress<PluginDownloadProgress>, CancellationToken, Task<FirstRunInstallOutcome>> install)
    {
        _settings = settings;
        _plugins = plugins;
        _useEnglish = useEnglish;
        _persistState = persistState;
        _install = install;
        _step = Math.Clamp(settings.FirstRunWizardStep, 0, 4);
        Text = T("BaChen AI Launcher 首次运行设置", "BaChen AI Launcher first-run setup");
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(940, 650);
        MinimumSize = new Size(820, 600);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.FromArgb(244, 249, 247);
        Font = new Font("Microsoft YaHei UI", 10F);

        var header = new Panel { Dock = DockStyle.Top, Height = 92, BackColor = Theme.DeepTeal };
        header.Controls.Add(new Label { Text = "BaChen AI Launcher", ForeColor = Color.White, Font = new Font("Microsoft YaHei UI", 20F, FontStyle.Bold), Location = new Point(30, 18), Size = new Size(500, 38) });
        _stepLabel.ForeColor = Color.FromArgb(191, 225, 218);
        _stepLabel.Location = new Point(32, 58);
        _stepLabel.Size = new Size(700, 24);
        header.Controls.Add(_stepLabel);

        _content.Dock = DockStyle.Fill;
        _content.Padding = new Padding(34);
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 78, BackColor = Color.FromArgb(235, 243, 240) };
        _cancel.Text = T("稍后设置", "Set up later");
        _cancel.Location = new Point(30, 20);
        _cancel.Size = new Size(120, 38);
        _cancel.Click += (_, _) => Close();
        _back.Text = T("上一步", "Back");
        _back.Location = new Point(660, 20);
        _back.Size = new Size(110, 38);
        _back.Click += async (_, _) => await MoveBackAsync();
        _next.Location = new Point(782, 20);
        _next.Size = new Size(126, 38);
        _next.Click += async (_, _) => await MoveNextAsync();
        footer.Controls.AddRange([_cancel, _back, _next]);
        footer.Resize += (_, _) =>
        {
            _next.Left = footer.ClientSize.Width - _next.Width - 30;
            _back.Left = _next.Left - _back.Width - 12;
        };

        Controls.Add(_content);
        Controls.Add(footer);
        Controls.Add(header);
        FormClosing += (_, eventArgs) =>
        {
            if (_installing)
            {
                _installationCancellation?.Cancel();
                eventArgs.Cancel = true;
            }
        };
        RenderStep();
    }

    private void RenderStep()
    {
        _content.Controls.Clear();
        _storagePath = null;
        _pluginList = null;
        _licenseAccepted = null;
        _overallProgress = null;
        _currentProgress = null;
        _overallStatus = null;
        _progressStatus = null;
        _progressHistory = null;
        string[] names =
        [
            T("存储位置", "Storage"),
            T("硬件检查", "Hardware"),
            T("选择插件", "Choose plugins"),
            T("许可与确认", "Licenses and review"),
            T("下载与部署", "Download and deploy"),
            T("完成验证", "Verification complete")
        ];
        _stepLabel.Text = $"{T("步骤", "Step")} {_step + 1} / 6    {names[_step]}";
        _back.Enabled = _step > 0 && !_installing;
        _cancel.Enabled = !_installing;
        _next.Text = _step switch
        {
            4 => T("开始安装", "Install"),
            5 => T("完成", "Finish"),
            _ => T("下一步", "Next")
        };
        _next.Enabled = !_installing;
        switch (_step)
        {
            case 0: RenderStorage(); break;
            case 1: RenderHardware(); break;
            case 2: RenderPluginSelection(); break;
            case 3: RenderReview(); break;
            case 4: RenderInstallation(); break;
            case 5: RenderCompletion(); break;
        }
    }

    private void RenderStorage()
    {
        AddHeading(T("选择模型和插件的存储位置", "Choose where plugins and models are stored"));
        AddBody(T("模型文件可能占用数十 GB。向导会验证目录可写并检查所在磁盘的可用空间。", "Model files can use tens of gigabytes. The wizard verifies write access and available disk space."), 92, 72);
        _storagePath = new TextBox { Text = _settings.DataRoot, Location = new Point(38, 190), Size = new Size(700, 32) };
        var browse = new Button { Text = T("浏览", "Browse"), Location = new Point(750, 188), Size = new Size(110, 36) };
        browse.Click += (_, _) =>
        {
            using var picker = new FolderBrowserDialog { SelectedPath = _storagePath.Text, ShowNewFolderButton = true };
            if (picker.ShowDialog(this) == DialogResult.OK)
            {
                _storagePath.Text = picker.SelectedPath;
                UpdateStorageSummary();
            }
        };
        _storagePath.TextChanged += (_, _) => UpdateStorageSummary();
        _content.Controls.Add(_storagePath);
        _content.Controls.Add(browse);
        UpdateStorageSummary();
    }

    private void UpdateStorageSummary()
    {
        const string name = "StorageSummary";
        if (_content.Controls[name] is Label previous)
        {
            _content.Controls.Remove(previous);
            previous.Dispose();
        }
        var text = T("请输入有效目录。", "Enter a valid directory.");
        try
        {
            var path = Path.GetFullPath(_storagePath?.Text.Trim() ?? string.Empty);
            var drive = new DriveInfo(Path.GetPathRoot(path)!);
            text = T($"磁盘 {drive.Name} 可用空间：{FormatBytes(drive.AvailableFreeSpace)}", $"Drive {drive.Name} available: {FormatBytes(drive.AvailableFreeSpace)}");
        }
        catch
        {
        }
        _content.Controls.Add(new Label { Name = name, Text = text, Location = new Point(38, 242), Size = new Size(820, 34), ForeColor = Theme.Muted });
    }

    private void RenderHardware()
    {
        AddHeading(T("检查这台电脑", "Check this computer"));
        var resources = SystemResourceProbe.Capture();
        var gpu = resources.GpuTotalMiB is null
            ? T("未能通过 nvidia-smi 读取 NVIDIA GPU。", "NVIDIA GPU could not be read with nvidia-smi.")
            : T($"GPU：{resources.GpuName}\r\n显存：{resources.GpuTotalMiB:N0} MiB", $"GPU: {resources.GpuName}\r\nMemory: {resources.GpuTotalMiB:N0} MiB");
        var text = $"{gpu}\r\n{T("系统内存", "System memory")}: {resources.TotalMemoryMiB:N0} MiB\r\n{T("可用内存", "Available memory")}: {resources.AvailableMemoryMiB:N0} MiB\r\n{T("系统", "System")}: {Environment.OSVersion}\r\n\r\n{T("资源不足会显示警告，但不会阻止安装；一次只运行一个大型模型。", "Insufficient resources produce a warning but do not block installation. Run one large model at a time.")}";
        _content.Controls.Add(new Label { Text = text, Location = new Point(38, 112), Size = new Size(820, 300), ForeColor = Theme.Ink, Font = new Font("Microsoft YaHei UI", 11F) });
    }

    private void RenderPluginSelection()
    {
        AddHeading(T("选择要安装的插件", "Choose plugins to install"));
        AddBody(T("当前可信目录提供一个经过固定提交与 SHA-256 校验的真实插件源。以后可以通过签名索引增加更多插件。", "The trusted catalog currently provides one real plugin source pinned to a commit and SHA-256. More plugins can be added through signed index updates."), 92, 62);
        _pluginList = new CheckedListBox { Location = new Point(38, 172), Size = new Size(820, 250), CheckOnClick = true };
        foreach (var plugin in _plugins)
        {
            var download = plugin.PackageSizeBytes + (plugin.AssetPackages ?? []).Sum(asset => asset.SizeBytes);
            var index = _pluginList.Items.Add($"{plugin.DisplayName}  {plugin.Version}    {FormatBytes(download)} download    {plugin.RecommendedVramMiB:N0} MiB VRAM");
            if ((_settings.FirstRunSelectedPluginIds ?? []).Contains(plugin.Id, StringComparer.OrdinalIgnoreCase))
            {
                _pluginList.SetItemChecked(index, true);
            }
        }
        _content.Controls.Add(_pluginList);
    }

    private void RenderReview()
    {
        AddHeading(T("确认下载和许可证", "Review downloads and licenses"));
        var selected = SelectedPlugins();
        var download = selected.Sum(plugin => plugin.PackageSizeBytes + (plugin.AssetPackages ?? []).Sum(asset => asset.SizeBytes));
        var details = string.Join("\r\n\r\n", selected.Select(plugin => $"{plugin.DisplayName} {plugin.Version}\r\n{plugin.LicenseName}\r\n{plugin.LicenseUrl}"));
        _content.Controls.Add(new TextBox { Text = $"{T("总下载量", "Total download")}: {FormatBytes(download)}\r\n\r\n{details}", Location = new Point(38, 108), Size = new Size(820, 300), Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Color.White });
        _licenseAccepted = new CheckBox { Text = T("我已阅读并接受以上插件和模型的上游许可条款", "I have read and accept the upstream plugin and model license terms"), Location = new Point(38, 438), Size = new Size(800, 34), BackColor = Color.White };
        _content.Controls.Add(_licenseAccepted);
    }

    private void RenderInstallation()
    {
        AddHeading(T("准备下载和部署", "Ready to download and deploy"));
        AddBody(T("下载会显示容量和速度；解压、创建 Python 和安装依赖会显示活动动画与阶段记录。", "Downloads show size and speed. Extraction, Python setup, and dependency installation show activity and stage history."), 86, 52);
        _overallStatus = new Label { Text = T("总体进度", "Overall progress"), Location = new Point(38, 145), Size = new Size(820, 24), ForeColor = Theme.Muted };
        _overallProgress = new ProgressBar { Location = new Point(38, 172), Size = new Size(820, 22), Minimum = 0, Maximum = 100 };
        var currentLabel = new Label { Text = T("当前任务", "Current task"), Location = new Point(38, 211), Size = new Size(820, 24), ForeColor = Theme.Muted };
        _currentProgress = new ProgressBar { Location = new Point(38, 238), Size = new Size(820, 22), Minimum = 0, Maximum = 100 };
        _progressStatus = new Label { Text = T("点击“开始安装”继续。", "Select Install to continue."), Location = new Point(38, 274), Size = new Size(820, 48), ForeColor = Theme.Ink };
        _progressHistory = new TextBox { Location = new Point(38, 328), Size = new Size(820, 104), Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Color.FromArgb(247, 250, 249), ForeColor = Theme.Muted };
        _content.Controls.Add(_overallStatus);
        _content.Controls.Add(_overallProgress);
        _content.Controls.Add(currentLabel);
        _content.Controls.Add(_currentProgress);
        _content.Controls.Add(_progressStatus);
        _content.Controls.Add(_progressHistory);
    }

    private void RenderCompletion()
    {
        AddHeading(T("安装与环境验证完成", "Installation and environment verification complete"));
        var lines = _outcomes.Select(outcome =>
        {
            var failures = outcome.Checks.Where(check => check.IsEnforced && !check.IsSatisfied).ToArray();
            return failures.Length == 0
                ? $"PASS  {outcome.Definition.DisplayName}"
                : $"FAIL  {outcome.Definition.DisplayName}: {string.Join(", ", failures.Select(failure => failure.Requirement))}";
        });
        _content.Controls.Add(new Label { Text = string.Join("\r\n", lines), Location = new Point(38, 120), Size = new Size(820, 260), ForeColor = Theme.Ink, Font = new Font("Consolas", 11F) });
    }

    private async Task MoveBackAsync()
    {
        if (_step <= 0)
        {
            return;
        }
        _step--;
        await PersistAsync();
        RenderStep();
    }

    private async Task MoveNextAsync()
    {
        if (_step == 0 && !ValidateStorage())
        {
            return;
        }
        if (_step == 2)
        {
            var selected = SelectedPlugins();
            if (selected.Count == 0)
            {
                MessageBox.Show(T("请至少选择一个插件。", "Select at least one plugin."), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }
        if (_step == 3 && _licenseAccepted?.Checked != true)
        {
            MessageBox.Show(T("必须先接受所选插件的许可证。", "Accept the selected plugin licenses first."), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (_step == 4)
        {
            await InstallAsync();
            return;
        }
        if (_step == 5)
        {
            DialogResult = DialogResult.OK;
            Close();
            return;
        }
        _step++;
        await PersistAsync();
        RenderStep();
    }

    private bool ValidateStorage()
    {
        try
        {
            var path = Path.GetFullPath(_storagePath?.Text.Trim() ?? string.Empty);
            Directory.CreateDirectory(path);
            var probe = Path.Combine(path, $".bachen-write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "test");
            File.Delete(probe);
            _settings.DataRoot = path;
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, T("存储目录不可用", "Storage directory unavailable"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private async Task InstallAsync()
    {
        _installing = true;
        _next.Enabled = false;
        _back.Enabled = false;
        _cancel.Enabled = true;
        _cancel.Text = T("取消安装", "Cancel installation");
        _installationCancellation = new CancellationTokenSource();
        _outcomes.Clear();
        try
        {
            var plugins = SelectedPlugins();
            var completedOverallPercentage = 0;
            for (var pluginIndex = 0; pluginIndex < plugins.Count; pluginIndex++)
            {
                var plugin = plugins[pluginIndex];
                var currentStagePercentage = 0;
                UpdateOverallProgress(pluginIndex, plugins.Count, 0, ref completedOverallPercentage);
                if (_overallStatus is not null)
                {
                    _overallStatus.Text = T(
                        $"总体进度 · 插件 {pluginIndex + 1}/{plugins.Count}",
                        $"Overall progress · plugin {pluginIndex + 1}/{plugins.Count}");
                }
                AppendProgressHistory(T($"开始安装 {plugin.DisplayName}", $"Installing {plugin.DisplayName}"));
                var status = new Progress<string>(message =>
                {
                    currentStagePercentage = Math.Max(currentStagePercentage, EstimateStagePercentage(message));
                    SetCurrentProgressIndeterminate();
                    UpdateOverallProgress(pluginIndex, plugins.Count, currentStagePercentage, ref completedOverallPercentage);
                    if (_progressStatus is not null) _progressStatus.Text = $"{plugin.DisplayName}\r\n{LocalizeInstallStatus(message)}";
                    AppendProgressHistory(LocalizeInstallStatus(message));
                });
                var download = new Progress<PluginDownloadProgress>(value =>
                {
                    if (_currentProgress is not null)
                    {
                        _currentProgress.Style = ProgressBarStyle.Blocks;
                        _currentProgress.MarqueeAnimationSpeed = 0;
                        _currentProgress.Value = value.TotalBytes is > 0 ? value.Percentage : 0;
                    }
                    var downloadStage = currentStagePercentage + (int)Math.Round(value.Percentage * Math.Min(20, 94 - currentStagePercentage) / 100d);
                    UpdateOverallProgress(pluginIndex, plugins.Count, downloadStage, ref completedOverallPercentage);
                    if (_progressStatus is not null) _progressStatus.Text = $"{plugin.DisplayName}\r\n{FormatBytes(value.BytesReceived)} / {(value.TotalBytes is > 0 ? FormatBytes(value.TotalBytes.Value) : "?")}    {FormatBytes((long)value.BytesPerSecond)}/s";
                });
                _outcomes.Add(await _install(plugin, status, download, _installationCancellation.Token));
                currentStagePercentage = 100;
                SetCurrentProgressValue(100);
                UpdateOverallProgress(pluginIndex, plugins.Count, 100, ref completedOverallPercentage);
                AppendProgressHistory(T($"已完成 {plugin.DisplayName}", $"Completed {plugin.DisplayName}"));
            }
            if (_outcomes.Any(outcome => outcome.Checks.Any(check => check.IsEnforced && !check.IsSatisfied)))
            {
                throw new InvalidOperationException(T("一个或多个插件未通过环境自检。", "One or more plugins failed environment verification."));
            }
            _step = 5;
            await PersistAsync();
            RenderStep();
        }
        catch (OperationCanceledException)
        {
            SetCurrentProgressValue(0);
            if (_progressStatus is not null) _progressStatus.Text = T("安装已取消，已下载的完整文件和断点文件会保留。", "Installation canceled. Completed and partial downloads were preserved.");
            AppendProgressHistory(T("安装已取消", "Installation canceled"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            MessageBox.Show(ex.Message, T("安装失败", "Installation failed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            if (_progressStatus is not null) _progressStatus.Text = ex.Message;
        }
        finally
        {
            _installing = false;
            _installationCancellation.Dispose();
            _installationCancellation = null;
            _cancel.Text = T("稍后设置", "Set up later");
            _next.Enabled = true;
            _back.Enabled = _step > 0 && _step < 5;
            _cancel.Enabled = true;
        }
    }

    private void SetCurrentProgressIndeterminate()
    {
        if (_currentProgress is null)
        {
            return;
        }
        _currentProgress.Value = 0;
        _currentProgress.Style = ProgressBarStyle.Marquee;
        _currentProgress.MarqueeAnimationSpeed = 28;
    }

    private void SetCurrentProgressValue(int percentage)
    {
        if (_currentProgress is null)
        {
            return;
        }
        _currentProgress.Style = ProgressBarStyle.Blocks;
        _currentProgress.MarqueeAnimationSpeed = 0;
        _currentProgress.Value = Math.Clamp(percentage, 0, 100);
    }

    private void UpdateOverallProgress(int pluginIndex, int pluginCount, int stagePercentage, ref int displayedPercentage)
    {
        displayedPercentage = Math.Max(displayedPercentage, CalculateOverallPercentage(pluginIndex, pluginCount, stagePercentage));
        if (_overallProgress is not null)
        {
            _overallProgress.Value = displayedPercentage;
        }
    }

    private void AppendProgressHistory(string message)
    {
        if (_progressHistory is null || string.IsNullOrWhiteSpace(message))
        {
            return;
        }
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        if (_progressHistory.Lines.LastOrDefault()?.EndsWith(message, StringComparison.Ordinal) == true)
        {
            return;
        }
        _progressHistory.AppendText((_progressHistory.TextLength == 0 ? string.Empty : Environment.NewLine) + line);
    }

    internal static int CalculateOverallPercentage(int pluginIndex, int pluginCount, int stagePercentage)
    {
        if (pluginCount <= 0)
        {
            return 0;
        }
        return (int)Math.Clamp(Math.Round((pluginIndex + Math.Clamp(stagePercentage, 0, 100) / 100d) * 100d / pluginCount), 0, 100);
    }

    internal static int EstimateStagePercentage(string message)
        => message switch
        {
            _ when message.StartsWith("Downloading plugin package", StringComparison.Ordinal) => 5,
            _ when message.StartsWith("Verifying plugin package", StringComparison.Ordinal) => 25,
            _ when message.StartsWith("Extracting plugin package", StringComparison.Ordinal) => 30,
            _ when message.StartsWith("Downloading model asset", StringComparison.Ordinal) => 35,
            _ when message.StartsWith("Extracting model asset", StringComparison.Ordinal) => 55,
            _ when message.StartsWith("Preparing Python environment", StringComparison.Ordinal) => 60,
            _ when message.StartsWith("Project requires Python", StringComparison.Ordinal) => 61,
            _ when message.StartsWith("Downloading managed Python", StringComparison.Ordinal) || message.StartsWith("Repairing managed Python", StringComparison.Ordinal) => 62,
            _ when message.StartsWith("Installing portable managed Python", StringComparison.Ordinal) => 70,
            _ when message.StartsWith("Creating Python virtual environment", StringComparison.Ordinal) => 75,
            _ when message.StartsWith("Creating the external uv", StringComparison.Ordinal) => 76,
            _ when message.StartsWith("Installing the external uv", StringComparison.Ordinal) => 78,
            _ when message.StartsWith("Installing Python dependencies", StringComparison.Ordinal) || message.StartsWith("Synchronizing repository dependencies", StringComparison.Ordinal) => 82,
            _ when message.StartsWith("Installing the Python plugin package", StringComparison.Ordinal) => 88,
            _ when message.StartsWith("Validating installed plugin files", StringComparison.Ordinal) => 94,
            _ when message.StartsWith("Activating plugin", StringComparison.Ordinal) => 97,
            _ when message.StartsWith("Plugin installation complete", StringComparison.Ordinal) => 100,
            _ => 60
        };

    private string LocalizeInstallStatus(string message)
    {
        if (_useEnglish)
        {
            return message;
        }
        if (message.StartsWith("Downloading plugin package", StringComparison.Ordinal)) return "正在下载插件包";
        if (message.StartsWith("Verifying plugin package", StringComparison.Ordinal)) return "正在校验插件包";
        if (message.StartsWith("Extracting plugin package", StringComparison.Ordinal)) return "正在解压插件包";
        if (message.StartsWith("Downloading model asset", StringComparison.Ordinal)) return "正在下载模型资源 " + message["Downloading model asset".Length..].Trim();
        if (message.StartsWith("Extracting model asset", StringComparison.Ordinal)) return "正在解压模型资源 " + message["Extracting model asset".Length..].Trim();
        if (message.StartsWith("Preparing Python environment", StringComparison.Ordinal)) return "正在准备 Python 环境";
        if (message.StartsWith("Downloading managed Python", StringComparison.Ordinal)) return "正在下载托管 Python";
        if (message.StartsWith("Repairing managed Python", StringComparison.Ordinal)) return "正在修复托管 Python";
        if (message.StartsWith("Installing portable managed Python", StringComparison.Ordinal)) return "正在安装便携 Python";
        if (message.StartsWith("Creating Python virtual environment", StringComparison.Ordinal)) return "正在创建 Python 虚拟环境";
        if (message.StartsWith("Installing Python dependencies", StringComparison.Ordinal)) return "正在安装 Python 依赖";
        if (message.StartsWith("Installing the Python plugin package", StringComparison.Ordinal)) return "正在安装 Python 插件包";
        if (message.StartsWith("Validating installed plugin files", StringComparison.Ordinal)) return "正在验证安装文件";
        if (message.StartsWith("Activating plugin", StringComparison.Ordinal)) return "正在激活插件";
        if (message.StartsWith("Plugin installation complete", StringComparison.Ordinal)) return "插件安装完成";
        if (message.StartsWith("Synchronizing repository dependencies", StringComparison.Ordinal)) return "正在同步仓库依赖";
        if (message.StartsWith("Creating the external uv", StringComparison.Ordinal)) return "正在创建 uv 工具环境";
        if (message.StartsWith("Installing the external uv", StringComparison.Ordinal)) return "正在安装 uv 环境管理器";
        return message;
    }

    private async Task PersistAsync()
    {
        var selected = SelectedPlugins().Select(plugin => plugin.Id).ToArray();
        await _persistState(_settings.DataRoot, selected, _step);
    }

    private IReadOnlyList<PluginPackageManifest> SelectedPlugins()
    {
        if (_pluginList is not null)
        {
            return _pluginList.CheckedIndices.Cast<int>().Select(index => _plugins[index]).ToArray();
        }
        var ids = _settings.FirstRunSelectedPluginIds ?? [];
        return _plugins.Where(plugin => ids.Contains(plugin.Id, StringComparer.OrdinalIgnoreCase)).ToArray();
    }

    private void AddHeading(string text)
        => _content.Controls.Add(new Label { Text = text, Location = new Point(38, 34), Size = new Size(820, 48), Font = new Font("Microsoft YaHei UI", 18F, FontStyle.Bold), ForeColor = Theme.Ink });

    private void AddBody(string text, int top, int height)
        => _content.Controls.Add(new Label { Text = text, Location = new Point(38, top), Size = new Size(820, height), ForeColor = Theme.Muted });

    private string T(string chinese, string english) => _useEnglish ? english : chinese;

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)Math.Max(0, bytes);
        var index = 0;
        while (value >= 1024 && index < units.Length - 1)
        {
            value /= 1024;
            index++;
        }
        return $"{value:0.##} {units[index]}";
    }
}
