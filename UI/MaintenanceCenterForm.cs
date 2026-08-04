namespace BaChenAiLauncher;

internal sealed record MaintenanceActionDefinition(
    string Title,
    string Description,
    string ButtonText,
    Color ButtonColor,
    Func<Task> ExecuteAsync,
    Func<bool>? CanExecute = null);

internal sealed record MaintenanceCategoryDefinition(
    string Title,
    string Description,
    IReadOnlyList<MaintenanceActionDefinition> Actions);

internal sealed class MaintenanceCenterForm : Form
{
    private readonly IReadOnlyList<MaintenanceCategoryDefinition> _categories;
    private readonly bool _useEnglish;
    private readonly Panel _navigation = new();
    private readonly Panel _content = new();
    private readonly Panel _actionList = new();
    private readonly SafeTextLabel _categoryTitle = new();
    private readonly ParagraphLabel _categoryDescription = new();
    private readonly SafeTextLabel _status = new();
    private readonly GlassProgressBar _progress;
    private readonly List<RoundedButton> _categoryButtons = [];
    private readonly List<RoundedButton> _actionButtons = [];
    private int _selectedCategory;
    private bool _busy;

    public MaintenanceCenterForm(
        IReadOnlyList<MaintenanceCategoryDefinition> categories,
        string launcherVersion,
        bool useEnglish)
    {
        _categories = categories;
        _useEnglish = useEnglish;
        _progress = new GlassProgressBar
        {
            TrackColor = Color.FromArgb(220, 234, 230),
            FillColor = Color.FromArgb(38, 151, 126),
            BorderColor = Color.FromArgb(155, 197, 187)
        };
        Text = useEnglish ? "Tools and maintenance" : "工具与维护";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(820, 590);
        MinimumSize = new Size(760, 540);
        BackColor = Color.FromArgb(229, 237, 234);
        Font = new Font("Microsoft YaHei UI", 10F);
        ShowInTaskbar = false;

        _navigation.Dock = DockStyle.Left;
        _navigation.Width = 206;
        _navigation.BackColor = Theme.DeepTeal;
        Controls.Add(_navigation);

        AddNavigationText(useEnglish ? "TOOLS" : "工具中心", 24, 20, 158, 30, 14F, Color.White, FontStyle.Bold);
        AddNavigationText(
            useEnglish ? "Updates, plugins and diagnostics" : "更新、插件与诊断",
            24, 53, 158, 40, 8.5F, Color.FromArgb(174, 214, 206), FontStyle.Regular);

        var categoryTop = 112;
        for (var index = 0; index < categories.Count; index++)
        {
            var categoryIndex = index;
            var button = new RoundedButton
            {
                Text = categories[index].Title,
                Location = new Point(20, categoryTop + index * 50),
                Size = new Size(166, 38),
                FillColor = index == 0 ? Color.FromArgb(37, 125, 115) : Color.FromArgb(28, 91, 87),
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold)
            };
            button.Click += (_, _) => SelectCategory(categoryIndex);
            _categoryButtons.Add(button);
            _navigation.Controls.Add(button);
        }

        AddNavigationText(
            $"BaChen AI Launcher  v{launcherVersion}",
            20, 535, 170, 24, 8F, Color.FromArgb(148, 194, 185), FontStyle.Regular,
            AnchorStyles.Left | AnchorStyles.Bottom);

        _content.Dock = DockStyle.Fill;
        _content.BackColor = Color.White;
        Controls.Add(_content);
        _content.BringToFront();

