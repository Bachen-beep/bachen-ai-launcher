namespace BaChenAiLauncher;

/// <summary>一级界面内的设置与维护工作台。</summary>
internal sealed class SettingsWorkspace : Panel
{
    private const int CornerRadius = 16;
    private readonly IReadOnlyList<MaintenanceCategoryDefinition> _categories;
    private readonly bool _useEnglish;
    private readonly Func<Control> _createSettingsCard;
    private readonly Panel _navigation = new();
    private readonly Panel _content = new();
    private readonly Panel _actionList = new();
    private readonly SafeTextLabel _categoryTitle = new();
    private readonly ParagraphLabel _categoryDescription = new();
    private readonly SafeTextLabel _status = new();
    private readonly GlassProgressBar _progress = new()
    {
        TrackColor = Color.FromArgb(220, 234, 230),
        FillColor = Color.FromArgb(38, 151, 126),
        BorderColor = Color.FromArgb(155, 197, 187)
    };
    private readonly List<RoundedButton> _categoryButtons = [];
    private readonly List<Control> _actionButtons = [];
    private int _selectedCategory;
    private bool _busy;

    public SettingsWorkspace(
        IReadOnlyList<MaintenanceCategoryDefinition> categories,
        string launcherVersion,
        bool useEnglish,
        Func<Control> createSettingsCard)
    {
        _categories = categories;
        _useEnglish = useEnglish;
        _createSettingsCard = createSettingsCard;
        Dock = DockStyle.Fill;
        BackColor = Color.White;
        Visible = false;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

        _navigation.Dock = DockStyle.Left;
        _navigation.Width = 194;
        _navigation.BackColor = Theme.DeepTeal;
        Controls.Add(_navigation);
        AddNavigationText(useEnglish ? "SETTINGS" : "设置", 22, 20, 150, 30, 14F, Color.White, FontStyle.Bold);
        AddNavigationText(
            useEnglish ? "Updates, plugins and diagnostics" : "更新、插件与诊断",
            22, 53, 150, 40, 8.5F, Color.FromArgb(174, 214, 206), FontStyle.Regular);

        const int categoryTop = 112;
        for (var index = 0; index < categories.Count; index++)
        {
            var categoryIndex = index;
            var button = new RoundedButton
            {
                Text = categories[index].Title,
                Location = new Point(16, categoryTop + index * 50),
                Size = new Size(162, 38),
                FillColor = index == 0 ? Color.FromArgb(37, 125, 115) : Color.FromArgb(28, 91, 87),
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold)
            };
            button.Click += (_, _) => SelectCategory(categoryIndex);
            _categoryButtons.Add(button);
            _navigation.Controls.Add(button);
        }
        AddNavigationText(
            $"BaChen AI Launcher  v{launcherVersion}",
            16, 0, 164, 24, 8F, Color.FromArgb(148, 194, 185), FontStyle.Regular,
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
        _actionList.AutoScroll = true;
        _actionList.BackColor = Color.White;
        _actionList.SizeChanged += (_, _) => LayoutActionRows();
        _content.Controls.Add(_actionList);

        var footerLine = new Panel
        {
            Height = 1,
            BackColor = Color.FromArgb(213, 226, 222),
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
        };
        _content.Controls.Add(footerLine);

        _status.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
        _status.ForeColor = Theme.Muted;
        _status.BackColor = Color.White;
        _status.Text = useEnglish ? "Ready" : "就绪";
        _status.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        _content.Controls.Add(_status);

        _progress.Visible = false;
        _progress.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        _content.Controls.Add(_progress);

        void LayoutFooter()
        {
            _categoryTitle.Width = Math.Max(280, _content.ClientSize.Width - 60);
            _categoryDescription.Width = Math.Max(280, _content.ClientSize.Width - 60);
            _actionList.SetBounds(
                24,
                116,
                Math.Max(420, _content.ClientSize.Width - 48),
                Math.Max(120, _content.ClientSize.Height - 226));
            footerLine.SetBounds(30, _content.ClientSize.Height - 97, Math.Max(260, _content.ClientSize.Width - 60), 1);
            _status.SetBounds(30, _content.ClientSize.Height - 82, Math.Max(220, _content.ClientSize.Width - 60), 30);
            _progress.SetBounds(30, _content.ClientSize.Height - 43, Math.Max(220, _content.ClientSize.Width - 60), 9);
            LayoutActionRows();
        }
        _content.SizeChanged += (_, _) => LayoutFooter();
        LayoutFooter();
        SelectCategory(0);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        ApplyRoundedRegion();
    }

    public void ShowCategory(int index = 0)
    {
        Visible = true;
        BringToFront();
        SelectCategory(Math.Clamp(index, 0, _categories.Count - 1));
    }

    public int SelectedCategory => _selectedCategory;

