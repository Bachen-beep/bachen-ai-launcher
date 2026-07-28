namespace BaChenAiLauncher;

internal sealed class PluginDownloadDialog : Form
{
    private readonly PluginDownloadService _service;
    private readonly PluginPackageManifest _manifest;
    private readonly string _dataRoot;
    private readonly bool _useEnglish;
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 100, Location = new Point(28, 82), Size = new Size(624, 24) };
    private readonly Label _status = new() { Location = new Point(28, 28), Size = new Size(624, 42) };
    private readonly Label _metrics = new() { Location = new Point(28, 118), Size = new Size(624, 32) };
    private readonly Button _pause = new() { Location = new Point(420, 174), Size = new Size(110, 38) };
    private readonly Button _cancel = new() { Location = new Point(542, 174), Size = new Size(110, 38) };
    private CancellationTokenSource? _cancellation;
    private bool _running;

    public string? PackagePath { get; private set; }

    public PluginDownloadDialog(PluginDownloadService service, PluginPackageManifest manifest, string dataRoot, bool useEnglish)
    {
        _service = service;
        _manifest = manifest;
        _dataRoot = dataRoot;
        _useEnglish = useEnglish;
        Text = T("下载插件", "Download plugin");
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ClientSize = new Size(680, 236);
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        BackColor = Theme.Card;
        Font = new Font("Microsoft YaHei UI", 10F);
        _pause.Text = T("暂停", "Pause");
        _cancel.Text = T("取消", "Cancel");
        _pause.Click += async (_, _) =>
        {
            if (_running)
            {
                _cancellation?.Cancel();
            }
            else
            {
                await StartDownloadAsync();
            }
        };
        _cancel.Click += (_, _) =>
        {
            _cancellation?.Cancel();
            DialogResult = DialogResult.Cancel;
            Close();
        };
        Shown += async (_, _) => await StartDownloadAsync();
        FormClosing += (_, _) => _cancellation?.Cancel();
        Controls.AddRange([_status, _progress, _metrics, _pause, _cancel]);
    }

    private async Task StartDownloadAsync()
    {
        if (_running)
        {
            return;
        }
        _running = true;
        _pause.Text = T("暂停", "Pause");
        _status.Text = T($"正在下载 {_manifest.DisplayName}", $"Downloading {_manifest.DisplayName}");
        _cancellation = new CancellationTokenSource();
        var progress = new Progress<PluginDownloadProgress>(value =>
        {
            _progress.Value = value.TotalBytes is > 0 ? value.Percentage : 0;
            var total = value.TotalBytes is > 0 ? FormatBytes(value.TotalBytes.Value) : T("未知", "unknown");
            _metrics.Text = $"{FormatBytes(value.BytesReceived)} / {total}    {FormatBytes((long)value.BytesPerSecond)}/s    {value.Percentage}%";
        });
        try
        {
            PackagePath = await _service.DownloadAsync(_manifest, _dataRoot, progress, _cancellation.Token);
            _progress.Value = 100;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (OperationCanceledException)
        {
            if (!IsDisposed && DialogResult == DialogResult.None)
            {
                _status.Text = T("下载已暂停，临时文件已保留。", "Download paused. Partial data was preserved.");
                _pause.Text = T("继续", "Resume");
            }
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                _status.Text = T("下载失败：", "Download failed: ") + ex.Message;
                _pause.Text = T("重试", "Retry");
            }
        }
        finally
        {
            _running = false;
            _cancellation.Dispose();
            _cancellation = null;
        }
    }

    private string T(string chinese, string english) => _useEnglish ? english : chinese;

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = Math.Max(0, bytes);
        var index = 0;
        var number = (double)value;
        while (number >= 1024 && index < units.Length - 1)
        {
            number /= 1024;
            index++;
        }
        return $"{number:0.##} {units[index]}";
    }
}