        _categoryTitle.Location = new Point(30, 24);
        _categoryTitle.Size = new Size(540, 34);
        _categoryTitle.Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold);
        _categoryTitle.ForeColor = Theme.Ink;
        _categoryTitle.BackColor = Color.White;
        _content.Controls.Add(_categoryTitle);

        _categoryDescription.Location = new Point(30, 62);
        _categoryDescription.Size = new Size(540, 44);
        _categoryDescription.Font = new Font("Microsoft YaHei UI", 9F);
        _categoryDescription.ForeColor = Theme.Muted;
        _categoryDescription.BackColor = Color.White;
        _content.Controls.Add(_categoryDescription);

        _actionList.Location = new Point(24, 116);
        _actionList.Size = new Size(566, 360);
        _actionList.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _actionList.AutoScroll = true;
        _actionList.BackColor = Color.White;
        _actionList.SizeChanged += (_, _) =>
        {
            foreach (Control row in _actionList.Controls)
            {
                row.Width = Math.Max(420, _actionList.ClientSize.Width - 24);
            }
        };
        _content.Controls.Add(_actionList);

        var footerLine = new Panel
        {
            Height = 1,
            BackColor = Color.FromArgb(213, 226, 222),
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            Location = new Point(30, 493),
            Width = 540
        };
        _content.Controls.Add(footerLine);

        _status.Location = new Point(30, 506);
        _status.Size = new Size(390, 30);
        _status.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        _status.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
        _status.ForeColor = Theme.Muted;
        _status.BackColor = Color.White;
        _status.Text = useEnglish ? "Ready" : "就绪";
        _content.Controls.Add(_status);

        _progress.Location = new Point(30, 542);
        _progress.Size = new Size(390, 9);
        _progress.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        _progress.Visible = false;
        _content.Controls.Add(_progress);

        var closeButton = new RoundedButton
        {
            Text = useEnglish ? "Close" : "关闭",
            Size = new Size(118, 38),
            FillColor = Color.FromArgb(65, 99, 95),
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
            Anchor = AnchorStyles.Right | AnchorStyles.Bottom
        };
        closeButton.Click += (_, _) => Close();
        _content.Controls.Add(closeButton);

        void LayoutFooter()
        {
            footerLine.Top = _content.ClientSize.Height - 97;
            footerLine.Width = Math.Max(260, _content.ClientSize.Width - 60);
            _status.Top = _content.ClientSize.Height - 82;
            _status.Width = Math.Max(220, _content.ClientSize.Width - 200);
            _progress.Top = _content.ClientSize.Height - 43;
            _progress.Width = Math.Max(220, _content.ClientSize.Width - 200);
            closeButton.Location = new Point(_content.ClientSize.Width - closeButton.Width - 30, _content.ClientSize.Height - 60);
        }
        _content.SizeChanged += (_, _) => LayoutFooter();
        LayoutFooter();
        SelectCategory(0);
    }

    public void CenterOverOwner(Form owner)
    {
        if (IsDisposed || owner.IsDisposed)
        {
            return;
        }

        var ownerOrigin = owner.PointToScreen(Point.Empty);
        var ownerCenter = new Point(
            ownerOrigin.X + owner.ClientSize.Width / 2,
            ownerOrigin.Y + owner.ClientSize.Height / 2);
        var workingArea = Screen.FromControl(owner).WorkingArea;
        var x = ownerCenter.X - Width / 2;
        var y = ownerCenter.Y - Height / 2;
        x = Math.Clamp(x, workingArea.Left, workingArea.Right - Width);
        y = Math.Clamp(y, workingArea.Top, workingArea.Bottom - Height);
        StartPosition = FormStartPosition.Manual;
        Location = new Point(x, y);
    }

    public void ReportProgress(int? percent, string message)
    {
        if (IsDisposed)
        {
            return;
        }

        _busy = true;
        _status.Text = message;
        _status.ForeColor = Theme.MidTeal;
        _progress.Visible = true;
        if (percent is null)
        {
            _progress.Style = ProgressBarStyle.Marquee;
            _progress.MarqueeAnimationSpeed = 28;
        }
        else
        {
            _progress.Style = ProgressBarStyle.Continuous;
            _progress.Value = Math.Clamp(percent.Value, 0, 100);
        }
        RefreshActionAvailability();
    }

    public void CompleteProgress(string message, bool failed = false)
    {
        if (IsDisposed)
        {
            return;
        }

        _busy = false;
        _progress.Visible = false;
        _status.Text = message;
        _status.ForeColor = failed ? Theme.Coral : Theme.MidTeal;
        RefreshActionAvailability();
    }

    private void SelectCategory(int index)
    {
        if (_busy || index < 0 || index >= _categories.Count)
        {
            return;
        }

        _selectedCategory = index;
        for (var buttonIndex = 0; buttonIndex < _categoryButtons.Count; buttonIndex++)
        {
            _categoryButtons[buttonIndex].FillColor = buttonIndex == index
                ? Color.FromArgb(37, 125, 115)
                : Color.FromArgb(28, 91, 87);
        }
        var category = _categories[index];
        _categoryTitle.Text = category.Title;
        _categoryDescription.Text = category.Description;
        _actionList.SuspendLayout();
        _actionList.Controls.Clear();
        _actionButtons.Clear();
        for (var actionIndex = 0; actionIndex < category.Actions.Count; actionIndex++)
        {
            var row = CreateActionRow(category.Actions[actionIndex]);
            row.Location = new Point(0, actionIndex * 86);
            _actionList.Controls.Add(row);
        }
        _actionList.ResumeLayout();
        RefreshActionAvailability();
    }

    private Control CreateActionRow(MaintenanceActionDefinition action)
    {
        var row = new RoundedPanel
        {
            Size = new Size(Math.Max(420, _actionList.ClientSize.Width - 24), 76),
            FillColor = Color.FromArgb(244, 249, 247),
            BorderColor = Color.FromArgb(207, 223, 218),
            BorderWidth = 1,
            CornerRadius = 8,
            Anchor = AnchorStyles.Left | AnchorStyles.Right
        };
        var title = new SafeTextLabel
        {
            Text = action.Title,
            Location = new Point(16, 10),
            Size = new Size(330, 24),
            Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold),
            ForeColor = Theme.Ink,
            BackColor = row.FillColor
        };
        var description = new ParagraphLabel
        {
            Text = action.Description,
            Location = new Point(16, 37),
            Size = new Size(330, 30),
            Font = new Font("Microsoft YaHei UI", 8F),
            ForeColor = Theme.Muted,
            BackColor = row.FillColor
        };
        var button = new RoundedButton
        {
            Text = action.ButtonText,
            Size = new Size(132, 36),
            FillColor = action.ButtonColor,
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        button.Location = new Point(row.Width - button.Width - 16, 20);
        button.Click += async (_, _) => await ExecuteActionAsync(action);
        row.SizeChanged += (_, _) =>
        {
            button.Left = row.ClientSize.Width - button.Width - 16;
            title.Width = Math.Max(150, button.Left - 28);
            description.Width = title.Width;
        };
        _actionButtons.Add(button);
        row.Controls.Add(title);
        row.Controls.Add(description);
        row.Controls.Add(button);
        return row;
    }

    private async Task ExecuteActionAsync(MaintenanceActionDefinition action)
    {
        if (_busy || action.CanExecute?.Invoke() == false)
        {
            return;
        }

        ReportProgress(null, _useEnglish ? $"Running: {action.Title}" : $"正在执行：{action.Title}");
        try
        {
            await action.ExecuteAsync();
            if (!IsDisposed)
            {
                _status.Text = _useEnglish ? "Task completed" : "操作已完成";
                _status.ForeColor = Theme.MidTeal;
            }
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                _status.Text = (_useEnglish ? "Failed: " : "操作失败：") + ex.Message;
                _status.ForeColor = Theme.Coral;
            }
        }
        finally
        {
            if (!IsDisposed)
            {
                _busy = false;
                _progress.Visible = false;
                RefreshActionAvailability();
            }
        }
    }

    private void RefreshActionAvailability()
    {
        if (_selectedCategory < 0 || _selectedCategory >= _categories.Count)
        {
            return;
        }

        var actions = _categories[_selectedCategory].Actions;
        for (var index = 0; index < Math.Min(actions.Count, _actionButtons.Count); index++)
        {
            _actionButtons[index].Enabled = !_busy && (actions[index].CanExecute?.Invoke() ?? true);
        }
        foreach (var button in _categoryButtons)
        {
            button.Enabled = !_busy;
        }
    }

    private void AddNavigationText(
        string text,
        int x,
        int y,
        int width,
        int height,
        float fontSize,
        Color color,
        FontStyle style,
        AnchorStyles anchor = AnchorStyles.Top | AnchorStyles.Left)
    {
        _navigation.Controls.Add(new SafeTextLabel
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(width, height),
            Font = new Font("Microsoft YaHei UI", fontSize, style),
            ForeColor = color,
            BackColor = Theme.DeepTeal,
            Anchor = anchor
        });
    }
}