    public void ReportProgress(int? percent, string message)
    {
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
        var top = 0;
        if (index == _categories.Count - 1)
        {
            var settingsCard = _createSettingsCard();
            settingsCard.Location = Point.Empty;
            settingsCard.Width = Math.Max(420, _actionList.ClientSize.Width - 24);
            _actionList.Controls.Add(settingsCard);
            top = settingsCard.Height + 10;
        }
        for (var actionIndex = 0; actionIndex < category.Actions.Count; actionIndex++)
        {
            var row = CreateActionRow(category.Actions[actionIndex]);
            row.Location = new Point(0, top + actionIndex * 86);
            _actionList.Controls.Add(row);
        }
        _actionList.ResumeLayout();
        LayoutActionRows();
        RefreshActionAvailability();
    }

    private Control CreateActionRow(MaintenanceActionDefinition action)
    {
        var row = new SettingsActionRow(action, () => ExecuteActionAsync(action))
        {
            Size = new Size(Math.Max(420, _actionList.ClientSize.Width - 24), 76),
            Anchor = AnchorStyles.Left | AnchorStyles.Right
        };
        _actionButtons.Add(row);
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

    private void LayoutActionRows()
    {
        foreach (Control row in _actionList.Controls)
        {
            if (row is SettingsActionRow || Equals(row.Tag, "settings-card"))
            {
                row.Width = Math.Max(420, _actionList.ClientSize.Width - 24);
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

    private void AddNavigationText(string text, int x, int y, int width, int height, float fontSize, Color color, FontStyle style, AnchorStyles anchor = AnchorStyles.Top | AnchorStyles.Left)
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

    private void ApplyRoundedRegion()
    {
        if (Width < 2 || Height < 2)
        {
            return;
        }

        var diameter = Math.Min(CornerRadius * 2, Math.Min(Width, Height));
        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddArc(0, 0, diameter, diameter, 180, 90);
        path.AddArc(Width - diameter, 0, diameter, diameter, 270, 90);
        path.AddArc(Width - diameter, Height - diameter, diameter, diameter, 0, 90);
        path.AddArc(0, Height - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        Region?.Dispose();
        Region = new Region(path);
    }
}

internal sealed class SettingsActionRow : Control
{
    private readonly MaintenanceActionDefinition _action;
    private readonly Func<Task> _execute;
    private bool _hovered;

    public SettingsActionRow(MaintenanceActionDefinition action, Func<Task> execute)
    {
        _action = action;
        _execute = execute;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnClick(EventArgs e)
    {
        if (Enabled)
        {
            _ = _execute();
        }
        base.OnClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        e.Graphics.Clear(PaintSurface.ResolveParentColor(this));
        var row = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        using var rowBrush = new SolidBrush(Color.FromArgb(244, 249, 247));
        using var rowPen = new Pen(Color.FromArgb(207, 223, 218), 1F);
        e.Graphics.FillRectangle(rowBrush, row);
        e.Graphics.DrawRectangle(rowPen, row);

        using var titleFont = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
        using var descriptionFont = new Font("Microsoft YaHei UI", 8F);
        PaintSurface.DrawText(e.Graphics, _action.Title, titleFont, new Rectangle(16, 10, Math.Max(180, Width - 188), 24), Theme.Ink, StringAlignment.Near);
        PaintSurface.DrawParagraph(e.Graphics, _action.Description, descriptionFont, new Rectangle(16, 37, Math.Max(180, Width - 188), 30), Theme.Muted);

        var buttonBounds = new Rectangle(Math.Max(16, Width - 148), 18, 132, 40);
        var buttonColor = !Enabled
            ? Color.FromArgb(176, 190, 187)
            : _hovered ? ControlPaint.Light(_action.ButtonColor, 0.08F) : _action.ButtonColor;
        using var buttonPath = CreatePillPath(buttonBounds);
        using var buttonBrush = new SolidBrush(buttonColor);
        e.Graphics.FillPath(buttonBrush, buttonPath);
        GlassPaint.DrawReflection(e.Graphics, buttonPath, buttonBounds, Enabled ? 8 : 6, _hovered ? 0.2F : 0F, 0);
        using var buttonBorder = new Pen(Color.FromArgb(26, Color.White), 1F);
        e.Graphics.DrawPath(buttonBorder, buttonPath);
        using var buttonFont = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
        PaintSurface.DrawText(e.Graphics, _action.ButtonText, buttonFont, buttonBounds, Color.White, StringAlignment.Center);
    }

    private static System.Drawing.Drawing2D.GraphicsPath CreatePillPath(Rectangle bounds)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        var diameter = Math.Max(2, Math.Min(bounds.Height, bounds.Width));
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 90, 180);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 180);
        path.CloseFigure();
        return path;
    }
}
