using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO.Compression;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace BaChenAiLauncher;

internal static class Theme
{
    public static readonly Color DeepTeal = Color.FromArgb(0, 70, 67);
    public static readonly Color MidTeal = Color.FromArgb(0, 98, 93);
    public static readonly Color Card = Color.FromArgb(244, 249, 247);
    public static readonly Color Ink = Color.FromArgb(29, 69, 66);
    public static readonly Color Muted = Color.FromArgb(82, 112, 108);
    public static readonly Color Coral = Color.FromArgb(186, 78, 73);
}

internal static class PaintSurface
{
    public static Color ResolveParentColor(Control control)
    {
        for (Control? current = control.Parent; current is not null; current = current.Parent)
        {
            if (current is RoundedPanel roundedPanel)
            {
                return roundedPanel.FillColor;
            }
            if (current.BackColor != Color.Transparent)
            {
                return current.BackColor;
            }
        }
        return SystemColors.Control;
    }

    public static Rectangle TextBounds(Rectangle bounds, int bottomSafety = 0)
    {
        return new Rectangle(bounds.X + 1, bounds.Y, Math.Max(1, bounds.Width - 2), Math.Max(1, bounds.Height));
    }

    public static void DrawText(
        Graphics graphics,
        string text,
        Font font,
        Rectangle bounds,
        Color color,
        StringAlignment horizontal = StringAlignment.Near,
        StringAlignment vertical = StringAlignment.Center,
        bool wrap = false,
        bool ellipsis = false)
    {
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var brush = new SolidBrush(color);
        Font? fittedFont = null;
        var drawFont = font;
        if (!wrap)
        {
            var measured = graphics.MeasureString(text, font, int.MaxValue, StringFormat.GenericTypographic);
            var scale = Math.Min(1F, Math.Min(bounds.Width / Math.Max(1F, measured.Width), bounds.Height / Math.Max(1F, measured.Height)));
            if (scale < 0.99F)
            {
                fittedFont = new Font(font.FontFamily, Math.Max(7F, font.Size * scale * 0.96F), font.Style, GraphicsUnit.Point);
                drawFont = fittedFont;
            }
        }
        using var format = new StringFormat
        {
            Alignment = horizontal,
            LineAlignment = vertical,
            FormatFlags = wrap ? StringFormatFlags.LineLimit : StringFormatFlags.NoWrap,
            Trimming = ellipsis ? StringTrimming.EllipsisCharacter : StringTrimming.None
        };
        graphics.DrawString(text, drawFont, brush, bounds, format);
        fittedFont?.Dispose();
    }

    public static void DrawParagraph(Graphics graphics, string text, Font font, Rectangle bounds, Color color)
    {
        var tokens = text.Contains(' ')
            ? text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(token => token + " ").ToArray()
            : text.Select(character => character.ToString()).ToArray();
        var lines = new List<string>();
        var current = string.Empty;
        foreach (var token in tokens)
        {
            var candidate = current + token;
            if (current.Length > 0 && graphics.MeasureString(candidate.TrimEnd(), font, int.MaxValue, StringFormat.GenericTypographic).Width > bounds.Width)
            {
                lines.Add(current.TrimEnd());
                current = token;
            }
            else
            {
                current = candidate;
            }
        }
        if (!string.IsNullOrWhiteSpace(current))
        {
            lines.Add(current.TrimEnd());
        }
        if (!text.Contains(' ') && lines.Count == 2 && lines[1].Length * 3 < lines[0].Length)
        {
            var split = (text.Length + 1) / 2;
            lines = [text[..split], text[split..]];
        }

        var lineHeight = Math.Max(16, (int)Math.Ceiling(font.GetHeight(graphics)) + 2);
        var maxLines = Math.Max(1, bounds.Height / lineHeight);
        for (var index = 0; index < Math.Min(lines.Count, maxLines); index++)
        {
            DrawText(graphics, lines[index], font, new Rectangle(bounds.X, bounds.Y + index * lineHeight, bounds.Width, lineHeight), color, vertical: StringAlignment.Near);
        }
    }
}

internal sealed class RoundedPanel : Panel
{
    public int CornerRadius { get; init; } = 18;
    public int ShadowOffset { get; init; }
    public int BorderWidth { get; init; }
    private Color _fillColor = Color.White;
    public Color FillColor
    {
        get => _fillColor;
        init
        {
            _fillColor = value;
            BackColor = value;
        }
    }
    public Color BorderColor { get; init; } = Color.Transparent;
    public Color ShadowColor { get; init; } = Color.Transparent;

    public RoundedPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        BackColor = Color.White;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(PaintSurface.ResolveParentColor(this));
        if (ShadowOffset > 0 && ShadowColor.A > 0)
        {
            using var shadow = CreatePath(new Rectangle(3, 3 + ShadowOffset, Width - 7, Height - 7), CornerRadius);
            using var brush = new SolidBrush(ShadowColor);
            e.Graphics.FillPath(brush, shadow);
        }

        using var path = CreatePath(new Rectangle(0, 0, Width - 1 - ShadowOffset, Height - 1 - ShadowOffset), CornerRadius);
        using var fill = new SolidBrush(FillColor);
        e.Graphics.FillPath(fill, path);
        if (BorderWidth > 0 && BorderColor.A > 0)
        {
            using var pen = new Pen(BorderColor, BorderWidth);
            e.Graphics.DrawPath(pen, path);
        }
    }

    private static GraphicsPath CreatePath(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Max(2, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class RoundedButton : Control
{
    public Color FillColor { get; init; } = Theme.DeepTeal;
    private bool _hovered;
    private bool _pressed;

    public RoundedButton()
    {
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        TabStop = true;
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
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        _pressed = true;
        Invalidate();
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(PaintSurface.ResolveParentColor(this));
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var color = !Enabled
            ? Color.FromArgb(176, 190, 187)
            : _pressed ? ControlPaint.Dark(FillColor, 0.14F)
            : _hovered ? ControlPaint.Light(FillColor, 0.08F)
            : FillColor;
        using var path = CreatePillPath(new Rectangle(0, 0, Width - 1, Height - 1));
        using var brush = new SolidBrush(color);
        e.Graphics.FillPath(brush, path);
        PaintSurface.DrawText(
            e.Graphics,
            Text,
            Font,
            PaintSurface.TextBounds(ClientRectangle),
            Enabled ? ForeColor : Color.FromArgb(245, 248, 247),
            StringAlignment.Center);
    }

    private static GraphicsPath CreatePillPath(Rectangle bounds)
    {
        var path = new GraphicsPath();
        var diameter = Math.Max(2, Math.Min(bounds.Height, bounds.Width));
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 90, 180);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 180);
        path.CloseFigure();
        return path;
    }
}

internal sealed class SafeTextLabel : Control
{
    private ContentAlignment _textAlign = ContentAlignment.MiddleLeft;
    public ContentAlignment TextAlign
    {
        get => _textAlign;
        set
        {
            _textAlign = value;
            Invalidate();
        }
    }

    public SafeTextLabel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(PaintSurface.ResolveParentColor(this));
        var horizontal = TextAlign switch
        {
            ContentAlignment.BottomCenter or ContentAlignment.MiddleCenter or ContentAlignment.TopCenter => StringAlignment.Center,
            ContentAlignment.BottomRight or ContentAlignment.MiddleRight or ContentAlignment.TopRight => StringAlignment.Far,
            _ => StringAlignment.Near
        };
        var vertical = TextAlign switch
        {
            ContentAlignment.BottomCenter or ContentAlignment.BottomLeft or ContentAlignment.BottomRight => StringAlignment.Far,
            ContentAlignment.TopCenter or ContentAlignment.TopLeft or ContentAlignment.TopRight => StringAlignment.Near,
            _ => StringAlignment.Center
        };
        PaintSurface.DrawText(e.Graphics, Text, Font, PaintSurface.TextBounds(ClientRectangle), ForeColor, horizontal, vertical);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Invalidate();
    }
}

internal sealed class ParagraphLabel : Control
{
    public ParagraphLabel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(PaintSurface.ResolveParentColor(this));
        PaintSurface.DrawParagraph(e.Graphics, Text, Font, PaintSurface.TextBounds(ClientRectangle), ForeColor);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Invalidate();
    }
}

internal sealed class AnimatedServiceCard : Control
{
    private readonly System.Windows.Forms.Timer _animationTimer = new() { Interval = 16 };
    private float _hoverProgress;
    private int _direction;

    public string IndexText { get; init; } = string.Empty;
    public string TitleText { get; init; } = string.Empty;
    public string DescriptionText { get; init; } = string.Empty;
    public string CapabilityText { get; init; } = string.Empty;
    public string ActionText { get; init; } = string.Empty;
    public Color AccentColor { get; init; } = Theme.MidTeal;
    public Action? InvokeAction { get; init; }

    public AnimatedServiceCard()
    {
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        _animationTimer.Tick += (_, _) => Animate();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _direction = 1;
        _animationTimer.Start();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _direction = -1;
        _animationTimer.Start();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location))
        {
            InvokeAction?.Invoke();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var lift = (int)Math.Round(_hoverProgress * 4);
        var body = new Rectangle(1, 3 - lift, Width - 2, Height - 11);
        var shadow = new Rectangle(4, 10 - lift, Width - 9, Height - 12);
        using var shadowPath = RoundedPath(shadow, 22);
        using var shadowBrush = new SolidBrush(Color.FromArgb((int)(35 + _hoverProgress * 55), 0, 37, 35));
        e.Graphics.FillPath(shadowBrush, shadowPath);

        using var bodyPath = RoundedPath(body, 22);
        using var bodyBrush = new SolidBrush(Color.FromArgb(247, 251, 249));
        e.Graphics.FillPath(bodyBrush, bodyPath);
        using var borderPen = new Pen(Color.FromArgb((int)(165 + _hoverProgress * 55), AccentColor), 1.2F);
        e.Graphics.DrawPath(borderPen, bodyPath);

        using var accentPath = RoundedPath(new Rectangle(body.Left, body.Top + 18, 6, body.Height - 36), 3);
        using var accentBrush = new SolidBrush(AccentColor);
        e.Graphics.FillPath(accentBrush, accentPath);

        PaintSurface.DrawText(e.Graphics, IndexText, new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold), PaintSurface.TextBounds(new Rectangle(20, 17 - lift, Width - 42, 23), 3), AccentColor);
        PaintSurface.DrawText(e.Graphics, TitleText, new Font("Microsoft YaHei UI", 15F, FontStyle.Bold), PaintSurface.TextBounds(new Rectangle(20, 39 - lift, Width - 42, 38)), Theme.Ink);
        PaintSurface.DrawParagraph(e.Graphics, DescriptionText, new Font("Microsoft YaHei UI", 9F), PaintSurface.TextBounds(new Rectangle(20, 78 - lift, Width - 40, 64)), Theme.Muted);

        var wideCard = Width >= 360;
        var capabilityRect = new Rectangle(20, Height - 48 - lift, wideCard ? 160 : 112, 24);
        using var capabilityPath = RoundedPath(capabilityRect, 12);
        using var capabilityBrush = new SolidBrush(Color.FromArgb(22, AccentColor));
        e.Graphics.FillPath(capabilityBrush, capabilityPath);
        PaintSurface.DrawText(e.Graphics, CapabilityText, new Font("Microsoft YaHei UI", 7.5F, FontStyle.Bold), PaintSurface.TextBounds(capabilityRect, 3), AccentColor, StringAlignment.Center);

        var actionWidth = wideCard ? 174 : 128;
        var actionRect = new Rectangle(Width - actionWidth - 20, Height - 52 - lift, actionWidth, 32);
        using var actionPath = RoundedPath(actionRect, 16);
        using var actionBrush = new SolidBrush(AccentColor);
        e.Graphics.FillPath(actionBrush, actionPath);
        PaintSurface.DrawText(e.Graphics, ActionText + "  →", new Font("Microsoft YaHei UI", 9F, FontStyle.Bold), PaintSurface.TextBounds(actionRect), Color.White, StringAlignment.Center);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(PaintSurface.ResolveParentColor(this));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _animationTimer.Dispose();
        }
        base.Dispose(disposing);
    }

    private void Animate()
    {
        _hoverProgress = Math.Clamp(_hoverProgress + _direction * 0.10F, 0F, 1F);
        if (_hoverProgress is 0F or 1F)
        {
            _animationTimer.Stop();
        }
        Invalidate();
    }

    private static GraphicsPath RoundedPath(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class StableModelSelectorForm : Form
{
    private readonly System.Windows.Forms.Timer _fadeTimer = new() { Interval = 16 };
    private readonly RoundedButton _startButton;
    private readonly List<ModelChoiceCard> _choices = [];

    public ServiceProfile? SelectedProfile { get; private set; }

    public StableModelSelectorForm(ServiceProfile smallSfx, ServiceProfile smallMusic, ServiceProfile medium, bool useEnglish)
    {
        AutoScaleMode = AutoScaleMode.None;
        string L(string chinese, string english) => useEnglish ? english : chinese;
        Text = "Select Stable Audio 3 Model";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(900, 620);
        BackColor = Theme.DeepTeal;
        Font = new Font("Microsoft YaHei UI", 10F);
        Opacity = 0;

        var title = new SafeTextLabel { Text = L("选择 Stable Audio 3 模型", "Select a Stable Audio 3 model"), Bounds = new Rectangle(38, 30, 820, 44), Font = new Font("Microsoft YaHei UI", 20F, FontStyle.Bold), ForeColor = Color.White, BackColor = Color.Transparent };
        var subtitle = new SafeTextLabel { Text = L("选择模型后再启动服务。small-sfx 与 small-music 更适合当前硬件。", "Choose a model before starting. small-sfx and small-music are recommended for this hardware."), Bounds = new Rectangle(40, 78, 820, 32), Font = new Font("Microsoft YaHei UI", 10F), ForeColor = Color.FromArgb(194, 225, 219), BackColor = Color.Transparent };
        Controls.Add(title);
        Controls.Add(subtitle);

        var choicesPanel = new FlowLayoutPanel { Location = new Point(30, 124), Size = new Size(840, 360), FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.Transparent };
        AddChoice(choicesPanel, smallSfx, "SMALL-SFX", L("短音效 / 环境声", "Short SFX / ambience"), L("约 2.1 GiB · 当前推荐", "Approx. 2.1 GiB · recommended"), Theme.MidTeal);
        AddChoice(choicesPanel, smallMusic, "SMALL-MUSIC", L("音乐 / 循环氛围", "Music / looping ambience"), L("约 2.1 GiB · 当前推荐", "Approx. 2.1 GiB · recommended"), Color.FromArgb(31, 121, 108));
        AddChoice(choicesPanel, medium, "MEDIUM", L("更高质量生成", "Higher quality generation"), L("约 8.6 GiB · 显存风险高", "Approx. 8.6 GiB · high VRAM risk"), Color.FromArgb(132, 79, 145));
        Controls.Add(choicesPanel);

        var cancelButton = new RoundedButton { Text = L("取消", "Cancel"), FillColor = Color.FromArgb(38, 101, 96), ForeColor = Color.White, Size = new Size(132, 42), Location = new Point(594, 550) };
        cancelButton.Click += (_, _) => Close();
        _startButton = new RoundedButton { Text = L("启动所选模型  →", "Launch selected  →"), FillColor = Theme.Coral, ForeColor = Color.White, Size = new Size(164, 42), Location = new Point(736, 550), Enabled = false };
        _startButton.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        Controls.Add(cancelButton);
        Controls.Add(_startButton);

        Shown += (_, _) => _fadeTimer.Start();
        _fadeTimer.Tick += (_, _) =>
        {
            Opacity = Math.Min(1D, Opacity + 0.10D);
            if (Opacity >= 1D)
            {
                _fadeTimer.Stop();
            }
        };
    }

    private void AddChoice(FlowLayoutPanel panel, ServiceProfile profile, string model, string purpose, string note, Color accent)
    {
        var choice = new ModelChoiceCard(model, purpose, note, accent) { Width = 834, Height = 104, Margin = new Padding(0, 0, 0, 12) };
        choice.Clicked += () => Select(profile, choice);
        _choices.Add(choice);
        panel.Controls.Add(choice);
    }

    private void Select(ServiceProfile profile, ModelChoiceCard selected)
    {
        SelectedProfile = profile;
        foreach (var choice in _choices)
        {
            choice.IsSelected = choice == selected;
        }
        _startButton.Enabled = true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _fadeTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal sealed class ModelChoiceCard : Control
{
    private readonly string _model;
    private readonly string _purpose;
    private readonly string _note;
    private readonly Color _accent;
    private bool _hovered;
    private bool _selected;

    public event Action? Clicked;
    public bool IsSelected { get => _selected; set { _selected = value; Invalidate(); } }

    public ModelChoiceCard(string model, string purpose, string note, Color accent)
    {
        _model = model;
        _purpose = purpose;
        _note = note;
        _accent = accent;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
    }

    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseClick(MouseEventArgs e) { Clicked?.Invoke(); base.OnMouseClick(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var fill = _selected ? Color.FromArgb(241, 250, 247) : _hovered ? Color.FromArgb(226, 243, 238) : Color.FromArgb(21, 89, 84);
        var ink = _selected ? Theme.Ink : Color.White;
        using var path = ChoicePath(new Rectangle(1, 1, Width - 2, Height - 2), 18);
        using var brush = new SolidBrush(fill);
        e.Graphics.FillPath(brush, path);
        using var pen = new Pen(_selected ? _accent : Color.FromArgb(86, 155, 144), _selected ? 2F : 1F);
        e.Graphics.DrawPath(pen, path);
        PaintSurface.DrawText(e.Graphics, _model, new Font("Microsoft YaHei UI", 9F, FontStyle.Bold), PaintSurface.TextBounds(new Rectangle(25, 16, 220, 20), 3), _accent);
        PaintSurface.DrawText(e.Graphics, _purpose, new Font("Microsoft YaHei UI", 13F, FontStyle.Bold), PaintSurface.TextBounds(new Rectangle(25, 41, 390, 30)), ink);
        PaintSurface.DrawText(e.Graphics, _note, new Font("Microsoft YaHei UI", 9.5F), PaintSurface.TextBounds(new Rectangle(445, 40, Width - 530, 31)), _selected ? Theme.Muted : Color.FromArgb(196, 228, 222));
        var mark = new Rectangle(Width - 58, 36, 30, 30);
        using var markPath = ChoicePath(mark, 14);
        using var markBrush = new SolidBrush(_selected ? _accent : Color.FromArgb(54, 127, 117));
        e.Graphics.FillPath(markBrush, markPath);
        PaintSurface.DrawText(e.Graphics, _selected ? "✓" : "", new Font("Segoe UI", 12F, FontStyle.Bold), PaintSurface.TextBounds(mark), Color.White, StringAlignment.Center);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(PaintSurface.ResolveParentColor(this));
    }

    private static GraphicsPath ChoicePath(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var d = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal enum ServiceRuntimeState
{
    Ready,
    Checking,
    Starting,
    Running,
    Stopping,
    Updating,
    Missing,
    Error
}

internal sealed record PluginUiEntry(
    string Id,
    string Title,
    string Description,
    string Category,
    ServiceProfile Profile,
    int RecommendedVramMiB,
    Color Accent,
    bool HasModelSelector = false);

internal sealed class PluginListItem : Control
{
    private readonly System.Windows.Forms.Timer _animationTimer = new() { Interval = 16 };
    private float _hoverProgress;
    private int _direction;
    private bool _selected;

    public string TitleText { get; set; } = string.Empty;
    public string CategoryText { get; set; } = string.Empty;
    public string StatusText { get; set; } = string.Empty;
    public Color AccentColor { get; set; } = Theme.MidTeal;
    public Color StatusColor { get; set; } = Theme.MidTeal;
    public Action? InvokeAction { get; set; }
    public bool IsSelected
    {
        get => _selected;
        set
        {
            _selected = value;
            Invalidate();
        }
    }

    public PluginListItem()
    {
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        _animationTimer.Tick += (_, _) => Animate();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _direction = 1;
        _animationTimer.Start();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _direction = -1;
        _animationTimer.Start();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location))
        {
            InvokeAction?.Invoke();
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(PaintSurface.ResolveParentColor(this));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var fill = _selected
            ? Color.FromArgb(233, 245, 241)
            : Blend(Color.White, Color.FromArgb(239, 247, 244), _hoverProgress);
        using var body = RoundedPath(new Rectangle(1, 1, Width - 3, Height - 3), 12);
        using var brush = new SolidBrush(fill);
        e.Graphics.FillPath(brush, body);
        using var border = new Pen(_selected ? AccentColor : Color.FromArgb(210, 225, 220), _selected ? 2F : 1F);
        e.Graphics.DrawPath(border, body);

        using var accent = new SolidBrush(AccentColor);
        e.Graphics.FillRectangle(accent, 1, 15, 5, Height - 30);
        PaintSurface.DrawText(e.Graphics, CategoryText, new Font("Microsoft YaHei UI", 7.5F, FontStyle.Bold), new Rectangle(20, 11, Width - 42, 18), AccentColor);
        PaintSurface.DrawText(e.Graphics, TitleText, new Font("Microsoft YaHei UI", 12F, FontStyle.Bold), new Rectangle(20, 31, Width - 42, 30), Theme.Ink);

        var statusRect = new Rectangle(20, Height - 29, Width - 40, 20);
        using var statusBrush = new SolidBrush(StatusColor);
        e.Graphics.FillEllipse(statusBrush, statusRect.Left, statusRect.Top + 6, 8, 8);
        PaintSurface.DrawText(e.Graphics, StatusText, new Font("Microsoft YaHei UI", 8F, FontStyle.Bold), new Rectangle(statusRect.Left + 15, statusRect.Top, statusRect.Width - 15, statusRect.Height), StatusColor);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _animationTimer.Dispose();
        }
        base.Dispose(disposing);
    }

    private void Animate()
    {
        _hoverProgress = Math.Clamp(_hoverProgress + _direction * 0.12F, 0F, 1F);
        if (_hoverProgress is 0F or 1F)
        {
            _animationTimer.Stop();
        }
        Invalidate();
    }

    private static Color Blend(Color from, Color to, float amount)
        => Color.FromArgb(
            (int)(from.R + (to.R - from.R) * amount),
            (int)(from.G + (to.G - from.G) * amount),
            (int)(from.B + (to.B - from.B) * amount));

    private static GraphicsPath RoundedPath(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class GpuMeter : Control
{
    private int _used;
    private int _total = 1;

    public void SetValue(int used, int total)
    {
        _used = Math.Max(0, used);
        _total = Math.Max(1, total);
        Invalidate();
    }

    public GpuMeter()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var track = new SolidBrush(Color.FromArgb(35, 123, 115));
        using var trackPath = RoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), Height / 2);
        e.Graphics.FillPath(track, trackPath);
        var fillWidth = Math.Max(4, (int)Math.Round((Width - 1) * Math.Clamp(_used / (double)_total, 0D, 1D)));
        using var fill = new SolidBrush(_used / (double)_total > 0.82D ? Theme.Coral : Color.FromArgb(102, 213, 177));
        using var fillPath = RoundedPath(new Rectangle(0, 0, fillWidth, Height - 1), Height / 2);
        e.Graphics.FillPath(fill, fillPath);
    }

    private static GraphicsPath RoundedPath(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Max(2, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed record LauncherLogEntry(DateTime Timestamp, string Message, string? ServiceName, bool IsError);

internal enum LauncherLogFilter
{
    All,
    Errors,
    CurrentService
}

internal enum LauncherUpdateChoice
{
    Install,
    Later,
    Skip,
    Cancel
}

internal sealed class LauncherForm : Form
{
    private static readonly string WindowsPowerShellPath = Path.Combine(
        Environment.GetEnvironmentVariable("WINDIR") ?? @"C:\Windows",
        "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
    private static readonly string SettingsPath = Path.Combine(
        LauncherPaths.UserConfigDirectory,
        "launcher.settings.json");
    private static readonly string ModelCatalogPath = Path.Combine(
        LauncherPaths.UserConfigDirectory,
        "models.json");
    private static readonly string TrustedPublishersPath = Path.Combine(
        LauncherPaths.UserConfigDirectory,
        "trusted-publishers.json");
    private static readonly string LauncherVersion =
        typeof(LauncherForm).Assembly.GetName().Version?.ToString(3) ?? "0.9.0";

    private static readonly HttpClient GitHubClient = CreateGitHubClient();
    private readonly PluginPackageService _pluginPackageService = new(GitHubClient);
    private readonly GitHubUpdateService _sourceUpdateService = new(GitHubClient);
    private readonly LauncherSelfUpdateService _launcherUpdateService = new(GitHubClient);
    private readonly LauncherDiagnosticsService _diagnosticsService = new();
    private LauncherSettings _settings;
    private LauncherModelCatalog _modelCatalog = new();
    private TrustedPublisherStore _trustedPublishers = new();
    private GitHubUpdateSource[] _updateSources = [];

    private readonly SafeTextLabel _statusLabel = new();
    private readonly SafeTextLabel _phaseLabel = new();
    private readonly RichTextBox _log = new();
    private readonly List<LauncherLogEntry> _logEntries = [];
    private readonly Dictionary<string, ServiceRuntimeState> _runtimeStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PluginListItem> _pluginItems = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Windows.Forms.Timer _gpuRefreshTimer = new() { Interval = 5000 };
    private readonly System.Windows.Forms.Timer _logAnimationTimer = new() { Interval = 15 };
    private RoundedButton _openButton = new();
    private RoundedButton? _detailPrimaryButton;
    private RoundedButton? _logToggleButton;
    private SafeTextLabel? _gpuSummaryLabel;
    private SafeTextLabel? _detailTitleLabel;
    private ParagraphLabel? _detailDescriptionLabel;
    private SafeTextLabel? _detailStatusLabel;
    private ParagraphLabel? _detailRootLabel;
    private SafeTextLabel? _detailPortLabel;
    private SafeTextLabel? _detailMemoryLabel;
    private SafeTextLabel? _detailVersionLabel;
    private ParagraphLabel? _detailDependencyLabel;
    private SafeTextLabel? _detailTrustLabel;
    private SafeTextLabel? _logSummaryLabel;
    private SafeTextLabel? _pluginCountLabel;
    private FlowLayoutPanel? _stableModePanel;
    private FlowLayoutPanel? _pluginList;
    private TextBox? _pluginSearchBox;
    private ComboBox? _pluginCategoryFilter;
    private Panel? _logHost;
    private GpuMeter? _gpuMeter;
    private List<PluginUiEntry> _pluginEntries = [];
    private string _selectedPluginId = "woosh-dflow";
    private bool _logExpanded;
    private ServiceProfile? _activeService;
    private Process? _activeProcess;
    private bool _useEnglish;
    private bool _updateBusy;
    private string _phaseChinese = "未启动服务";
    private string _phaseEnglish = "No service started";
    private LauncherLogFilter _logFilter = LauncherLogFilter.All;
    private string _pluginSearchQuery = string.Empty;
    private string _pluginCategory = "*";

    private ServiceProfile _woosh = null!;
    private ServiceProfile _smallSfx = null!;
    private ServiceProfile _smallMusic = null!;
    private ServiceProfile _medium = null!;
    private ServiceProfile _indexTts = null!;
    private ServiceProfile _selectedStableProfile = null!;
    private string _backgroundUpdateStatusChinese = string.Empty;
    private string _backgroundUpdateStatusEnglish = string.Empty;
    private ContextMenuStrip? _maintenanceMenu;

    public LauncherForm()
    {
        AutoScaleMode = AutoScaleMode.None;
        Directory.CreateDirectory(LauncherPaths.UserConfigDirectory);
        MigrateLegacyConfiguration();
        _settings = File.Exists(SettingsPath) ? LoadSettings() : CreateFirstRunSettings();
        NormalizeSettings(_settings);
        EnsureDataDirectories(_settings);
        SaveSettings(_settings);
        _modelCatalog = LoadModelCatalog();
        _trustedPublishers = TrustedPublisherStoreService.Load(TrustedPublishersPath);
        MigrateRenamedCatalogPaths(_modelCatalog);
        SyncBuiltInCatalogEntries();
        SaveModelCatalog(_modelCatalog);
        ArchiveMigratedLegacyConfiguration(_settings);
        ConfigureProfiles();
        _selectedStableProfile = _smallSfx;
        InitializeUi();
        RefreshStatus();
        _gpuRefreshTimer.Tick += (_, _) => UpdateGpuIndicator();
        _logAnimationTimer.Tick += (_, _) => AnimateLogDrawer();
        Shown += async (_, _) =>
        {
            _gpuRefreshTimer.Start();
            await CheckUpdatesInBackgroundAsync();
            await CheckLauncherUpdateInBackgroundAsync();
        };
        FormClosed += (_, _) =>
        {
            _gpuRefreshTimer.Dispose();
            _logAnimationTimer.Dispose();
        };
        FormClosing += HandleLauncherFormClosing;
    }

    private void ConfigureProfiles()
    {
        var updateSources = new List<GitHubUpdateSource>
        {
            new("Woosh", "SonyResearch/Woosh", "main", _settings.WooshRoot,
                ["gradio_Woosh-DFlow.py", "Start-Woosh-DFlow.cmd", "woosh-model-downloads.txt", "woosh-source.zip"],
                ["pyproject.toml", "uv.lock"]),
            new("Stable Audio 3", "Stability-AI/stable-audio-3", "main", _settings.StableRoot,
                ["run_gradio.py", "LOCAL_DEPLOYMENT.md", "run-local-server.cmd", "start-small-sfx.cmd", "start-small-music.cmd", "start-medium.cmd", "stop-local-server.cmd", "verify-install.cmd", "hf-login.cmd"],
                ["pyproject.toml", "uv.lock"]),
            new("IndexTTS2", "index-tts/index-tts", "main", _settings.IndexTtsRoot,
                ["README.md", "webui.py", "gen_subtitle.py", "tools/windows_launcher.ps1", "Start-IndexTTS.bat", "User-Guide.txt"],
                ["pyproject.toml", "uv.lock"])
        };
        foreach (var definition in CustomModelDefinitions().Where(definition => !string.IsNullOrWhiteSpace(definition.GitHubRepository)))
        {
            updateSources.Add(new GitHubUpdateSource(
                definition.DisplayName,
                definition.GitHubRepository.Trim(),
                string.IsNullOrWhiteSpace(definition.GitHubBranch) ? "main" : definition.GitHubBranch.Trim(),
                definition.RootDirectory,
                [],
                ["pyproject.toml", "requirements.txt", "uv.lock"]));
        }
        _updateSources = updateSources.ToArray();

        _woosh = new ServiceProfile(
            "Woosh-DFlow（音效）",
            "Sony 文本生成音效 / 环境声",
            _settings.WooshRoot,
            Path.Combine(_settings.WooshRoot, ".venv", "Scripts", "python.exe"),
            $"gradio_Woosh-DFlow.py --server-name 127.0.0.1 --server-port {_settings.WooshPort}",
            _settings.WooshPort,
            RequiredFiles: ["gradio_Woosh-DFlow.py", "checkpoints"],
            RecommendedVramMiB: 6800,
            RecommendedSystemMemoryMiB: 16384,
            Dependencies: ["python>=3.10", "cuda"]);

        _smallSfx = CreateStableProfile("Stable Audio 3 · small-sfx", "Stable Audio 3 短音效生成", "small-sfx");
        _smallMusic = CreateStableProfile("Stable Audio 3 · small-music", "Stable Audio 3 音乐生成", "small-music");
        _medium = CreateStableProfile("Stable Audio 3 · medium", "高质量模型，显存需求高", "medium", true);

        _indexTts = new ServiceProfile(
            "IndexTTS2（角色语音）",
            "音色克隆与情绪化角色语音",
            _settings.IndexTtsRoot,
            WindowsPowerShellPath,
            $"-NoProfile -ExecutionPolicy Bypass -File \"{Path.Combine(_settings.IndexTtsRoot, "tools", "windows_launcher.ps1")}\" -PreferredPort {_settings.IndexTtsPort}",
            _settings.IndexTtsPort,
            RequiredFiles: ["tools/windows_launcher.ps1", "webui.py", "checkpoints"],
            RecommendedVramMiB: 7500,
            RecommendedSystemMemoryMiB: 16384,
            Dependencies: ["python>=3.10", "cuda"]);
    }

    private IEnumerable<LauncherModelDefinition> CustomModelDefinitions()
        => _modelCatalog.Models.Where(definition => !definition.IsBuiltIn);

    private static ServiceProfile CreateCustomProfile(LauncherModelDefinition definition)
    {
        var root = definition.RootDirectory.Trim();
        var executable = ExpandModelValue(definition.Executable, root, definition.Port);
        if (!Path.IsPathRooted(executable))
        {
            executable = Path.Combine(root, executable);
        }
        return new ServiceProfile(
            definition.DisplayName,
            definition.Description,
            root,
            executable,
            ExpandModelValue(definition.Arguments, root, definition.Port),
            definition.Port,
            definition.IsHighVram,
            definition.RequiredFiles ?? [],
            definition.RecommendedVramMiB,
            definition.RecommendedSystemMemoryMiB,
            definition.Dependencies ?? []);
    }

    private static string ExpandModelValue(string value, string root, int port)
    {
        return Environment.ExpandEnvironmentVariables(value ?? string.Empty)
            .Replace("{root}", root, StringComparison.OrdinalIgnoreCase)
            .Replace("{port}", port.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private string L(string chinese, string english) => _useEnglish ? english : chinese;

    private void SetRuntimePhase(string chinese, string english)
    {
        _phaseChinese = chinese;
        _phaseEnglish = english;
        if (!IsDisposed)
        {
            _phaseLabel.Text = L(_phaseChinese, _phaseEnglish);
            _phaseLabel.Invalidate();
        }
    }

    private void RenderLog()
    {
        if (_log.IsDisposed)
        {
            return;
        }
        var entries = (_logFilter switch
        {
            LauncherLogFilter.Errors => _logEntries.Where(entry => entry.IsError),
            LauncherLogFilter.CurrentService when _activeService is not null => _logEntries.Where(entry => entry.ServiceName == _activeService.Name),
            LauncherLogFilter.CurrentService => Enumerable.Empty<LauncherLogEntry>(),
            _ => _logEntries
        }).ToList();
        _log.Text = string.Concat(entries.Select(entry => $"[{entry.Timestamp:HH:mm:ss}] {entry.Message}{Environment.NewLine}"));
        _log.SelectionStart = _log.TextLength;
        _log.ScrollToCaret();
        if (_logSummaryLabel is not null)
        {
            var latest = entries.LastOrDefault();
            _logSummaryLabel.Text = latest is null
                ? L("暂无运行消息", "No runtime messages")
                : $"[{latest.Timestamp:HH:mm:ss}] {latest.Message}";
            _logSummaryLabel.ForeColor = latest?.IsError == true ? Color.FromArgb(244, 158, 151) : Color.FromArgb(166, 202, 195);
        }
    }

    private void SetLogFilter(LauncherLogFilter filter)
    {
        _logFilter = filter;
        RenderLog();
    }

    private void ToggleLanguage()
    {
        _useEnglish = !_useEnglish;
        _pluginCategory = "*";
        Controls.Clear();
        InitializeUi();
        _openButton.Enabled = _activeService is not null;
        RefreshStatus();
    }

    private ServiceProfile CreateStableProfile(string name, string description, string model, bool isMedium = false)
    {
        return new ServiceProfile(
            name,
            description,
            _settings.StableRoot,
            Path.Combine(_settings.StableRoot, ".venv", "Scripts", "python.exe"),
            $"run_gradio.py --model {model} --port {_settings.StablePort}",
            _settings.StablePort,
            isMedium,
            ["run_gradio.py", "stable_audio_3"],
            isMedium ? 8800 : 2200,
            isMedium ? 16384 : 8192,
            ["python>=3.10", "cuda"]);
    }

    private static void MigrateLegacyConfiguration()
    {
        Directory.CreateDirectory(LauncherPaths.UserConfigDirectory);
        if (LauncherPaths.UsesConfigOverride)
        {
            return;
        }
        CopyFirstExistingFile(SettingsPath,
            Path.Combine(LauncherPaths.LegacyUserConfigDirectory, "launcher.settings.json"),
            LauncherPaths.BrandedLegacySettingsPath,
            LauncherPaths.LegacySettingsPath);
        CopyFirstExistingFile(ModelCatalogPath,
            Path.Combine(LauncherPaths.LegacyUserConfigDirectory, "models.json"),
            LauncherPaths.BrandedLegacyModelCatalogPath,
            LauncherPaths.LegacyModelCatalogPath);
    }

    private static void CopyFirstExistingFile(string destinationPath, params string[] candidates)
    {
        if (File.Exists(destinationPath))
        {
            return;
        }
        var sourcePath = candidates.FirstOrDefault(File.Exists);
        if (sourcePath is not null)
        {
            File.Copy(sourcePath, destinationPath, overwrite: false);
            WriteMigrationLog($"Configuration migrated from {sourcePath} to {destinationPath}");
        }
    }

    private static LauncherSettings CreateFirstRunSettings()
    {
        var dataRoot = LauncherPaths.DefaultDataDirectory;
        if (!LauncherPaths.UsesDataOverride)
        {
            using var picker = new FolderBrowserDialog
            {
                Description = "Choose where BaChen AI Launcher should store plugins, models, logs, and downloads.",
                InitialDirectory = dataRoot,
                SelectedPath = dataRoot,
                ShowNewFolderButton = true,
                UseDescriptionForTitle = true
            };
            if (picker.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(picker.SelectedPath))
            {
                dataRoot = picker.SelectedPath;
            }
        }
        return CreateSettingsForDataRoot(dataRoot);
    }

    private static LauncherSettings CreateSettingsForDataRoot(string dataRoot)
    {
        var normalizedRoot = Path.GetFullPath(dataRoot);
        var plugins = Path.Combine(normalizedRoot, "plugins");
        return new LauncherSettings
        {
            SchemaVersion = 2,
            DataRoot = normalizedRoot,
            WooshRoot = Path.Combine(plugins, "Woosh"),
            StableRoot = Path.Combine(plugins, "Stable Audio 3"),
            IndexTtsRoot = Path.Combine(plugins, "IndexTTS"),
            WooshPort = 7860,
            StablePort = 7861,
            IndexTtsPort = 7862
        };
    }

    private static void NormalizeSettings(LauncherSettings settings)
    {
        settings.SchemaVersion = 2;
        settings.DataRoot = MigrateRenamedPath(settings.DataRoot);
        settings.WooshRoot = MigrateRenamedPath(settings.WooshRoot);
        settings.StableRoot = MigrateRenamedPath(settings.StableRoot);
        settings.IndexTtsRoot = MigrateRenamedPath(settings.IndexTtsRoot);
        var shouldInferRoot = string.IsNullOrWhiteSpace(settings.DataRoot)
            || Path.GetFullPath(settings.DataRoot).Equals(Path.GetFullPath(LauncherPaths.DefaultDataDirectory), StringComparison.OrdinalIgnoreCase);
        var inferredRoot = shouldInferRoot ? InferExistingDataRoot(settings) : null;
        if (!string.IsNullOrWhiteSpace(inferredRoot))
        {
            settings.DataRoot = inferredRoot;
        }
        if (string.IsNullOrWhiteSpace(settings.DataRoot))
        {
            settings.DataRoot = LauncherPaths.DefaultDataDirectory;
        }
        settings.DataRoot = Path.GetFullPath(settings.DataRoot);
        var plugins = Path.Combine(settings.DataRoot, "plugins");
        settings.WooshRoot = string.IsNullOrWhiteSpace(settings.WooshRoot) ? Path.Combine(plugins, "Woosh") : Path.GetFullPath(settings.WooshRoot);
        settings.StableRoot = string.IsNullOrWhiteSpace(settings.StableRoot) ? Path.Combine(plugins, "Stable Audio 3") : Path.GetFullPath(settings.StableRoot);
        settings.IndexTtsRoot = string.IsNullOrWhiteSpace(settings.IndexTtsRoot) ? Path.Combine(plugins, "IndexTTS") : Path.GetFullPath(settings.IndexTtsRoot);
    }

    private static string MigrateRenamedPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }
        var fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath) || Directory.Exists(fullPath))
        {
            return fullPath;
        }
        var migrated = fullPath.Replace("Bachen AI Audio", "BaChen AI Launcher", StringComparison.OrdinalIgnoreCase);
        return File.Exists(migrated) || Directory.Exists(migrated) ? migrated : fullPath;
    }

    private static void MigrateRenamedCatalogPaths(LauncherModelCatalog catalog)
    {
        foreach (var model in catalog.Models)
        {
            model.RootDirectory = MigrateRenamedPath(model.RootDirectory);
            if (!string.IsNullOrWhiteSpace(model.Executable) && Path.IsPathRooted(model.Executable))
            {
                model.Executable = MigrateRenamedPath(model.Executable);
            }
        }
    }

    private static string? InferExistingDataRoot(LauncherSettings settings)
    {
        if (!Directory.Exists(settings.WooshRoot) || !Directory.Exists(settings.StableRoot) || !Directory.Exists(settings.IndexTtsRoot))
        {
            return null;
        }
        var parents = new[] { settings.WooshRoot, settings.StableRoot, settings.IndexTtsRoot }
            .Select(path => Directory.GetParent(Path.GetFullPath(path))?.FullName)
            .ToArray();
        if (parents.Any(string.IsNullOrWhiteSpace) || !parents.All(path => path!.Equals(parents[0], StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }
        var pluginDirectory = parents[0]!;
        return Path.GetFileName(pluginDirectory).Equals("plugins", StringComparison.OrdinalIgnoreCase)
            ? Directory.GetParent(pluginDirectory)?.FullName
            : null;
    }

    private static void EnsureDataDirectories(LauncherSettings settings)
    {
        Directory.CreateDirectory(settings.DataRoot);
        foreach (var relative in new[] { "plugins", "downloads", "logs", "backups" })
        {
            Directory.CreateDirectory(Path.Combine(settings.DataRoot, relative));
        }
    }

    private static void ArchiveMigratedLegacyConfiguration(LauncherSettings settings)
    {
        if (LauncherPaths.UsesConfigOverride ||
            (!File.Exists(LauncherPaths.LegacySettingsPath) && !File.Exists(LauncherPaths.LegacyModelCatalogPath)))
        {
            return;
        }

        try
        {
            _ = JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(SettingsPath))
                ?? throw new InvalidDataException("The migrated launcher settings could not be parsed.");
            _ = JsonSerializer.Deserialize<LauncherModelCatalog>(File.ReadAllText(ModelCatalogPath))
                ?? throw new InvalidDataException("The migrated model catalog could not be parsed.");

            var backupDirectory = Path.Combine(settings.DataRoot, "backups", "legacy-config");
            Directory.CreateDirectory(backupDirectory);
            MoveLegacyFile(LauncherPaths.LegacySettingsPath, backupDirectory);
            MoveLegacyFile(LauncherPaths.LegacyModelCatalogPath, backupDirectory);
            WriteMigrationLog($"Legacy configuration archived to {backupDirectory}");
        }
        catch (Exception ex)
        {
            WriteMigrationLog($"Legacy configuration migration failed. Original files were preserved. {ex.Message}");
        }
    }

    private static void WriteMigrationLog(string message)
    {
        try
        {
            var line = $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}";
            File.AppendAllText(Path.Combine(LauncherPaths.UserConfigDirectory, "migration.log"), line, Encoding.UTF8);
        }
        catch
        {
            // Migration must never fail because its diagnostic log is unavailable.
        }
    }

    private static void MoveLegacyFile(string sourcePath, string backupDirectory)
    {
        if (!File.Exists(sourcePath))
        {
            return;
        }

        var targetPath = Path.Combine(backupDirectory, Path.GetFileName(sourcePath));
        if (File.Exists(targetPath))
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            targetPath = Path.Combine(
                backupDirectory,
                $"{Path.GetFileNameWithoutExtension(sourcePath)}-{timestamp}{Path.GetExtension(sourcePath)}");
        }
        File.Move(sourcePath, targetPath);
    }

    private static LauncherSettings LoadSettings()
        => LauncherConfigurationStore.LoadOrCreate(SettingsPath, () => new LauncherSettings());

    private static void SaveSettings(LauncherSettings settings)
        => LauncherConfigurationStore.SaveAtomic(SettingsPath, settings);

    private LauncherModelCatalog LoadModelCatalog()
    {
        var loaded = LauncherConfigurationStore.LoadOrCreate(ModelCatalogPath, CreateDefaultModelCatalog);
        return loaded.Models.Count > 0 ? loaded : CreateDefaultModelCatalog();
    }

    private LauncherModelCatalog CreateDefaultModelCatalog()
    {
        return new LauncherModelCatalog
        {
            SchemaVersion = 2,
            Models =
            [
                new LauncherModelDefinition { Id = "woosh-dflow", DisplayName = "Woosh-DFlow", Description = "Text to sound effects and ambience", Category = "Sound design", RootDirectory = _settings.WooshRoot, Port = _settings.WooshPort, RecommendedVramMiB = 6800, RecommendedSystemMemoryMiB = 16384, RequiredFiles = ["gradio_Woosh-DFlow.py", "checkpoints"], Dependencies = ["python>=3.10", "cuda"], GitHubRepository = "SonyResearch/Woosh", InstalledVersion = "built-in", Publisher = "SonyResearch", TrustSource = "BuiltIn", IsManifestTrusted = true, IsBuiltIn = true },
                new LauncherModelDefinition { Id = "stable-audio-3", DisplayName = "Stable Audio 3", Description = "Sound effects, music, and medium generation", Category = "Audio generation", RootDirectory = _settings.StableRoot, Port = _settings.StablePort, RecommendedVramMiB = 2200, RecommendedSystemMemoryMiB = 8192, RequiredFiles = ["run_gradio.py", "stable_audio_3"], Dependencies = ["python>=3.10", "cuda"], GitHubRepository = "Stability-AI/stable-audio-3", InstalledVersion = "built-in", Publisher = "Stability AI", TrustSource = "BuiltIn", IsManifestTrusted = true, IsBuiltIn = true },
                new LauncherModelDefinition { Id = "indextts2", DisplayName = "IndexTTS2", Description = "Character voice and emotional speech", Category = "Character voice", RootDirectory = _settings.IndexTtsRoot, Port = _settings.IndexTtsPort, RecommendedVramMiB = 7500, RecommendedSystemMemoryMiB = 16384, RequiredFiles = ["tools/windows_launcher.ps1", "webui.py", "checkpoints"], Dependencies = ["python>=3.10", "cuda"], GitHubRepository = "index-tts/index-tts", InstalledVersion = "built-in", Publisher = "IndexTTS", TrustSource = "BuiltIn", IsManifestTrusted = true, IsBuiltIn = true }
            ]
        };
    }

    private static void SaveModelCatalog(LauncherModelCatalog catalog)
    {
        catalog.SchemaVersion = 2;
        LauncherConfigurationStore.SaveAtomic(ModelCatalogPath, catalog);
    }

    private void SyncBuiltInCatalogEntries()
    {
        var defaults = CreateDefaultModelCatalog().Models;
        foreach (var definition in defaults)
        {
            var existing = _modelCatalog.Models.FirstOrDefault(item => item.Id.Equals(definition.Id, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                _modelCatalog.Models.Add(definition);
                continue;
            }
            existing.DisplayName = definition.DisplayName;
            existing.Description = definition.Description;
            existing.Category = definition.Category;
            existing.RootDirectory = definition.RootDirectory;
            existing.Port = definition.Port;
            existing.RecommendedVramMiB = definition.RecommendedVramMiB;
            existing.RecommendedSystemMemoryMiB = definition.RecommendedSystemMemoryMiB;
            existing.RequiredFiles = definition.RequiredFiles;
            existing.Dependencies = definition.Dependencies;
            existing.GitHubRepository = definition.GitHubRepository;
            if (string.IsNullOrWhiteSpace(existing.InstalledVersion) || existing.InstalledVersion == "local")
            {
                existing.InstalledVersion = definition.InstalledVersion;
            }
            existing.Publisher = definition.Publisher;
            existing.TrustSource = definition.TrustSource;
            existing.IsManifestTrusted = true;
            existing.IsBuiltIn = true;
        }
    }

    private static HttpClient CreateGitHubClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"BaChen-AI-Launcher/{LauncherVersion}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private async Task CheckUpdatesAsync()
    {
        if (_updateBusy)
        {
            return;
        }

        _updateBusy = true;
        SetRuntimePhase("正在检查 GitHub 源码更新", "Checking GitHub source updates");
        AppendLog(L("正在检查 GitHub 源码更新……", "Checking GitHub source updates..."));
        try
        {
            var checks = new List<SourceUpdateCheck>();
            foreach (var source in _updateSources)
            {
                checks.Add(await FetchUpdateCheckAsync(source));
            }

            var lines = checks.Select(check =>
            {
                var shortSha = check.LatestSha[..Math.Min(8, check.LatestSha.Length)];
                if (!check.HasLocalBaseline)
                {
                    return L(
                        $"{check.Source.DisplayName}：尚未建立更新记录，GitHub 最新 {shortSha}（{check.LatestDate:yyyy-MM-dd}）",
                        $"{check.Source.DisplayName}: not tracked yet; latest {shortSha} ({check.LatestDate:yyyy-MM-dd})");
                }
                return check.UpdateAvailable
                    ? L($"{check.Source.DisplayName}：发现更新 {shortSha}（{check.LatestDate:yyyy-MM-dd}）", $"{check.Source.DisplayName}: update available {shortSha} ({check.LatestDate:yyyy-MM-dd})")
                    : L($"{check.Source.DisplayName}：已是记录中的最新版本", $"{check.Source.DisplayName}: up to date");
            });
            var summary = string.Join(Environment.NewLine, lines);
            AppendLog(summary.Replace(Environment.NewLine, " | "));
            SetRuntimePhase("更新检查完成", "Update check complete");
            MessageBox.Show(summary, L("GitHub 更新检查", "GitHub update check"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppendLog(L($"检查更新失败：{ex.Message}", $"Update check failed: {ex.Message}"));
            SetRuntimePhase("更新检查失败", "Update check failed");
            MessageBox.Show(ex.Message, L("无法检查更新", "Unable to check updates"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _updateBusy = false;
        }
    }

    private async Task CheckLauncherUpdateAsync()
    {
        if (_updateBusy)
        {
            return;
        }
        _updateBusy = true;
        try
        {
            SetRuntimePhase("正在检查启动器更新", "Checking launcher updates");
            AppendLog(L("正在验证启动器更新清单……", "Verifying launcher update manifest..."));
            var check = await _launcherUpdateService.CheckAsync(_settings.LauncherUpdateChannel);
            if (!check.IsUpdateAvailable)
            {
                MessageBox.Show(L($"当前版本 {check.CurrentVersion.ToString(3)} 已是最新版本。", $"Version {check.CurrentVersion.ToString(3)} is up to date."), L("启动器更新", "Launcher update"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var choice = ShowLauncherUpdatePrompt(check);
            if (choice == LauncherUpdateChoice.Skip)
            {
                _settings.SkippedLauncherVersion = check.LatestVersion.ToString(3);
                SaveSettings(_settings);
                return;
            }
            if (choice == LauncherUpdateChoice.Later)
            {
                _settings.LauncherUpdateDeferredUntil = DateTimeOffset.Now.AddHours(24);
                SaveSettings(_settings);
                return;
            }
            if (choice != LauncherUpdateChoice.Install) return;
            await InstallLauncherUpdateAsync(check);
        }
        catch (Exception ex)
        {
            var message = DescribeLauncherUpdateError(ex);
            AppendLog(L($"启动器更新失败：{message}", $"Launcher update failed: {message}"), null, true);
            MessageBox.Show(message, L("启动器更新失败", "Launcher update failed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _updateBusy = false;
            SetRuntimePhase("启动器更新检查完成", "Launcher update check complete");
        }
    }

    private async Task CheckLauncherUpdateInBackgroundAsync()
    {
        if (!_settings.AutomaticallyCheckLauncherUpdates || _updateBusy ||
            _settings.LauncherUpdateDeferredUntil > DateTimeOffset.Now) return;
        _updateBusy = true;
        try
        {
            var check = await _launcherUpdateService.CheckAsync(_settings.LauncherUpdateChannel);
            if (!check.IsUpdateAvailable || _settings.SkippedLauncherVersion == check.LatestVersion.ToString(3)) return;
            var choice = ShowLauncherUpdatePrompt(check);
            if (choice == LauncherUpdateChoice.Install)
            {
                await InstallLauncherUpdateAsync(check);
            }
            else if (choice == LauncherUpdateChoice.Later)
            {
                _settings.LauncherUpdateDeferredUntil = DateTimeOffset.Now.AddHours(24);
                SaveSettings(_settings);
            }
            else if (choice == LauncherUpdateChoice.Skip)
            {
                _settings.SkippedLauncherVersion = check.LatestVersion.ToString(3);
                SaveSettings(_settings);
            }
        }
        catch (Exception ex)
        {
            AppendLog(L($"自动更新检查跳过：{DescribeLauncherUpdateError(ex)}", $"Automatic update check skipped: {DescribeLauncherUpdateError(ex)}"));
        }
        finally
        {
            _updateBusy = false;
        }
    }

    private async Task InstallLauncherUpdateAsync(LauncherUpdateCheck check)
    {
            SetRuntimePhase("正在下载并校验启动器", "Downloading and verifying launcher");
            var packagePath = await _launcherUpdateService.DownloadVerifiedAsync(check.Manifest);
            AppendLog(L($"启动器 {check.LatestVersion.ToString(3)} 校验通过，准备重启。", $"Launcher {check.LatestVersion.ToString(3)} verified; preparing to restart."));
            LauncherSelfUpdateService.BeginApply(packagePath, check.Manifest);
            Application.Exit();
    }

    private LauncherUpdateChoice ShowLauncherUpdatePrompt(LauncherUpdateCheck check)
    {
        using var dialog = new Form { Text = L("发现启动器更新", "Launcher update available"), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, ClientSize = new Size(680, 250), MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false, BackColor = Theme.Card, Font = new Font("Microsoft YaHei UI", 10F) };
        var message = new Label { Text = L($"发现版本 {check.LatestVersion.ToString(3)}。更新清单和文件将经过签名与哈希校验，并保留当前版本用于回滚。", $"Version {check.LatestVersion.ToString(3)} is available. The manifest and file will be verified, and the current version will be kept for rollback."), Location = new Point(26, 24), Size = new Size(628, 76), ForeColor = Theme.Ink };
        var notes = new LinkLabel { Text = L("查看发布说明", "View release notes"), Location = new Point(26, 112), Size = new Size(180, 30), LinkColor = Theme.MidTeal };
        notes.Click += (_, _) => Process.Start(new ProcessStartInfo(check.Manifest.ReleaseNotesUrl) { UseShellExecute = true });
        var result = LauncherUpdateChoice.Cancel;
        var install = new Button { Text = L("立即更新", "Install now"), Location = new Point(190, 176), Size = new Size(140, 40) };
        var later = new Button { Text = L("24 小时后提醒", "Remind in 24h"), Location = new Point(340, 176), Size = new Size(140, 40) };
        var skip = new Button { Text = L("跳过此版本", "Skip version"), Location = new Point(490, 176), Size = new Size(140, 40) };
        install.Click += (_, _) => { result = LauncherUpdateChoice.Install; dialog.Close(); };
        later.Click += (_, _) => { result = LauncherUpdateChoice.Later; dialog.Close(); };
        skip.Click += (_, _) => { result = LauncherUpdateChoice.Skip; dialog.Close(); };
        dialog.Controls.AddRange([message, notes, install, later, skip]);
        dialog.ShowDialog(this);
        return result;
    }

    private string DescribeLauncherUpdateError(Exception exception)
    {
        if (exception is LauncherUpdateUnavailableException unavailable)
        {
            return unavailable.Channel == LauncherUpdateChannel.Stable
                ? L("目前没有可用的稳定版。你可以继续使用当前版本，或在设置中切换到预览版通道。", "No stable release is currently available. Keep using this version or switch to the Preview channel in Settings.")
                : L("目前没有可用的预览版，请稍后重试。", "No preview release is currently available. Try again later.");
        }
        if (exception is TaskCanceledException) return L("网络请求超时，请稍后重试。", "The network request timed out. Try again later.");
        if (exception is HttpRequestException) return L("无法连接 GitHub 更新服务，请检查网络或代理设置。", "GitHub update services could not be reached. Check the network or proxy.");
        if (exception is IOException && exception.Message.Contains("disk space", StringComparison.OrdinalIgnoreCase)) return L("磁盘空间不足，无法安全下载更新。", "There is not enough disk space to download the update safely.");
        return exception.Message;
    }

    private async Task RollbackLauncherAsync()
    {
        if (LauncherSelfUpdateService.GetRollbackPath() is null)
        {
            MessageBox.Show(L("没有可恢复的上一版启动器。", "No previous launcher backup is available."), L("恢复启动器", "Restore launcher"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (MessageBox.Show(L("将退出并恢复上一版启动器，当前版本会被保留为新的备份。继续吗？", "The launcher will exit and restore the previous version. The current version becomes the new backup. Continue?"), L("恢复上一版启动器", "Restore previous launcher"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }
        try
        {
            await LauncherSelfUpdateService.BeginRollbackAsync();
            Application.Exit();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, L("恢复失败", "Restore failed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task CheckUpdatesInBackgroundAsync()
    {
        if (_updateBusy)
        {
            return;
        }

        _updateBusy = true;
        try
        {
            var checks = new List<SourceUpdateCheck>();
            foreach (var source in _updateSources)
            {
                checks.Add(await FetchUpdateCheckAsync(source));
            }
            var count = checks.Count(check => check.UpdateAvailable || !check.HasLocalBaseline);
            _backgroundUpdateStatusChinese = count == 0 ? "源码已是最新记录" : $"{count} 个源码可检查更新";
            _backgroundUpdateStatusEnglish = count == 0 ? "Sources match tracked versions" : $"{count} source update(s) available";
            AppendLog(L(_backgroundUpdateStatusChinese, _backgroundUpdateStatusEnglish));
            RefreshStatus();
        }
        catch (Exception ex)
        {
            AppendLog(L($"后台更新检查跳过：{ex.Message}", $"Background update check skipped: {ex.Message}"));
        }
        finally
        {
            _updateBusy = false;
        }
    }

    private async Task UpdateSourcesAsync()
    {
        if (_updateBusy)
        {
            return;
        }

        _updateBusy = true;
        var checks = new List<SourceUpdateCheck>();
        var dependencyChanges = new Dictionary<string, string[]>();
        try
        {
            SetRuntimePhase("正在生成更新预览", "Building update preview");
            AppendLog(L("正在生成更新预览……", "Building update preview..."));
            foreach (var source in _updateSources)
            {
                var check = await FetchUpdateCheckAsync(source);
                checks.Add(check);
                dependencyChanges[source.DisplayName] = check.UpdateAvailable || !check.HasLocalBaseline
                    ? await GetChangedDependencyFilesAsync(source)
                    : [];
            }
        }
        catch (Exception ex)
        {
            _updateBusy = false;
            MessageBox.Show(ex.Message, L("无法生成更新预览", "Unable to build update preview"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var previewLines = new List<string>();
        foreach (var check in checks)
        {
            var shortSha = check.LatestSha[..Math.Min(8, check.LatestSha.Length)];
            var state = check.HasLocalBaseline && !check.UpdateAvailable
                ? L("无需更新", "up to date")
                : L("将更新", "will update");
            previewLines.Add($"{check.Source.DisplayName}: {state}  {shortSha}  {check.LatestDate:yyyy-MM-dd}");
            if (!string.IsNullOrWhiteSpace(check.LatestMessage))
            {
                previewLines.Add("  " + check.LatestMessage);
            }
            if (dependencyChanges[check.Source.DisplayName].Length > 0)
            {
                previewLines.Add("  " + L("依赖文件变化：", "Dependency changes: ") + string.Join(", ", dependencyChanges[check.Source.DisplayName]));
            }
        }
        previewLines.Add(string.Empty);
        previewLines.Add(L("更新前会备份被覆盖文件；模型权重、虚拟环境、输出、日志和本地启动脚本会被保留。", "Overwritten files are backed up; weights, virtual environments, outputs, logs, and local launch scripts are preserved."));
        if (dependencyChanges.Values.Any(files => files.Length > 0))
        {
            previewLines.Add(L("检测到依赖文件变化。更新后请先运行环境自检，依赖不会自动安装。", "Dependency files changed. Run environment check after updating; dependencies are not installed automatically."));
        }

        var confirmation = MessageBox.Show(
            string.Join(Environment.NewLine, previewLines),
            L("更新预览", "Update preview"),
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (confirmation != DialogResult.Yes)
        {
            _updateBusy = false;
            return;
        }

        var runningPids = GetKnownServicePids();
        if (runningPids.Count > 0)
        {
            var stopConfirmation = MessageBox.Show(
                L("检测到 AI 服务正在运行。更新源码前需要停止这些已识别的服务。是否停止并继续？", "AI services are running. Recognized services must be stopped before updating source. Stop them and continue?"),
                L("需要停止服务", "Services must stop"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (stopConfirmation != DialogResult.Yes)
            {
                _updateBusy = false;
                return;
            }
            StopProcesses(runningPids);
            await Task.Delay(700);
        }

        var results = new List<string>();
        try
        {
            foreach (var check in checks)
            {
                var source = check.Source;
                try
                {
                    SetRuntimePhase($"正在更新 {source.DisplayName}", $"Updating {source.DisplayName}");
                    AppendLog(L($"正在更新 {source.DisplayName}……", $"Updating {source.DisplayName}..."));
                    if (check.HasLocalBaseline && !check.UpdateAvailable)
                    {
                        results.Add(L($"{source.DisplayName}：无需更新", $"{source.DisplayName}: already up to date"));
                        continue;
                    }

                    var backupPath = await ApplySourceUpdateAsync(check);
                    var result = L(
                        $"{source.DisplayName}：源码已更新，备份：{backupPath}",
                        $"{source.DisplayName}: source updated; backup: {backupPath}");
                    if (dependencyChanges[source.DisplayName].Length > 0)
                    {
                        result += L("（依赖已变化，请运行环境自检）", " (dependencies changed; run environment check)");
                    }
                    results.Add(result);
                    AppendLog(result);
                }
                catch (Exception ex)
                {
                    var failure = L($"{source.DisplayName}：更新失败（{ex.Message}）", $"{source.DisplayName}: update failed ({ex.Message})");
                    results.Add(failure);
                    AppendLog(failure);
                }
            }

            MessageBox.Show(
                string.Join(Environment.NewLine, results) + Environment.NewLine + Environment.NewLine + L("模型权重与本地运行环境未被替换。", "Model weights and local runtime environments were preserved."),
                L("源码更新结果", "Source update results"),
                MessageBoxButtons.OK,
                results.Any(result => result.Contains(L("失败", "failed"), StringComparison.OrdinalIgnoreCase)) ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }
        finally
        {
            _updateBusy = false;
            SetRuntimePhase("源码更新流程完成", "Source update workflow complete");
            RefreshStatus();
        }
    }

    private static string UpdateStatePath(GitHubUpdateSource source) => GitHubUpdateService.UpdateStatePath(source);
    private SourceUpdateState? LoadUpdateState(GitHubUpdateSource source) => _sourceUpdateService.LoadState(source);
    private void SaveUpdateState(GitHubUpdateSource source, string commitSha) => _sourceUpdateService.SaveState(source, commitSha);
    private Task<SourceUpdateCheck> FetchUpdateCheckAsync(GitHubUpdateSource source) => _sourceUpdateService.FetchCheckAsync(source);
    private Task<string[]> GetChangedDependencyFilesAsync(GitHubUpdateSource source) => _sourceUpdateService.GetChangedDependencyFilesAsync(source);

    private async Task<string> ApplySourceUpdateAsync(SourceUpdateCheck check)
    {
        var source = check.Source;
        var tempRoot = Path.Combine(Path.GetTempPath(), "bachen-ai-update-" + Guid.NewGuid().ToString("N"));
        var archivePath = Path.Combine(tempRoot, "source.zip");
        var extractPath = Path.Combine(tempRoot, "extract");
        var backupStaging = Path.Combine(tempRoot, "backup");
        Directory.CreateDirectory(tempRoot);
        try
        {
            using (var response = await GitHubClient.GetAsync($"https://github.com/{source.Repository}/archive/refs/heads/{source.Branch}.zip", HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                await using var input = await response.Content.ReadAsStreamAsync();
                await using var output = new FileStream(archivePath, FileMode.Create, FileAccess.Write, FileShare.None);
                await input.CopyToAsync(output);
            }

            ZipFile.ExtractToDirectory(archivePath, extractPath);
            var extractedRoot = Directory.GetDirectories(extractPath).SingleOrDefault()
                ?? throw new InvalidDataException("The GitHub archive did not contain a source directory.");
            Directory.CreateDirectory(backupStaging);
            foreach (var sourceFile in Directory.EnumerateFiles(extractedRoot, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(extractedRoot, sourceFile).Replace('\\', '/');
                if (ShouldPreserveUpdatePath(source, relative))
                {
                    continue;
                }

                var destination = Path.Combine(source.DeploymentRoot, relative.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(destination))
                {
                    var backupFile = Path.Combine(backupStaging, relative.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(backupFile)!);
                    File.Copy(destination, backupFile, true);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(sourceFile, destination, true);
            }

            var backupsRoot = Path.Combine(source.DeploymentRoot, "launcher-update-backups");
            Directory.CreateDirectory(backupsRoot);
            var safeName = source.DisplayName.Replace(' ', '-');
            var backupZip = Path.Combine(backupsRoot, $"{safeName}-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
            var previousState = LoadUpdateState(source);
            var definition = _modelCatalog.Models.FirstOrDefault(model => Path.GetFullPath(model.RootDirectory).Equals(Path.GetFullPath(source.DeploymentRoot), StringComparison.OrdinalIgnoreCase));
            var metadata = JsonSerializer.Serialize(
                new UpdateBackupMetadata(source.DisplayName, previousState?.CommitSha, DateTimeOffset.Now, definition?.InstalledVersion, definition?.Dependencies),
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(backupStaging, ".launcher-backup-metadata.json"), metadata, Encoding.UTF8);
            ZipFile.CreateFromDirectory(backupStaging, backupZip, CompressionLevel.Optimal, false);

            SaveUpdateState(source, check.LatestSha);
            if (definition is not null)
            {
                definition.InstalledVersion = $"git-{check.LatestSha[..Math.Min(8, check.LatestSha.Length)]}";
                SaveModelCatalog(_modelCatalog);
            }
            return backupZip;
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }

    private static bool ShouldPreserveUpdatePath(GitHubUpdateSource source, string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        var firstSegment = normalized.Split('/')[0];
        string[] preservedDirectories = [".venv", ".runtime", ".uv-cache", "checkpoints", "generated_audio", "outputs", "logs", "prompts", "archive", "launcher-update-backups"];
        if (preservedDirectories.Contains(firstSegment, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }
        if (normalized.Equals(".bachen-ai-launcher-update.json", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals(".ai-audio-launcher-update.json", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return source.PreservedFiles.Contains(normalized, StringComparer.OrdinalIgnoreCase);
    }

    private void ShowEnvironmentReport()
    {
        var lines = new List<string>
        {
            L("AI 音频环境自检", "AI audio environment check"),
            new string('-', 42)
        };
        var profiles = new List<ServiceProfile> { _woosh, _smallSfx, _indexTts };
        profiles.AddRange(CustomModelDefinitions().Select(CreateCustomProfile));
        foreach (var profile in profiles)
        {
            var missing = GetMissingRequirements(profile);
            var state = missing.Count == 0 ? L("通过", "PASS") : L("需要处理", "ACTION NEEDED");
            lines.Add($"{profile.Name}: {state}");
            lines.Add($"  {L("目录", "Root")}: {profile.WorkingDirectory}");
            foreach (var item in missing)
            {
                lines.Add("  - " + item);
            }
            var source = _updateSources.FirstOrDefault(item => item.DeploymentRoot.Equals(profile.WorkingDirectory, StringComparison.OrdinalIgnoreCase));
            var updateState = source is null ? null : LoadUpdateState(source);
            if (updateState is not null)
            {
                lines.Add($"  {L("源码记录", "Tracked source")}: {updateState.CommitSha[..Math.Min(8, updateState.CommitSha.Length)]}");
            }
            lines.Add(string.Empty);
        }

        var resources = SystemResourceProbe.Capture();
        lines.Add(resources.GpuTotalMiB is null || resources.GpuUsedMiB is null
            ? L("GPU：无法读取 nvidia-smi", "GPU: nvidia-smi unavailable")
            : L($"GPU 显存：{resources.GpuUsedMiB} / {resources.GpuTotalMiB} MiB", $"GPU memory: {resources.GpuUsedMiB} / {resources.GpuTotalMiB} MiB"));
        lines.Add(L(
            $"系统内存：{resources.AvailableMemoryMiB:N0} / {resources.TotalMemoryMiB:N0} MiB 可用",
            $"System memory: {resources.AvailableMemoryMiB:N0} / {resources.TotalMemoryMiB:N0} MiB available"));
        lines.Add(string.Empty);
        lines.Add(L($"启动器版本：{LauncherVersion}", $"Launcher version: {LauncherVersion}"));
        lines.Add(L($"程序目录：{LauncherPaths.BaseDirectory}", $"Application directory: {LauncherPaths.BaseDirectory}"));
        lines.Add(L($"数据目录：{_settings.DataRoot}", $"Data directory: {_settings.DataRoot}"));
        lines.Add(L($"配置文件：{SettingsPath}", $"Settings: {SettingsPath}"));
        lines.Add(L($"模型清单：{ModelCatalogPath}", $"Model catalog: {ModelCatalogPath}"));

        using var report = new Form
        {
            Text = L("环境自检", "Environment check"),
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(860, 620),
            MinimumSize = new Size(700, 480),
            BackColor = Theme.Card,
            Font = new Font("Microsoft YaHei UI", 10F),
            ShowInTaskbar = false
        };
        var text = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            BackColor = Theme.Card,
            ForeColor = Theme.Ink,
            Font = new Font("Cascadia Mono", 10F),
            Text = string.Join(Environment.NewLine, lines)
        };
        report.Controls.Add(text);
        report.ShowDialog(this);
    }

    private static List<string> GetMissingRequirements(ServiceProfile profile)
    {
        var missing = new List<string>();
        if (!Directory.Exists(profile.WorkingDirectory))
        {
            missing.Add($"Root directory: {profile.WorkingDirectory}");
            return missing;
        }
        if (!File.Exists(profile.Executable))
        {
            missing.Add($"Executable: {profile.Executable}");
        }
        foreach (var relative in profile.RequiredFiles ?? [])
        {
            var path = Path.Combine(profile.WorkingDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                missing.Add(relative);
            }
        }
        foreach (var dependency in PluginDependencyChecker.Check(profile.Dependencies, profile.WorkingDirectory).Where(result => result.IsEnforced && !result.IsSatisfied))
        {
            missing.Add($"{dependency.Requirement}: {dependency.Details}");
        }
        return missing;
    }

    private void ShowSettingsDialog()
    {
        using var dialog = new Form
        {
            Text = L("启动器设置", "Launcher settings"),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            ClientSize = new Size(940, 620),
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            BackColor = Theme.Card,
            Font = new Font("Microsoft YaHei UI", 10F)
        };
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 430,
            Padding = new Padding(24, 24, 24, 8),
            ColumnCount = 3,
            RowCount = 7,
            BackColor = Theme.Card
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 175));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));

        for (var row = 0; row < 7; row++)
        {
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        }

        var dataRootBox = AddPathSettingRow(table, 0, L("数据根目录", "Data directory"), _settings.DataRoot, dialog);
        var wooshBox = AddPathSettingRow(table, 1, "Woosh", _settings.WooshRoot, dialog);
        var stableBox = AddPathSettingRow(table, 2, "Stable Audio 3", _settings.StableRoot, dialog);
        var indexBox = AddPathSettingRow(table, 3, "IndexTTS2", _settings.IndexTtsRoot, dialog);
        var wooshPort = AddPortSettingRow(table, 4, L("Woosh 端口", "Woosh port"), _settings.WooshPort);
        var stablePort = AddPortSettingRow(table, 5, L("Stable 端口", "Stable port"), _settings.StablePort);
        var indexPort = AddPortSettingRow(table, 6, L("IndexTTS2 端口", "IndexTTS2 port"), _settings.IndexTtsPort);

        var previousDataRoot = Path.GetFullPath(_settings.DataRoot);
        var followsDataRoot = new[]
        {
            (_settings.WooshRoot, "Woosh"),
            (_settings.StableRoot, "Stable Audio 3"),
            (_settings.IndexTtsRoot, "IndexTTS")
        }.All(item => Path.GetFullPath(item.Item1).Equals(Path.Combine(previousDataRoot, "plugins", item.Item2), StringComparison.OrdinalIgnoreCase));
        dataRootBox.TextChanged += (_, _) =>
        {
            if (!followsDataRoot || string.IsNullOrWhiteSpace(dataRootBox.Text))
            {
                return;
            }
            var plugins = Path.Combine(dataRootBox.Text.Trim(), "plugins");
            wooshBox.Text = Path.Combine(plugins, "Woosh");
            stableBox.Text = Path.Combine(plugins, "Stable Audio 3");
            indexBox.Text = Path.Combine(plugins, "IndexTTS");
        };
        dialog.Controls.Add(table);

        var automaticUpdates = new CheckBox
        {
            Text = L("启动时自动检查启动器更新", "Automatically check launcher updates at startup"),
            Checked = _settings.AutomaticallyCheckLauncherUpdates,
            Location = new Point(28, 442),
            Size = new Size(520, 32),
            ForeColor = Theme.Ink,
            BackColor = Theme.Card
        };
        dialog.Controls.Add(automaticUpdates);

        var updateChannelLabel = new Label
        {
            Text = L("启动器更新通道", "Launcher update channel"),
            Location = new Point(560, 442),
            Size = new Size(180, 30),
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Theme.Ink,
            BackColor = Theme.Card
        };
        var updateChannel = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(742, 442),
            Size = new Size(170, 30)
        };
        updateChannel.Items.AddRange([L("稳定版", "Stable"), L("预览版", "Preview")]);
        updateChannel.SelectedIndex = _settings.LauncherUpdateChannel == LauncherUpdateChannel.Preview ? 1 : 0;
        dialog.Controls.Add(updateChannelLabel);
        dialog.Controls.Add(updateChannel);

        var note = new Label
        {
            AutoSize = false,
            Text = L("保存只会更新路径，不会移动现有插件或模型文件。", "Saving updates paths only; existing plugins and model files are not moved."),
            ForeColor = Theme.Muted,
            Location = new Point(28, 480),
            Size = new Size(750, 48)
        };
        dialog.Controls.Add(note);
        var cancel = new Button { Text = L("取消", "Cancel"), DialogResult = DialogResult.Cancel, Size = new Size(110, 38), Location = new Point(682, 558) };
        var save = new Button { Text = L("保存", "Save"), DialogResult = DialogResult.OK, Size = new Size(110, 38), Location = new Point(806, 558) };
        dialog.Controls.Add(cancel);
        dialog.Controls.Add(save);
        dialog.AcceptButton = save;
        dialog.CancelButton = cancel;

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var updated = new LauncherSettings
        {
            SchemaVersion = 2,
            DataRoot = dataRootBox.Text.Trim(),
            WooshRoot = wooshBox.Text.Trim(),
            StableRoot = stableBox.Text.Trim(),
            IndexTtsRoot = indexBox.Text.Trim(),
            WooshPort = (int)wooshPort.Value,
            StablePort = (int)stablePort.Value,
            IndexTtsPort = (int)indexPort.Value
            ,AutomaticallyCheckLauncherUpdates = automaticUpdates.Checked
            ,LauncherUpdateChannel = updateChannel.SelectedIndex == 1 ? LauncherUpdateChannel.Preview : LauncherUpdateChannel.Stable
            ,SkippedLauncherVersion = _settings.SkippedLauncherVersion
            ,LauncherUpdateDeferredUntil = _settings.LauncherUpdateDeferredUntil
        };
        if (updated.LauncherUpdateChannel != _settings.LauncherUpdateChannel)
        {
            updated.SkippedLauncherVersion = string.Empty;
            updated.LauncherUpdateDeferredUntil = null;
        }
        var builtInPorts = new[] { updated.WooshPort, updated.StablePort, updated.IndexTtsPort };
        if (builtInPorts.Distinct().Count() != builtInPorts.Length)
        {
            MessageBox.Show(
                L("Woosh、Stable Audio 3 和 IndexTTS 必须使用三个不同端口。", "Woosh, Stable Audio 3, and IndexTTS must use three different ports."),
                L("端口冲突", "Port conflict"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }
        var invalidRoots = new[] { updated.WooshRoot, updated.StableRoot, updated.IndexTtsRoot }.Where(path => !Directory.Exists(path)).ToArray();
        if (invalidRoots.Length > 0 && MessageBox.Show(
                L("以下目录目前不存在：\n", "These directories do not currently exist:\n") + string.Join(Environment.NewLine, invalidRoots) + Environment.NewLine + Environment.NewLine + L("仍然保存吗？", "Save anyway?"),
                L("目录检查", "Directory check"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        NormalizeSettings(updated);
        EnsureDataDirectories(updated);
        _settings = updated;
        SaveSettings(_settings);
        SyncBuiltInCatalogEntries();
        SaveModelCatalog(_modelCatalog);
        ConfigureProfiles();
        _selectedStableProfile = _smallSfx;
        Controls.Clear();
        InitializeUi();
        RefreshStatus();
        AppendLog(L("启动器设置已保存。", "Launcher settings saved."));
    }

    private TextBox AddPathSettingRow(TableLayoutPanel table, int row, string label, string value, Form owner)
    {
        var text = new TextBox { Text = value, Dock = DockStyle.Fill, Margin = new Padding(6, 8, 6, 8) };
        var browse = new Button { Text = L("浏览…", "Browse..."), Dock = DockStyle.Fill, Margin = new Padding(6) };
        browse.Click += (_, _) =>
        {
            using var picker = new FolderBrowserDialog { InitialDirectory = Directory.Exists(text.Text) ? text.Text : Environment.GetFolderPath(Environment.SpecialFolder.Desktop) };
            if (picker.ShowDialog(owner) == DialogResult.OK)
            {
                text.Text = picker.SelectedPath;
            }
        };
        table.Controls.Add(new Label { Text = label, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, row);
        table.Controls.Add(text, 1, row);
        table.Controls.Add(browse, 2, row);
        return text;
    }

    private static NumericUpDown AddPortSettingRow(TableLayoutPanel table, int row, string label, int value)
    {
        var port = new NumericUpDown { Minimum = 1024, Maximum = 65535, Value = Math.Clamp(value, 1024, 65535), Width = 160, Margin = new Padding(6, 8, 6, 8) };
        table.Controls.Add(new Label { Text = label, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, row);
        table.Controls.Add(port, 1, row);
        return port;
    }

    private async Task RestoreBackupAsync()
    {
        var backups = _updateSources
            .SelectMany(source => Directory.Exists(Path.Combine(source.DeploymentRoot, "launcher-update-backups"))
                ? Directory.EnumerateFiles(Path.Combine(source.DeploymentRoot, "launcher-update-backups"), "*.zip")
                    .Select(path => new UpdateBackupEntry(source, path, File.GetLastWriteTime(path)))
                : [])
            .OrderByDescending(item => item.LastWriteTime)
            .ToList();
        if (backups.Count == 0)
        {
            MessageBox.Show(L("没有找到可恢复的 ZIP 备份。", "No restorable ZIP backups were found."), L("恢复备份", "Restore backup"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var picker = new Form
        {
            Text = L("选择要恢复的备份", "Choose a backup to restore"),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            ClientSize = new Size(760, 180),
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            Font = new Font("Microsoft YaHei UI", 10F)
        };
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(24, 42), Size = new Size(710, 34) };
        foreach (var backup in backups)
        {
            combo.Items.Add($"{backup.Source.DisplayName}  |  {backup.LastWriteTime:yyyy-MM-dd HH:mm:ss}  |  {Path.GetFileName(backup.Path)}");
        }
        combo.SelectedIndex = 0;
        var cancel = new Button { Text = L("取消", "Cancel"), DialogResult = DialogResult.Cancel, Location = new Point(500, 112), Size = new Size(110, 36) };
        var restore = new Button { Text = L("恢复", "Restore"), DialogResult = DialogResult.OK, Location = new Point(624, 112), Size = new Size(110, 36) };
        picker.Controls.Add(combo);
        picker.Controls.Add(cancel);
        picker.Controls.Add(restore);
        picker.AcceptButton = restore;
        picker.CancelButton = cancel;
        if (picker.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var selected = backups[combo.SelectedIndex];
        if (MessageBox.Show(
                L($"将把备份恢复到：\n{selected.Source.DeploymentRoot}\n\n当前同名源码文件会被覆盖。继续吗？", $"Restore this backup to:\n{selected.Source.DeploymentRoot}\n\nCurrent source files with matching names will be overwritten. Continue?"),
                L("确认恢复", "Confirm restore"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        var running = GetKnownServicePids();
        if (running.Count > 0)
        {
            StopProcesses(running);
            await Task.Delay(700);
        }

        try
        {
            UpdateBackupMetadata? metadata = null;
            using (var archive = ZipFile.OpenRead(selected.Path))
            {
                var metadataEntry = archive.GetEntry(".launcher-backup-metadata.json");
                if (metadataEntry is not null)
                {
                    using var reader = new StreamReader(metadataEntry.Open(), Encoding.UTF8);
                    metadata = JsonSerializer.Deserialize<UpdateBackupMetadata>(await reader.ReadToEndAsync());
                }
                foreach (var entry in archive.Entries.Where(entry => !entry.FullName.Equals(".launcher-backup-metadata.json", StringComparison.OrdinalIgnoreCase)))
                {
                    var destination = Path.GetFullPath(Path.Combine(selected.Source.DeploymentRoot, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                    var root = Path.GetFullPath(selected.Source.DeploymentRoot) + Path.DirectorySeparatorChar;
                    if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(entry.Name))
                    {
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    entry.ExtractToFile(destination, true);
                }
            }
            if (!string.IsNullOrWhiteSpace(metadata?.PreviousCommitSha))
            {
                SaveUpdateState(selected.Source, metadata.PreviousCommitSha);
            }
            else if (File.Exists(UpdateStatePath(selected.Source)))
            {
                File.Delete(UpdateStatePath(selected.Source));
            }
            var definition = _modelCatalog.Models.FirstOrDefault(model => Path.GetFullPath(model.RootDirectory).Equals(Path.GetFullPath(selected.Source.DeploymentRoot), StringComparison.OrdinalIgnoreCase));
            if (definition is not null)
            {
                if (!string.IsNullOrWhiteSpace(metadata?.PreviousVersion))
                {
                    definition.InstalledVersion = metadata.PreviousVersion;
                }
                if (metadata?.PreviousDependencies is not null)
                {
                    definition.Dependencies = metadata.PreviousDependencies;
                }
                SaveModelCatalog(_modelCatalog);
            }
            AppendLog(L($"已恢复备份：{selected.Path}", $"Backup restored: {selected.Path}"));
            MessageBox.Show(L("备份恢复完成。建议运行环境自检后再启动模型。", "Backup restored. Run environment check before launching a model."), L("恢复完成", "Restore complete"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, L("恢复失败", "Restore failed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            RefreshStatus();
        }
    }

    private void OpenLogsFolder()
    {
        var candidates = new[]
        {
            Path.Combine(_settings.DataRoot, "logs"),
            Path.Combine(_settings.WooshRoot, "logs"),
            Path.Combine(_settings.StableRoot, "generated_audio"),
            Path.Combine(_settings.IndexTtsRoot, "logs")
        };
        var target = candidates.FirstOrDefault(Directory.Exists) ?? _settings.IndexTtsRoot;
        if (Directory.Exists(target))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{target}\"") { UseShellExecute = true });
        }
    }

    private ContextMenuStrip CreateMaintenanceMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(L("检查启动器更新", "Check launcher update"), null, async (_, _) => await CheckLauncherUpdateAsync());
        menu.Items.Add(L("恢复上一版启动器", "Restore previous launcher"), null, async (_, _) => await RollbackLauncherAsync());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(L("安装签名插件包", "Install signed plugin"), null, async (_, _) => await ShowInstallPluginWizardAsync());
        menu.Items.Add(L("卸载所选插件", "Uninstall selected plugin"), null, (_, _) => UninstallSelectedPlugin());
        menu.Items.Add(L("受信任发布者", "Trusted publishers"), null, (_, _) => ShowTrustedPublishersDialog());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(L("添加新模型", "Add new model"), null, (_, _) => ShowAddModelDialog());
        menu.Items.Add(L("运行环境自检", "Run environment check"), null, (_, _) => ShowEnvironmentReport());
        menu.Items.Add(L("配置模型目录与端口", "Configure paths and ports"), null, (_, _) => ShowSettingsDialog());
        menu.Items.Add(L("恢复源码备份", "Restore source backup"), null, async (_, _) => await RestoreBackupAsync());
        menu.Items.Add(L("恢复插件版本", "Restore plugin version"), null, (_, _) => RestorePluginVersion());
        menu.Items.Add(L("打开日志目录", "Open logs folder"), null, (_, _) => OpenLogsFolder());
        menu.Items.Add(L("导出诊断包", "Export diagnostics"), null, (_, _) => ExportDiagnostics());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(L($"启动器版本 {LauncherVersion}", $"Launcher version {LauncherVersion}"))!.Enabled = false;
        return menu;
    }

    private void RestorePluginVersion()
    {
        var backupsRoot = Path.Combine(_settings.DataRoot, "backups", "plugin-installs");
        var backupRoots = new[] { backupsRoot, Path.Combine(_settings.DataRoot, "backups", "uninstalled-plugins") };
        var backups = backupRoots.Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateDirectories(root))
                .Select(path => (Path: path, Metadata: Path.Combine(path, ".bachen-plugin-definition.json")))
                .Where(item => File.Exists(item.Metadata))
                .OrderByDescending(item => Directory.GetLastWriteTime(item.Path))
                .ToList();
        if (backups.Count == 0)
        {
            MessageBox.Show(L("没有找到可恢复的插件版本。", "No restorable plugin versions were found."), L("恢复插件版本", "Restore plugin version"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var picker = new Form { Text = L("选择插件版本", "Choose plugin version"), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, ClientSize = new Size(760, 180), MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false };
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(24, 40), Size = new Size(712, 34) };
        var definitions = new List<LauncherModelDefinition>();
        foreach (var backup in backups)
        {
            var definition = JsonSerializer.Deserialize<LauncherModelDefinition>(File.ReadAllText(backup.Metadata)) ?? new LauncherModelDefinition();
            definitions.Add(definition);
            combo.Items.Add($"{definition.DisplayName}  |  {definition.InstalledVersion}  |  {Directory.GetLastWriteTime(backup.Path):yyyy-MM-dd HH:mm:ss}");
        }
        combo.SelectedIndex = 0;
        picker.Controls.Add(combo);
        picker.Controls.Add(new Button { Text = L("取消", "Cancel"), DialogResult = DialogResult.Cancel, Location = new Point(500, 112), Size = new Size(110, 36) });
        picker.Controls.Add(new Button { Text = L("恢复", "Restore"), DialogResult = DialogResult.OK, Location = new Point(624, 112), Size = new Size(110, 36) });
        if (picker.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var selectedBackup = backups[combo.SelectedIndex].Path;
        var restoredDefinition = definitions[combo.SelectedIndex];
        var targetRoot = Path.GetFullPath(restoredDefinition.RootDirectory);
        var managedRoot = Path.GetFullPath(Path.Combine(_settings.DataRoot, "plugins")) + Path.DirectorySeparatorChar;
        if (!targetRoot.StartsWith(managedRoot, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(L("备份目标不在托管插件目录内，已阻止恢复。", "The backup target is outside managed plugin storage; restore was blocked."), L("路径验证失败", "Path validation failed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        if (MessageBox.Show(L($"恢复 {restoredDefinition.DisplayName} {restoredDefinition.InstalledVersion}？当前版本也会保留为备份。", $"Restore {restoredDefinition.DisplayName} {restoredDefinition.InstalledVersion}? The current version will also be retained as a backup."), L("确认恢复", "Confirm restore"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        string? currentBackup = null;
        try
        {
            var currentDefinition = _modelCatalog.Models.FirstOrDefault(model => model.Id.Equals(restoredDefinition.Id, StringComparison.OrdinalIgnoreCase));
            if (_activeService is not null && _activeService.WorkingDirectory.Equals(targetRoot, StringComparison.OrdinalIgnoreCase))
            {
                StopKnownServices();
            }
            if (Directory.Exists(targetRoot))
            {
                currentBackup = Path.Combine(backupsRoot, $"{restoredDefinition.Id}-{DateTime.Now:yyyyMMdd-HHmmss}-rollback");
                if (currentDefinition is not null)
                {
                    File.WriteAllText(Path.Combine(targetRoot, ".bachen-plugin-definition.json"), JsonSerializer.Serialize(currentDefinition, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
                }
                Directory.Move(targetRoot, currentBackup);
            }
            Directory.Move(selectedBackup, targetRoot);
            var restoredMetadata = Path.Combine(targetRoot, ".bachen-plugin-definition.json");
            if (File.Exists(restoredMetadata))
            {
                File.Delete(restoredMetadata);
            }
            if (currentDefinition is not null)
            {
                _modelCatalog.Models.Remove(currentDefinition);
            }
            _modelCatalog.Models.Add(restoredDefinition);
            SaveModelCatalog(_modelCatalog);
            ConfigureProfiles();
            _selectedPluginId = restoredDefinition.Id;
            Controls.Clear();
            InitializeUi();
            RefreshStatus();
            AppendLog(L($"已恢复插件版本：{restoredDefinition.DisplayName} {restoredDefinition.InstalledVersion}", $"Plugin version restored: {restoredDefinition.DisplayName} {restoredDefinition.InstalledVersion}"));
        }
        catch (Exception ex)
        {
            if (!Directory.Exists(targetRoot) && currentBackup is not null && Directory.Exists(currentBackup))
            {
                Directory.Move(currentBackup, targetRoot);
            }
            MessageBox.Show(ex.Message, L("恢复失败", "Restore failed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task ShowInstallPluginWizardAsync()
    {
        using var manifestPicker = new OpenFileDialog
        {
            Title = L("选择插件清单", "Choose plugin manifest"),
            Filter = "BaChen plugin manifest (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (manifestPicker.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        PluginPackageManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<PluginPackageManifest>(
                await File.ReadAllTextAsync(manifestPicker.FileName),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("The plugin manifest is empty.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, L("清单读取失败", "Manifest read failed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var verification = PluginManifestSignatureVerifier.Verify(manifest, _trustedPublishers);
        if (!verification.IsTrusted)
        {
            MessageBox.Show(
                L("此插件不会被安装，因为清单未通过签名验证。\n\n", "This plugin will not be installed because its manifest is not trusted.\n\n") + verification.Message,
                L("签名验证失败", "Signature verification failed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (manifest.SchemaVersion >= 3 && !ConfirmPluginLicenseAcceptance(manifest))
        {
            return;
        }

        var existing = _modelCatalog.Models.FirstOrDefault(model => model.Id.Equals(manifest.Id, StringComparison.OrdinalIgnoreCase));
        var usedPorts = _modelCatalog.Models.Where(model => existing is null || !model.Id.Equals(existing.Id, StringComparison.OrdinalIgnoreCase)).Select(model => model.Port);
        if (usedPorts.Contains(manifest.Port))
        {
            MessageBox.Show(L($"端口 {manifest.Port} 已分配给其他插件。", $"Port {manifest.Port} is assigned to another plugin."), L("端口冲突", "Port conflict"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string? packagePath = null;
        using (var packagePicker = new OpenFileDialog
        {
            Title = L("选择插件 ZIP 包；取消可使用清单中的 HTTPS 地址", "Choose plugin ZIP; cancel to use the manifest HTTPS URL"),
            Filter = "Plugin package (*.zip)|*.zip",
            InitialDirectory = Path.GetDirectoryName(manifestPicker.FileName),
            CheckFileExists = true
        })
        {
            if (packagePicker.ShowDialog(this) == DialogResult.OK)
            {
                packagePath = packagePicker.FileName;
            }
            else if (!Uri.TryCreate(manifest.PackageUrl, UriKind.Absolute, out var packageUri) || packageUri.Scheme != Uri.UriSchemeHttps)
            {
                return;
            }
        }

        var summary = L(
            $"插件：{manifest.DisplayName}\n版本：{manifest.Version}\n发布者：{verification.Publisher?.DisplayName}\n分类：{manifest.Category}\n端口：{manifest.Port}\n包来源：{(packagePath ?? manifest.PackageUrl)}\n\n签名有效。是否安装？",
            $"Plugin: {manifest.DisplayName}\nVersion: {manifest.Version}\nPublisher: {verification.Publisher?.DisplayName}\nCategory: {manifest.Category}\nPort: {manifest.Port}\nPackage: {packagePath ?? manifest.PackageUrl}\n\nSignature is valid. Install now?");
        if (MessageBox.Show(summary, L("安装插件", "Install plugin"), MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            SetRuntimePhase("正在验证并安装插件", $"Installing {manifest.DisplayName}");
            var result = await _pluginPackageService.InstallAsync(manifest, packagePath, _settings.DataRoot);
            if (existing is not null && result.ReplacedPluginBackup is not null)
            {
                File.WriteAllText(
                    Path.Combine(result.ReplacedPluginBackup, ".bachen-plugin-definition.json"),
                    JsonSerializer.Serialize(existing, new JsonSerializerOptions { WriteIndented = true }),
                    Encoding.UTF8);
            }
            if (existing is not null)
            {
                _modelCatalog.Models.Remove(existing);
            }
            _modelCatalog.Models.Add(result.Definition);
            SaveModelCatalog(_modelCatalog);
            ConfigureProfiles();
            _selectedPluginId = result.Definition.Id;
            _pluginSearchQuery = string.Empty;
            _pluginCategory = "*";
            Controls.Clear();
            InitializeUi();
            RefreshStatus();
            AppendLog(L($"已安装插件：{result.Definition.DisplayName} {result.Definition.InstalledVersion}", $"Plugin installed: {result.Definition.DisplayName} {result.Definition.InstalledVersion}"));
            var backupMessage = result.ReplacedPluginBackup is null ? string.Empty : Environment.NewLine + L($"旧版本备份：{result.ReplacedPluginBackup}", $"Previous version backup: {result.ReplacedPluginBackup}");
            MessageBox.Show(L("插件安装完成。", "Plugin installation complete.") + backupMessage, L("安装完成", "Installation complete"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppendLog(L("插件安装失败：", "Plugin installation failed: ") + ex.Message, null, true);
            MessageBox.Show(ex.Message, L("安装失败", "Installation failed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private bool ConfirmPluginLicenseAcceptance(PluginPackageManifest manifest)
    {
        using var dialog = new Form { Text = L("插件许可确认", "Plugin license agreement"), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, ClientSize = new Size(720, 300), MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false, BackColor = Theme.Card, Font = new Font("Microsoft YaHei UI", 10F) };
        var message = new Label { Text = L($"{manifest.DisplayName} 由 {manifest.Publisher} 发布。安装和使用受“{manifest.LicenseName}”约束。请先阅读上游条款；启动器不会替代发布者授予任何权利。", $"{manifest.DisplayName} is published by {manifest.Publisher} and governed by {manifest.LicenseName}. Review the upstream terms first; the launcher does not grant rights on the publisher's behalf."), Location = new Point(28, 24), Size = new Size(664, 86), ForeColor = Theme.Ink };
        var link = new LinkLabel { Text = L("打开完整许可条款", "Open full license terms"), Location = new Point(28, 118), Size = new Size(260, 30), LinkColor = Theme.MidTeal };
        link.Click += (_, _) => Process.Start(new ProcessStartInfo(manifest.LicenseUrl) { UseShellExecute = true });
        var accepted = new CheckBox { Text = L("我已阅读并接受此插件及模型的许可条款", "I have read and accept the plugin and model license terms"), Location = new Point(28, 166), Size = new Size(600, 34), ForeColor = Theme.Ink, BackColor = Theme.Card };
        var cancel = new Button { Text = L("取消", "Cancel"), DialogResult = DialogResult.Cancel, Location = new Point(456, 232), Size = new Size(110, 40) };
        var install = new Button { Text = L("接受并继续", "Accept and continue"), DialogResult = DialogResult.OK, Location = new Point(578, 232), Size = new Size(114, 40), Enabled = false };
        accepted.CheckedChanged += (_, _) => install.Enabled = accepted.Checked;
        dialog.Controls.AddRange([message, link, accepted, cancel, install]);
        dialog.AcceptButton = install;
        dialog.CancelButton = cancel;
        return dialog.ShowDialog(this) == DialogResult.OK && accepted.Checked;
    }

    private void UninstallSelectedPlugin()
    {
        var definition = _modelCatalog.Models.FirstOrDefault(model => model.Id.Equals(_selectedPluginId, StringComparison.OrdinalIgnoreCase));
        if (definition is null || definition.IsBuiltIn)
        {
            MessageBox.Show(L("内置插件不能通过此功能卸载。", "Built-in plugins cannot be removed with this command."), L("无法卸载", "Cannot uninstall"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (MessageBox.Show(
                L($"卸载 {definition.DisplayName}？托管目录会移动到备份，而不是永久删除。", $"Uninstall {definition.DisplayName}? Managed files will be moved to backup, not permanently deleted."),
                L("确认卸载", "Confirm uninstall"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }
        try
        {
            if (_activeService is not null && _activeService.WorkingDirectory.Equals(definition.RootDirectory, StringComparison.OrdinalIgnoreCase))
            {
                StopKnownServices();
            }
            var result = _pluginPackageService.Uninstall(definition, _settings.DataRoot);
            if (result.BackupPath is not null)
            {
                File.WriteAllText(Path.Combine(result.BackupPath, ".bachen-plugin-definition.json"), JsonSerializer.Serialize(definition, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
            }
            _modelCatalog.Models.Remove(definition);
            SaveModelCatalog(_modelCatalog);
            ConfigureProfiles();
            _selectedPluginId = "woosh-dflow";
            Controls.Clear();
            InitializeUi();
            RefreshStatus();
            var details = result.FilesMoved
                ? L($"插件文件已移动到：{result.BackupPath}", $"Plugin files moved to: {result.BackupPath}")
                : L("插件目录位于托管目录外，仅移除了启动器登记，未移动文件。", "The plugin directory is outside managed storage; only its launcher registration was removed.");
            AppendLog(L($"已卸载插件：{definition.DisplayName}", $"Plugin uninstalled: {definition.DisplayName}"));
            MessageBox.Show(details, L("卸载完成", "Uninstall complete"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, L("卸载失败", "Uninstall failed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowTrustedPublishersDialog()
    {
        using var dialog = new Form
        {
            Text = L("受信任发布者", "Trusted publishers"),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            ClientSize = new Size(760, 430),
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            Font = new Font("Microsoft YaHei UI", 10F)
        };
        var list = new ListBox { Location = new Point(24, 24), Size = new Size(712, 300) };
        void RefreshPublisherList()
        {
            list.Items.Clear();
            foreach (var publisher in _trustedPublishers.Publishers.OrderBy(item => item.DisplayName))
            {
                list.Items.Add($"{publisher.DisplayName}  |  {publisher.KeyId}  |  {publisher.AddedAt:yyyy-MM-dd}");
            }
        }
        RefreshPublisherList();
        var add = new Button { Text = L("导入公钥", "Import public key"), Location = new Point(374, 354), Size = new Size(140, 38) };
        var remove = new Button { Text = L("移除信任", "Remove trust"), Location = new Point(526, 354), Size = new Size(110, 38) };
        var close = new Button { Text = L("关闭", "Close"), DialogResult = DialogResult.OK, Location = new Point(648, 354), Size = new Size(88, 38) };
        add.Click += (_, _) =>
        {
            using var keyPicker = new OpenFileDialog { Filter = "PEM public key (*.pem;*.pub)|*.pem;*.pub|All files (*.*)|*.*", CheckFileExists = true };
            if (keyPicker.ShowDialog(dialog) != DialogResult.OK)
            {
                return;
            }
            var pem = File.ReadAllText(keyPicker.FileName);
            try
            {
                using var rsa = System.Security.Cryptography.RSA.Create();
                rsa.ImportFromPem(pem);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, L("公钥无效", "Invalid public key"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            using var metadata = new Form { Text = L("发布者信息", "Publisher information"), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, ClientSize = new Size(520, 190), MaximizeBox = false, MinimizeBox = false };
            var name = new TextBox { Location = new Point(160, 30), Size = new Size(330, 30) };
            var keyId = new TextBox { Location = new Point(160, 76), Size = new Size(330, 30), Text = Path.GetFileNameWithoutExtension(keyPicker.FileName) };
            metadata.Controls.Add(new Label { Text = L("发布者名称", "Publisher name"), Location = new Point(24, 34), Size = new Size(125, 26) });
            metadata.Controls.Add(new Label { Text = "Key ID", Location = new Point(24, 80), Size = new Size(125, 26) });
            metadata.Controls.Add(name);
            metadata.Controls.Add(keyId);
            metadata.Controls.Add(new Button { Text = L("取消", "Cancel"), DialogResult = DialogResult.Cancel, Location = new Point(276, 130), Size = new Size(100, 34) });
            metadata.Controls.Add(new Button { Text = L("导入", "Import"), DialogResult = DialogResult.OK, Location = new Point(390, 130), Size = new Size(100, 34) });
            if (metadata.ShowDialog(dialog) != DialogResult.OK || string.IsNullOrWhiteSpace(name.Text) || string.IsNullOrWhiteSpace(keyId.Text))
            {
                return;
            }
            _trustedPublishers.Publishers.RemoveAll(item => item.KeyId.Equals(keyId.Text.Trim(), StringComparison.OrdinalIgnoreCase));
            _trustedPublishers.Publishers.Add(new TrustedPublisher { KeyId = keyId.Text.Trim(), DisplayName = name.Text.Trim(), PublicKeyPem = pem, AddedAt = DateTimeOffset.Now });
            TrustedPublisherStoreService.Save(TrustedPublishersPath, _trustedPublishers);
            RefreshPublisherList();
        };
        remove.Click += (_, _) =>
        {
            if (list.SelectedIndex < 0)
            {
                return;
            }
            var publisher = _trustedPublishers.Publishers.OrderBy(item => item.DisplayName).ElementAt(list.SelectedIndex);
            _trustedPublishers.Publishers.Remove(publisher);
            TrustedPublisherStoreService.Save(TrustedPublishersPath, _trustedPublishers);
            RefreshPublisherList();
        };
        dialog.Controls.Add(list);
        dialog.Controls.Add(add);
        dialog.Controls.Add(remove);
        dialog.Controls.Add(close);
        dialog.AcceptButton = close;
        dialog.ShowDialog(this);
    }

    private void ShowAddModelDialog()
    {
        using var dialog = new Form
        {
            Text = L("添加新模型", "Add new model"),
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(920, 690),
            MinimumSize = new Size(760, 560),
            BackColor = Theme.Card,
            Font = new Font("Microsoft YaHei UI", 10F),
            ShowInTaskbar = false
        };
        var table = new TableLayoutPanel
        {
            Location = new Point(24, 22),
            Size = new Size(872, 570),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            ColumnCount = 3,
            RowCount = 14,
            AutoScroll = true,
            BackColor = Theme.Card
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 102));

        var name = AddTextRow(table, 0, L("模型名称 *", "Model name *"), string.Empty);
        var description = AddTextRow(table, 1, L("说明", "Description"), string.Empty);
        var category = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown, Margin = new Padding(6) };
        category.Items.AddRange([
            "Experimental", "Image generation", "Video generation", "LLM / Chat", "Vision", "Coding",
            "3D generation", "Sound design", "Audio generation", "Music", "TTS", "Voice", "Utilities", "Other"]);
        category.Text = "Experimental";
        table.Controls.Add(new Label { Text = L("分类", "Category"), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
        table.Controls.Add(category, 1, 2);

        var root = AddTextRow(table, 3, L("模型目录 *", "Model root *"), string.Empty);
        var rootBrowse = new Button { Text = L("浏览…", "Browse..."), Dock = DockStyle.Fill, Margin = new Padding(6) };
        rootBrowse.Click += (_, _) =>
        {
            using var picker = new FolderBrowserDialog { InitialDirectory = Directory.Exists(root.Text) ? root.Text : Environment.GetFolderPath(Environment.SpecialFolder.Desktop) };
            if (picker.ShowDialog(dialog) == DialogResult.OK)
            {
                root.Text = picker.SelectedPath;
            }
        };
        table.Controls.Add(rootBrowse, 2, 3);

        var executable = AddTextRow(table, 4, L("启动程序 *", "Executable *"), string.Empty);
        var executableBrowse = new Button { Text = L("选择文件", "Choose file"), Dock = DockStyle.Fill, Margin = new Padding(6) };
        executableBrowse.Click += (_, _) =>
        {
            using var picker = new OpenFileDialog { Filter = "Programs (*.exe)|*.exe|All files (*.*)|*.*", CheckFileExists = false };
            if (picker.ShowDialog(dialog) == DialogResult.OK)
            {
                executable.Text = picker.FileName;
            }
        };
        table.Controls.Add(executableBrowse, 2, 4);

        var arguments = AddTextRow(table, 5, L("启动参数", "Arguments"), string.Empty);
        var port = new NumericUpDown { Minimum = 1024, Maximum = 65535, Value = 7862, Width = 180, Margin = new Padding(6) };
        table.Controls.Add(new Label { Text = L("WebUI 端口", "WebUI port"), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 6);
        table.Controls.Add(port, 1, 6);
        var vram = new NumericUpDown { Minimum = 0, Maximum = 32, DecimalPlaces = 1, Increment = 0.5M, Value = 4, Width = 180, Margin = new Padding(6) };
        table.Controls.Add(new Label { Text = L("建议显存 (GB)", "Recommended VRAM (GB)"), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 7);
        table.Controls.Add(vram, 1, 7);
        var highVram = new CheckBox { Text = L("高显存模型，启动前警告", "High VRAM warning before launch"), AutoSize = true, Margin = new Padding(6, 9, 6, 6) };
        table.Controls.Add(highVram, 2, 7);
        var required = AddTextRow(table, 8, L("必需文件", "Required files"), string.Empty);
        var repository = AddTextRow(table, 9, L("GitHub 仓库", "GitHub repository"), string.Empty);
        var branch = AddTextRow(table, 10, L("更新分支", "Update branch"), "main");
        var version = AddTextRow(table, 11, L("本地版本", "Local version"), "local");
        var dependencies = AddTextRow(table, 12, L("依赖声明", "Dependencies"), string.Empty);
        var systemMemory = new NumericUpDown { Minimum = 0, Maximum = 256, DecimalPlaces = 1, Increment = 1, Value = 8, Width = 180, Margin = new Padding(6) };
        table.Controls.Add(new Label { Text = L("建议内存 (GB)", "Recommended RAM (GB)"), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 13);
        table.Controls.Add(systemMemory, 1, 13);
        dialog.Controls.Add(table);

        var hint = new Label
        {
            Text = L("支持 {root} 和 {port} 占位符；必需文件使用分号分隔。GitHub 仓库填写 owner/repository 后将纳入更新检查。", "Use {root} and {port} placeholders; separate required files with semicolons. Enter owner/repository to include GitHub update checks."),
            Location = new Point(26, 602),
            Size = new Size(650, 48),
            ForeColor = Theme.Muted
        };
        dialog.Controls.Add(hint);
        var cancel = new Button { Text = L("取消", "Cancel"), DialogResult = DialogResult.Cancel, Location = new Point(674, 620), Size = new Size(104, 38), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
        var add = new Button { Text = L("添加模型", "Add model"), Location = new Point(790, 620), Size = new Size(104, 38), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
        add.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(name.Text) || string.IsNullOrWhiteSpace(root.Text) || string.IsNullOrWhiteSpace(executable.Text))
            {
                MessageBox.Show(L("请填写模型名称、模型目录和启动程序。", "Enter the model name, root directory, and executable."), L("信息不完整", "Information incomplete"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!string.IsNullOrWhiteSpace(repository.Text) && repository.Text.Trim().Split('/').Length != 2)
            {
                MessageBox.Show(L("GitHub 仓库格式应为 owner/repository。", "GitHub repository must use owner/repository format."), L("仓库格式", "Repository format"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var configuredPorts = new[] { _settings.WooshPort, _settings.StablePort, _settings.IndexTtsPort }
                .Concat(CustomModelDefinitions().Select(definition => definition.Port));
            if (configuredPorts.Contains((int)port.Value))
            {
                MessageBox.Show(L("该端口已经由现有模型配置使用。请为新模型分配一个不同端口。", "That port is already assigned to an existing model configuration. Choose a different port."), L("端口冲突", "Port conflict"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!Directory.Exists(root.Text.Trim()) && MessageBox.Show(
                    L("模型目录暂时不存在。仍保存配置吗？", "The model directory does not exist yet. Save the configuration anyway?"),
                    L("目录检查", "Directory check"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }
            var definition = new LauncherModelDefinition
            {
                DisplayName = name.Text.Trim(),
                Description = description.Text.Trim(),
                Category = string.IsNullOrWhiteSpace(category.Text) ? "Experimental" : category.Text.Trim(),
                RootDirectory = root.Text.Trim(),
                Executable = executable.Text.Trim(),
                Arguments = arguments.Text.Trim(),
                Port = (int)port.Value,
                RecommendedVramMiB = (int)(vram.Value * 1024M),
                RecommendedSystemMemoryMiB = (int)(systemMemory.Value * 1024M),
                IsHighVram = highVram.Checked,
                RequiredFiles = required.Text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(item => item.Replace('\\', '/')).ToArray(),
                Dependencies = dependencies.Text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                GitHubRepository = repository.Text.Trim(),
                GitHubBranch = string.IsNullOrWhiteSpace(branch.Text) ? "main" : branch.Text.Trim(),
                InstalledVersion = string.IsNullOrWhiteSpace(version.Text) ? "local" : version.Text.Trim(),
                Publisher = L("本机用户", "Local user"),
                TrustSource = "LocalUser"
            };
            _modelCatalog.Models.Add(definition);
            SaveModelCatalog(_modelCatalog);
            ConfigureProfiles();
            Controls.Clear();
            InitializeUi();
            RefreshStatus();
            AppendLog(L($"已添加模型：{definition.DisplayName}", $"Model added: {definition.DisplayName}"));
            dialog.DialogResult = DialogResult.OK;
            dialog.Close();
        };
        dialog.Controls.Add(cancel);
        dialog.Controls.Add(add);
        dialog.AcceptButton = add;
        dialog.CancelButton = cancel;
        dialog.ShowDialog(this);

        TextBox AddTextRow(TableLayoutPanel layout, int row, string label, string value)
        {
            var box = new TextBox { Text = value, Dock = DockStyle.Fill, Margin = new Padding(6) };
            layout.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
            layout.Controls.Add(box, 1, row);
            return box;
        }
    }

    private void InitializeUi()
    {
        Text = "BaChen AI Launcher";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1180, 800);
        Size = new Size(1440, 900);
        Font = new Font("Microsoft YaHei UI", 10F);
        BackColor = Color.FromArgb(229, 237, 234);
        _pluginItems.Clear();

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 82,
            BackColor = Theme.DeepTeal
        };
        header.Controls.Add(CreateText("BACHEN AI LAUNCHER", new Rectangle(28, 12, 330, 31), 16F, Color.White, FontStyle.Bold));
        header.Controls.Add(CreateText(L($"本地 AI 插件控制台  ·  v{LauncherVersion}", $"Local AI plugin console  ·  v{LauncherVersion}"), new Rectangle(30, 43, 300, 22), 8.5F, Color.FromArgb(176, 222, 213), FontStyle.Regular));

        _phaseLabel.Bounds = new Rectangle(380, 22, 330, 42);
        _phaseLabel.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
        _phaseLabel.ForeColor = Color.FromArgb(224, 243, 237);
        _phaseLabel.BackColor = Color.Transparent;
        _phaseLabel.TextAlign = ContentAlignment.MiddleLeft;
        _phaseLabel.Text = L(_phaseChinese, _phaseEnglish);
        header.Controls.Add(_phaseLabel);

        var gpuPanel = new Panel { Size = new Size(270, 54), BackColor = Theme.DeepTeal };
        gpuPanel.Controls.Add(CreateText("RTX 5060", new Rectangle(0, 2, 96, 21), 8F, Color.FromArgb(166, 221, 210), FontStyle.Bold));
        _gpuSummaryLabel = CreateText(L("正在读取显存", "Reading GPU memory"), new Rectangle(98, 2, 170, 21), 8F, Color.White, FontStyle.Bold, ContentAlignment.MiddleRight);
        gpuPanel.Controls.Add(_gpuSummaryLabel);
        _gpuMeter = new GpuMeter { Location = new Point(0, 31), Size = new Size(268, 9) };
        gpuPanel.Controls.Add(_gpuMeter);
        header.Controls.Add(gpuPanel);

        var toolsButton = CreateActionButton(L("工具", "Tools"), Color.FromArgb(37, 110, 103), 96);
        toolsButton.Height = 36;
        toolsButton.Click += (_, _) =>
        {
            _maintenanceMenu?.Dispose();
            _maintenanceMenu = CreateMaintenanceMenu();
            _maintenanceMenu.Show(toolsButton, new Point(toolsButton.Width - _maintenanceMenu.PreferredSize.Width, toolsButton.Height));
        };
        header.Controls.Add(toolsButton);
        var languageButton = CreateActionButton(_useEnglish ? "中文" : "EN", Color.FromArgb(53, 127, 118), 74);
        languageButton.Height = 36;
        languageButton.Click += (_, _) => ToggleLanguage();
        header.Controls.Add(languageButton);

        void LayoutHeader()
        {
            languageButton.Left = header.ClientSize.Width - languageButton.Width - 24;
            languageButton.Top = 23;
            toolsButton.Left = languageButton.Left - toolsButton.Width - 10;
            toolsButton.Top = 23;
            gpuPanel.Left = toolsButton.Left - gpuPanel.Width - 24;
            gpuPanel.Top = 15;
            _phaseLabel.Width = Math.Max(230, gpuPanel.Left - _phaseLabel.Left - 24);
        }
        header.SizeChanged += (_, _) => LayoutHeader();

        _logHost = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = _logExpanded ? 210 : 72,
            BackColor = BackColor,
            Padding = new Padding(12, 4, 12, 8)
        };
        var logCard = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            FillColor = Color.FromArgb(18, 57, 55),
            CornerRadius = 18,
            BorderColor = Color.FromArgb(47, 101, 95),
            BorderWidth = 1
        };
        _logHost.Controls.Add(logCard);
        logCard.Controls.Add(CreateText(L("运行日志", "Runtime log"), new Rectangle(20, 8, 110, 31), 11F, Color.FromArgb(211, 239, 232), FontStyle.Bold));
        _logSummaryLabel = CreateText(L("暂无运行消息", "No runtime messages"), new Rectangle(142, 9, 700, 30), 8.5F, Color.FromArgb(166, 202, 195), FontStyle.Regular);
        logCard.Controls.Add(_logSummaryLabel);

        _logToggleButton = CreateActionButton(_logExpanded ? L("收起", "Collapse") : L("展开", "Expand"), Color.FromArgb(43, 110, 102), 88);
        _logToggleButton.Height = 30;
        _logToggleButton.Click += (_, _) => ToggleLogDrawer();
        logCard.Controls.Add(_logToggleButton);

        var allLogsButton = CreateActionButton(L("全部", "All"), Color.FromArgb(35, 104, 98), 68);
        var errorLogsButton = CreateActionButton(L("错误", "Errors"), Theme.Coral, 76);
        var currentLogsButton = CreateActionButton(L("当前", "Current"), Color.FromArgb(47, 83, 132), 76);
        var copyLogsButton = CreateActionButton(L("复制", "Copy"), Color.FromArgb(31, 121, 108), 76);
        var clearLogsButton = CreateActionButton(L("清空", "Clear"), Color.FromArgb(96, 99, 108), 76);
        var logButtons = new[] { allLogsButton, errorLogsButton, currentLogsButton, copyLogsButton, clearLogsButton };
        foreach (var button in logButtons)
        {
            button.Height = 28;
            button.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold);
            button.Visible = _logExpanded;
            logCard.Controls.Add(button);
        }
        allLogsButton.Click += (_, _) => SetLogFilter(LauncherLogFilter.All);
        errorLogsButton.Click += (_, _) => SetLogFilter(LauncherLogFilter.Errors);
        currentLogsButton.Click += (_, _) => SetLogFilter(LauncherLogFilter.CurrentService);
        copyLogsButton.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_log.Text))
            {
                Clipboard.SetText(_log.Text);
                AppendLog(L("已复制当前日志视图。", "Current log view copied."));
            }
        };
        clearLogsButton.Click += (_, _) =>
        {
            _logEntries.Clear();
            _logFilter = LauncherLogFilter.All;
            RenderLog();
        };

        _log.Name = "logOutput";
        _log.ReadOnly = true;
        _log.BorderStyle = BorderStyle.None;
        _log.BackColor = logCard.FillColor;
        _log.ForeColor = Color.FromArgb(216, 236, 230);
        _log.Font = new Font("Cascadia Mono", 9F);
        _log.Visible = _logExpanded;
        logCard.Controls.Add(_log);

        void LayoutLogCard()
        {
            _logToggleButton.Left = logCard.ClientSize.Width - _logToggleButton.Width - 18;
            _logToggleButton.Top = 8;
            _logSummaryLabel.Width = Math.Max(180, _logToggleButton.Left - _logSummaryLabel.Left - 14);
            var x = 20;
            foreach (var button in logButtons)
            {
                button.Location = new Point(x, 49);
                x = button.Right + 8;
            }
            _log.Location = new Point(20, 84);
            _log.Size = new Size(Math.Max(100, logCard.ClientSize.Width - 40), Math.Max(40, logCard.ClientSize.Height - 100));
        }
        logCard.SizeChanged += (_, _) => LayoutLogCard();

        var statusStrip = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 56,
            BackColor = Color.FromArgb(214, 226, 222),
            Padding = new Padding(22, 8, 22, 8)
        };
        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.ForeColor = Theme.Muted;
        _statusLabel.Font = new Font("Microsoft YaHei UI", 8F);
        _statusLabel.BackColor = Color.Transparent;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        statusStrip.Controls.Add(_statusLabel);

        var mainShell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 3,
            RowCount = 1,
            BackColor = BackColor
        };
        mainShell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        mainShell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 410));
        mainShell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        mainShell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var navigation = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 10, 0),
            FillColor = Theme.DeepTeal,
            CornerRadius = 16
        };
        navigation.Controls.Add(CreateText(L("工作区", "WORKSPACE"), new Rectangle(20, 18, 145, 24), 8F, Color.FromArgb(155, 205, 196), FontStyle.Bold));
        var pluginsButton = CreateActionButton(L("插件", "Plugins"), Color.FromArgb(37, 125, 115), 150);
        pluginsButton.Location = new Point(20, 54);
        var updatesButton = CreateActionButton(L("检查更新", "Updates"), Color.FromArgb(31, 91, 87), 150);
        updatesButton.Location = new Point(20, 102);
        updatesButton.Click += async (_, _) => await CheckUpdatesAsync();
        var healthButton = CreateActionButton(L("环境自检", "Health check"), Color.FromArgb(31, 91, 87), 150);
        healthButton.Location = new Point(20, 150);
        healthButton.Click += (_, _) => ShowEnvironmentReport();
        var settingsButton = CreateActionButton(L("设置", "Settings"), Color.FromArgb(31, 91, 87), 150);
        settingsButton.Location = new Point(20, 198);
        settingsButton.Click += (_, _) => ShowSettingsDialog();
        navigation.Controls.Add(pluginsButton);
        navigation.Controls.Add(updatesButton);
        navigation.Controls.Add(healthButton);
        navigation.Controls.Add(settingsButton);
        navigation.Controls.Add(CreateText(L("单模型安全模式", "SINGLE MODEL MODE"), new Rectangle(20, 270, 150, 25), 8F, Color.FromArgb(159, 211, 201), FontStyle.Bold));
        navigation.Controls.Add(CreateParagraph(L("启动新插件前会检查端口与显存占用。", "Ports and GPU memory are checked before launch."), new Rectangle(20, 302, 150, 72), 8F, Color.FromArgb(202, 229, 223), FontStyle.Regular));
        mainShell.Controls.Add(navigation, 0, 0);

        var pluginPanel = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 10, 0),
            FillColor = Color.White,
            CornerRadius = 16,
            BorderColor = Color.FromArgb(202, 218, 213),
            BorderWidth = 1
        };
        pluginPanel.Controls.Add(CreateText(L("已安装插件", "Installed plugins"), new Rectangle(24, 18, 250, 34), 14F, Theme.Ink, FontStyle.Bold));
        _pluginEntries = BuildPluginEntries();
        _pluginCountLabel = CreateText(string.Empty, new Rectangle(26, 52, 340, 24), 8.5F, Theme.Muted, FontStyle.Regular);
        pluginPanel.Controls.Add(_pluginCountLabel);

        var searchHost = new RoundedPanel
        {
            Location = new Point(24, 82),
            Size = new Size(362, 34),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            FillColor = Color.FromArgb(244, 249, 247),
            BorderColor = Color.FromArgb(190, 211, 205),
            BorderWidth = 1,
            CornerRadius = 8
        };
        _pluginSearchBox = new TextBox
        {
            Location = new Point(10, 7),
            Size = new Size(342, 22),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            BorderStyle = BorderStyle.None,
            BackColor = searchHost.FillColor,
            Font = new Font("Microsoft YaHei UI", 9.5F),
            PlaceholderText = L("搜索名称、说明或分类", "Search name, description, or category"),
            Text = _pluginSearchQuery
        };
        _pluginSearchBox.TextChanged += (_, _) =>
        {
            _pluginSearchQuery = _pluginSearchBox.Text.Trim();
            RebuildPluginList();
        };
        searchHost.Controls.Add(_pluginSearchBox);
        pluginPanel.Controls.Add(searchHost);

        var categoryHost = new RoundedPanel
        {
            Location = new Point(24, 124),
            Size = new Size(362, 34),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            FillColor = Color.FromArgb(244, 249, 247),
            BorderColor = Color.FromArgb(190, 211, 205),
            BorderWidth = 1,
            CornerRadius = 8
        };
        _pluginCategoryFilter = new ComboBox
        {
            Location = new Point(8, 4),
            Size = new Size(346, 26),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            BackColor = categoryHost.FillColor,
            Font = new Font("Microsoft YaHei UI", 9F)
        };
        _pluginCategoryFilter.Items.Add(L("全部分类", "All categories"));
        foreach (var category in _pluginEntries.Select(entry => entry.Category).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value))
        {
            _pluginCategoryFilter.Items.Add(category);
        }
        _pluginCategoryFilter.SelectedIndex = 0;
        _pluginCategoryFilter.SelectedIndexChanged += (_, _) =>
        {
            _pluginCategory = _pluginCategoryFilter.SelectedIndex <= 0 ? "*" : _pluginCategoryFilter.SelectedItem?.ToString() ?? "*";
            RebuildPluginList();
        };
        categoryHost.Controls.Add(_pluginCategoryFilter);
        pluginPanel.Controls.Add(categoryHost);

        _pluginList = new FlowLayoutPanel
        {
            Location = new Point(18, 170),
            Size = new Size(370, 418),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Color.White,
            Padding = new Padding(0)
        };
        _pluginList.SizeChanged += (_, _) =>
        {
            foreach (var item in _pluginList.Controls.OfType<PluginListItem>())
            {
                item.Width = Math.Max(260, _pluginList.ClientSize.Width - (_pluginList.VerticalScroll.Visible ? 22 : 4));
            }
        };
        pluginPanel.Controls.Add(_pluginList);
        RebuildPluginList();
        mainShell.Controls.Add(pluginPanel, 1, 0);

        var detailPanel = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            FillColor = Color.White,
            CornerRadius = 16,
            BorderColor = Color.FromArgb(202, 218, 213),
            BorderWidth = 1
        };
        _detailTitleLabel = CreateText(string.Empty, new Rectangle(30, 22, 500, 39), 17F, Theme.Ink, FontStyle.Bold);
        _detailDescriptionLabel = CreateParagraph(string.Empty, new Rectangle(30, 66, 500, 54), 9F, Theme.Muted, FontStyle.Regular);
        _detailStatusLabel = CreateText(string.Empty, new Rectangle(30, 126, 500, 32), 10F, Theme.DeepTeal, FontStyle.Bold);
        detailPanel.Controls.Add(_detailTitleLabel);
        detailPanel.Controls.Add(_detailDescriptionLabel);
        detailPanel.Controls.Add(_detailStatusLabel);
        detailPanel.Controls.Add(CreateText(L("插件信息", "PLUGIN INFO"), new Rectangle(30, 170, 180, 22), 8F, Color.FromArgb(89, 130, 124), FontStyle.Bold));
        _detailRootLabel = CreateParagraph(string.Empty, new Rectangle(30, 198, 500, 38), 8F, Theme.Ink, FontStyle.Regular);
        _detailPortLabel = CreateText(string.Empty, new Rectangle(30, 240, 240, 27), 9F, Theme.Ink, FontStyle.Bold);
        _detailMemoryLabel = CreateText(string.Empty, new Rectangle(280, 240, 250, 27), 9F, Theme.Ink, FontStyle.Bold);
        _detailVersionLabel = CreateText(string.Empty, new Rectangle(30, 272, 500, 25), 8.5F, Theme.Ink, FontStyle.Bold);
        _detailDependencyLabel = CreateParagraph(string.Empty, new Rectangle(30, 301, 500, 44), 8F, Theme.Muted, FontStyle.Regular);
        _detailTrustLabel = CreateText(string.Empty, new Rectangle(30, 348, 500, 25), 8.5F, Theme.MidTeal, FontStyle.Bold);
        detailPanel.Controls.Add(_detailRootLabel);
        detailPanel.Controls.Add(_detailPortLabel);
        detailPanel.Controls.Add(_detailMemoryLabel);
        detailPanel.Controls.Add(_detailVersionLabel);
        detailPanel.Controls.Add(_detailDependencyLabel);
        detailPanel.Controls.Add(_detailTrustLabel);
        detailPanel.Controls.Add(CreateText(L("启动配置", "LAUNCH PROFILE"), new Rectangle(30, 382, 180, 22), 8F, Color.FromArgb(89, 130, 124), FontStyle.Bold));

        _stableModePanel = new FlowLayoutPanel
        {
            Location = new Point(30, 410),
            Size = new Size(500, 40),
            BackColor = Color.White,
            WrapContents = false
        };
        AddStableModeButton(_stableModePanel, "small-sfx", _smallSfx);
        AddStableModeButton(_stableModePanel, "small-music", _smallMusic);
        AddStableModeButton(_stableModePanel, "medium", _medium);
        detailPanel.Controls.Add(_stableModePanel);

        _detailPrimaryButton = CreateActionButton(L("启动插件", "Launch plugin"), Theme.DeepTeal, 210);
        _detailPrimaryButton.Location = new Point(30, 466);
        _detailPrimaryButton.Height = 42;
        _detailPrimaryButton.Click += (_, _) => HandlePrimaryPluginAction();
        _openButton = _detailPrimaryButton;
        var stopButton = CreateActionButton(L("停止当前 AI", "Stop active AI"), Theme.Coral, 170);
        stopButton.Location = new Point(252, 466);
        stopButton.Height = 42;
        stopButton.Click += (_, _) => StopKnownServices();
        detailPanel.Controls.Add(_detailPrimaryButton);
        detailPanel.Controls.Add(stopButton);
        detailPanel.Controls.Add(CreateParagraph(L("模型启动后，主按钮会自动切换为打开 WebUI。", "The primary action changes to Open WebUI when the service is ready."), new Rectangle(30, 520, 500, 45), 8.5F, Theme.Muted, FontStyle.Regular));
        detailPanel.SizeChanged += (_, _) =>
        {
            var width = Math.Max(280, detailPanel.ClientSize.Width - 60);
            _detailTitleLabel.Width = width;
            _detailDescriptionLabel.Width = width;
            _detailStatusLabel.Width = width;
            _detailRootLabel.Width = width;
            _detailVersionLabel.Width = width;
            _detailDependencyLabel.Width = width;
            _detailTrustLabel.Width = width;
            _stableModePanel.Width = width;
        };
        mainShell.Controls.Add(detailPanel, 2, 0);

        Controls.Add(mainShell);
        Controls.Add(statusStrip);
        Controls.Add(_logHost);
        Controls.Add(header);
        LayoutHeader();
        LayoutLogCard();
        SelectPlugin(_pluginEntries.Any(entry => entry.Id == _selectedPluginId) ? _selectedPluginId : _pluginEntries[0].Id);
        RenderLog();
        UpdateGpuIndicator();
    }

    private List<PluginUiEntry> BuildPluginEntries()
    {
        var entries = new List<PluginUiEntry>
        {
            new("woosh-dflow", "Woosh-DFlow", L("从文字提示生成短音效与环境声。", "Generate short effects and ambient sound from text prompts."), L("声音设计", "Sound design"), _woosh, 6800, Theme.MidTeal),
            new("stable-audio-3", "Stable Audio 3", L("在启动前选择音效、音乐或 medium 模型。", "Choose SFX, music, or medium before launch."), L("音频生成", "Audio generation"), _selectedStableProfile, 2200, Color.FromArgb(29, 117, 105), true),
            new("indextts2", "IndexTTS2", L("生成角色语音、音色克隆与情绪化对白。", "Create character voices, voice clones, and emotional dialogue."), L("角色语音", "Character voice"), _indexTts, 7500, Color.FromArgb(54, 87, 139))
        };
        entries.AddRange(CustomModelDefinitions().Select(definition => new PluginUiEntry(
            definition.Id,
            definition.DisplayName,
            string.IsNullOrWhiteSpace(definition.Description) ? L("由插件目录配置驱动的本地服务。", "Local service managed by the plugin catalog.") : definition.Description,
            definition.Category,
            CreateCustomProfile(definition),
            definition.RecommendedVramMiB,
            CategoryAccent(definition.Category))));
        return entries;
    }

    private void RebuildPluginList()
    {
        if (_pluginList is null || _pluginList.IsDisposed)
        {
            return;
        }

        _pluginItems.Clear();
        _pluginList.SuspendLayout();
        _pluginList.Controls.Clear();
        var visibleEntries = _pluginEntries.Where(entry =>
        {
            var matchesCategory = _pluginCategory == "*" || entry.Category.Equals(_pluginCategory, StringComparison.OrdinalIgnoreCase);
            var matchesSearch = string.IsNullOrWhiteSpace(_pluginSearchQuery) ||
                entry.Title.Contains(_pluginSearchQuery, StringComparison.OrdinalIgnoreCase) ||
                entry.Description.Contains(_pluginSearchQuery, StringComparison.OrdinalIgnoreCase) ||
                entry.Category.Contains(_pluginSearchQuery, StringComparison.OrdinalIgnoreCase);
            return matchesCategory && matchesSearch;
        }).ToList();

        foreach (var entry in visibleEntries)
        {
            var capturedId = entry.Id;
            var item = new PluginListItem
            {
                Width = Math.Max(260, _pluginList.ClientSize.Width - 4),
                Height = 94,
                Margin = new Padding(0, 0, 0, 10),
                TitleText = entry.Title,
                CategoryText = entry.Category.ToUpperInvariant(),
                AccentColor = entry.Accent,
                IsSelected = entry.Id.Equals(_selectedPluginId, StringComparison.OrdinalIgnoreCase),
                InvokeAction = () => SelectPlugin(capturedId)
            };
            _pluginItems[entry.Id] = item;
            _pluginList.Controls.Add(item);
        }

        if (visibleEntries.Count == 0)
        {
            _pluginList.Controls.Add(new Label
            {
                AutoSize = false,
                Width = Math.Max(260, _pluginList.ClientSize.Width - 12),
                Height = 86,
                Text = L("没有符合当前条件的插件。", "No plugins match the current filters."),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Theme.Muted,
                Font = new Font("Microsoft YaHei UI", 9F)
            });
        }

        if (_pluginCountLabel is not null)
        {
            _pluginCountLabel.Text = L($"显示 {visibleEntries.Count} / {_pluginEntries.Count} 个插件", $"Showing {visibleEntries.Count} of {_pluginEntries.Count} plugins");
        }
        _pluginList.ResumeLayout();

        if (visibleEntries.Count > 0 && visibleEntries.All(entry => !entry.Id.Equals(_selectedPluginId, StringComparison.OrdinalIgnoreCase)))
        {
            SelectPlugin(visibleEntries[0].Id);
        }
    }

    private void SelectPlugin(string id)
    {
        _selectedPluginId = id;
        foreach (var pair in _pluginItems)
        {
            pair.Value.IsSelected = pair.Key.Equals(id, StringComparison.OrdinalIgnoreCase);
        }
        UpdatePluginUi();
    }

    private void AddStableModeButton(FlowLayoutPanel panel, string text, ServiceProfile profile)
    {
        var button = new RadioButton
        {
            Appearance = Appearance.Button,
            FlatStyle = FlatStyle.Flat,
            Text = text,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold),
            Size = new Size(text == "small-music" ? 128 : 104, 34),
            Margin = new Padding(0, 0, 8, 0),
            Cursor = Cursors.Hand,
            Checked = ReferenceEquals(profile, _selectedStableProfile),
            BackColor = ReferenceEquals(profile, _selectedStableProfile) ? Color.FromArgb(213, 238, 231) : Color.FromArgb(239, 245, 243),
            ForeColor = Theme.Ink
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(174, 205, 197);
        button.CheckedChanged += (_, _) =>
        {
            button.BackColor = button.Checked ? Color.FromArgb(213, 238, 231) : Color.FromArgb(239, 245, 243);
            if (!button.Checked)
            {
                return;
            }
            _selectedStableProfile = profile;
            var index = _pluginEntries.FindIndex(entry => entry.Id == "stable-audio-3");
            if (index >= 0)
            {
                _pluginEntries[index] = _pluginEntries[index] with
                {
                    Profile = profile,
                    RecommendedVramMiB = profile.IsMedium ? 8800 : 2200
                };
            }
            UpdatePluginUi();
        };
        panel.Controls.Add(button);
    }

    private void HandlePrimaryPluginAction()
    {
        var entry = _pluginEntries.FirstOrDefault(item => item.Id.Equals(_selectedPluginId, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            return;
        }
        var state = GetRuntimeState(entry.Profile);
        if (state == ServiceRuntimeState.Running)
        {
            Process.Start(new ProcessStartInfo($"http://127.0.0.1:{entry.Profile.Port}") { UseShellExecute = true });
            return;
        }
        if (state == ServiceRuntimeState.Missing)
        {
            ShowActionableError(L("环境未就绪", "Environment not ready"), string.Join(Environment.NewLine, GetMissingRequirements(entry.Profile)), entry.Profile);
            return;
        }
        if (state is ServiceRuntimeState.Checking or ServiceRuntimeState.Starting or ServiceRuntimeState.Stopping or ServiceRuntimeState.Updating)
        {
            return;
        }
        _ = StartServiceAsync(entry.Profile);
    }

    private ServiceRuntimeState GetRuntimeState(ServiceProfile profile)
    {
        if (GetMissingRequirements(profile).Count > 0)
        {
            return ServiceRuntimeState.Missing;
        }
        if (_activeService is not null && _activeService.WorkingDirectory.Equals(profile.WorkingDirectory, StringComparison.OrdinalIgnoreCase) && _activeProcess is { HasExited: false })
        {
            return GetListeningPids(profile.Port).Count > 0 ? ServiceRuntimeState.Running : ServiceRuntimeState.Starting;
        }
        return _runtimeStates.TryGetValue(ServiceKey(profile), out var state) ? state : ServiceRuntimeState.Ready;
    }

    private void SetServiceRuntimeState(ServiceProfile profile, ServiceRuntimeState state)
    {
        _runtimeStates[ServiceKey(profile)] = state;
        UpdatePluginUi();
    }

    private static string ServiceKey(ServiceProfile profile) => Path.GetFullPath(profile.WorkingDirectory);

    private void UpdatePluginUi()
    {
        if (_pluginEntries.Count == 0)
        {
            return;
        }
        foreach (var entry in _pluginEntries)
        {
            if (!_pluginItems.TryGetValue(entry.Id, out var item))
            {
                continue;
            }
            var state = GetRuntimeState(entry.Profile);
            item.StatusText = RuntimeStateText(state);
            item.StatusColor = RuntimeStateColor(state);
            item.Invalidate();
        }

        var selected = _pluginEntries.FirstOrDefault(entry => entry.Id.Equals(_selectedPluginId, StringComparison.OrdinalIgnoreCase)) ?? _pluginEntries[0];
        var selectedState = GetRuntimeState(selected.Profile);
        if (_detailTitleLabel is not null) _detailTitleLabel.Text = selected.Title;
        if (_detailDescriptionLabel is not null) _detailDescriptionLabel.Text = selected.Description;
        if (_detailStatusLabel is not null)
        {
            _detailStatusLabel.Text = $"●  {RuntimeStateText(selectedState)}";
            _detailStatusLabel.ForeColor = RuntimeStateColor(selectedState);
        }
        if (_detailRootLabel is not null) _detailRootLabel.Text = L("目录：", "Directory: ") + selected.Profile.WorkingDirectory;
        if (_detailPortLabel is not null) _detailPortLabel.Text = $"PORT  {selected.Profile.Port}";
        if (_detailMemoryLabel is not null) _detailMemoryLabel.Text = selected.RecommendedVramMiB > 0 ? $"VRAM  {selected.RecommendedVramMiB / 1024D:0.#} GB" : "VRAM  --";
        var definition = _modelCatalog.Models.FirstOrDefault(model => model.Id.Equals(selected.Id, StringComparison.OrdinalIgnoreCase));
        if (_detailVersionLabel is not null)
        {
            _detailVersionLabel.Text = definition is null
                ? "VERSION  --"
                : $"VERSION  {definition.InstalledVersion}    {L("发布者", "PUBLISHER")}  {(string.IsNullOrWhiteSpace(definition.Publisher) ? "--" : definition.Publisher)}";
        }
        if (_detailDependencyLabel is not null)
        {
            _detailDependencyLabel.Text = definition is null || definition.Dependencies.Length == 0
                ? L("依赖：未声明", "Dependencies: none declared")
                : L("依赖：", "Dependencies: ") + string.Join(", ", definition.Dependencies);
        }
        if (_detailTrustLabel is not null)
        {
            var trust = definition is null ? null : InstalledPluginTrustValidator.Verify(definition, _trustedPublishers);
            _detailTrustLabel.Text = trust?.IsTrusted == true
                ? L($"信任状态：已验证 · {definition!.TrustSource}", $"TRUST  VERIFIED · {definition!.TrustSource}")
                : L("信任状态：验证失败，启动将被阻止", "TRUST  FAILED · launch blocked");
            _detailTrustLabel.ForeColor = trust?.IsTrusted == true ? Theme.MidTeal : Theme.Coral;
        }
        if (_stableModePanel is not null) _stableModePanel.Visible = selected.HasModelSelector;
        if (_detailPrimaryButton is not null)
        {
            _detailPrimaryButton.Text = selectedState switch
            {
                ServiceRuntimeState.Running => L("打开 WebUI", "Open WebUI"),
                ServiceRuntimeState.Checking => L("正在检查", "Checking"),
                ServiceRuntimeState.Starting => L("正在启动", "Starting"),
                ServiceRuntimeState.Stopping => L("正在停止", "Stopping"),
                ServiceRuntimeState.Updating => L("正在更新", "Updating"),
                ServiceRuntimeState.Missing => L("查看缺失文件", "Review missing files"),
                ServiceRuntimeState.Error => L("重新启动", "Retry launch"),
                _ => L("启动插件", "Launch plugin")
            };
            _detailPrimaryButton.Enabled = selectedState is not (ServiceRuntimeState.Checking or ServiceRuntimeState.Starting or ServiceRuntimeState.Stopping or ServiceRuntimeState.Updating);
            _detailPrimaryButton.Invalidate();
        }
    }

    private string RuntimeStateText(ServiceRuntimeState state) => state switch
    {
        ServiceRuntimeState.Ready => L("就绪", "Ready"),
        ServiceRuntimeState.Checking => L("环境检查中", "Checking environment"),
        ServiceRuntimeState.Starting => L("启动中", "Starting"),
        ServiceRuntimeState.Running => L("正在运行", "Running"),
        ServiceRuntimeState.Stopping => L("停止中", "Stopping"),
        ServiceRuntimeState.Updating => L("更新中", "Updating"),
        ServiceRuntimeState.Missing => L("缺少文件", "Missing files"),
        _ => L("启动失败", "Launch failed")
    };

    private static Color RuntimeStateColor(ServiceRuntimeState state) => state switch
    {
        ServiceRuntimeState.Running => Color.FromArgb(29, 139, 92),
        ServiceRuntimeState.Checking or ServiceRuntimeState.Starting or ServiceRuntimeState.Stopping or ServiceRuntimeState.Updating => Color.FromArgb(195, 126, 42),
        ServiceRuntimeState.Missing or ServiceRuntimeState.Error => Theme.Coral,
        _ => Theme.MidTeal
    };

    private void UpdateGpuIndicator()
    {
        var gpu = GetGpuMemoryUsage();
        if (_gpuSummaryLabel is null || _gpuMeter is null)
        {
            return;
        }
        if (gpu is null)
        {
            _gpuSummaryLabel.Text = L("显存不可用", "GPU unavailable");
            _gpuMeter.SetValue(0, 1);
            return;
        }
        _gpuSummaryLabel.Text = $"{gpu.Value.UsedMiB:N0} / {gpu.Value.TotalMiB:N0} MiB";
        _gpuMeter.SetValue(gpu.Value.UsedMiB, gpu.Value.TotalMiB);
    }

    private void ToggleLogDrawer()
    {
        _logExpanded = !_logExpanded;
        if (_logToggleButton is not null)
        {
            _logToggleButton.Text = _logExpanded ? L("收起", "Collapse") : L("展开", "Expand");
            _logToggleButton.Invalidate();
        }
        _log.Visible = _logExpanded;
        if (_log.Parent is not null)
        {
            foreach (Control control in _log.Parent.Controls)
            {
                if (control is RoundedButton button && !ReferenceEquals(button, _logToggleButton))
                {
                    button.Visible = _logExpanded;
                }
            }
        }
        _logAnimationTimer.Start();
    }

    private void AnimateLogDrawer()
    {
        if (_logHost is null)
        {
            _logAnimationTimer.Stop();
            return;
        }
        var target = _logExpanded ? 210 : 72;
        var difference = target - _logHost.Height;
        if (Math.Abs(difference) <= 8)
        {
            _logHost.Height = target;
            _logAnimationTimer.Stop();
            return;
        }
        _logHost.Height += Math.Sign(difference) * Math.Max(8, Math.Abs(difference) / 4);
    }

    private void InitializeLegacyUi()
    {
        Text = "BaChen AI Launcher";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1024, 640);
        Size = new Size(1440, 960);
        Font = new Font("Microsoft YaHei UI", 10F);
        BackColor = Color.FromArgb(225, 235, 231);

        var canvas = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = BackColor,
            Padding = Padding.Empty
        };

        var content = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Width = 1260,
            Padding = new Padding(0, 0, 0, 8),
            BackColor = Color.Transparent
        };

        var header = new RoundedPanel
        {
            Size = new Size(1260, 92),
            Margin = new Padding(0, 0, 0, 12),
            FillColor = Theme.DeepTeal,
            CornerRadius = 28,
            ShadowColor = Color.FromArgb(48, 0, 44, 42),
            ShadowOffset = 7
        };
        header.Controls.Add(CreateText("BACHEN AI LAUNCHER", new Rectangle(34, 10, 620, 42), 18F, Color.White, FontStyle.Bold));
        header.Controls.Add(CreateText("Woosh  ·  Stable Audio 3  ·  IndexTTS2", new Rectangle(36, 53, 600, 24), 9.5F, Color.FromArgb(196, 225, 220), FontStyle.Regular));
        var livePill = new RoundedPanel
        {
            Location = new Point(944, 21),
            Size = new Size(278, 50),
            FillColor = Color.FromArgb(17, 94, 88),
            CornerRadius = 23,
            BorderColor = Color.FromArgb(109, 187, 174),
            BorderWidth = 1
        };
        livePill.Controls.Add(CreateText("LOCAL CONTROL", new Rectangle(18, 5, 238, 19), 8.5F, Color.FromArgb(183, 239, 226), FontStyle.Bold));
        livePill.Controls.Add(CreateText(L("单模型安全运行", "Single-model safe mode"), new Rectangle(18, 25, 238, 20), 9F, Color.White, FontStyle.Regular));
        header.Controls.Add(livePill);
        var languageButton = CreateActionButton(_useEnglish ? "中文" : "EN", Color.FromArgb(29, 117, 105), 88);
        languageButton.Location = new Point(842, 28);
        languageButton.Height = 36;
        languageButton.Click += (_, _) => ToggleLanguage();
        header.Controls.Add(languageButton);
        var maintenanceButton = CreateActionButton(L("维护工具", "Tools"), Color.FromArgb(40, 108, 126), 116);
        maintenanceButton.Location = new Point(712, 28);
        maintenanceButton.Height = 36;
        maintenanceButton.Click += (_, _) =>
        {
            _maintenanceMenu?.Dispose();
            _maintenanceMenu = CreateMaintenanceMenu();
            _maintenanceMenu.Show(maintenanceButton, new Point(maintenanceButton.Width - _maintenanceMenu.PreferredSize.Width, maintenanceButton.Height));
        };
        header.Controls.Add(maintenanceButton);

        var hero = new RoundedPanel
        {
            Size = new Size(1260, 136),
            Margin = new Padding(0, 0, 0, 12),
            FillColor = Theme.MidTeal,
            CornerRadius = 30,
            ShadowColor = Color.FromArgb(42, 0, 44, 42),
            ShadowOffset = 7
        };
        var heroTitle = CreateText(L("生成、切换、管理", "Create. Switch. Perform."), new Rectangle(38, 15, 700, 55), 23F, Color.White, FontStyle.Bold);
        var heroSubtitle = CreateText(L("用一个本地控制台调度音效、音乐与角色语音。", "One local control room for sound effects, music, and character voices."), new Rectangle(40, 68, 710, 26), 10F, Color.FromArgb(206, 231, 226), FontStyle.Regular);
        hero.Controls.Add(heroTitle);
        hero.Controls.Add(heroSubtitle);
        _phaseLabel.Bounds = new Rectangle(40, 100, 710, 24);
        _phaseLabel.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
        _phaseLabel.ForeColor = Color.FromArgb(232, 247, 242);
        _phaseLabel.BackColor = Color.Transparent;
        _phaseLabel.TextAlign = ContentAlignment.MiddleLeft;
        _phaseLabel.Text = L(_phaseChinese, _phaseEnglish);
        hero.Controls.Add(_phaseLabel);
        var caution = new RoundedPanel
        {
            Location = new Point(812, 24),
            Size = new Size(408, 88),
            FillColor = Theme.Card,
            CornerRadius = 20,
            BorderColor = Color.FromArgb(194, 216, 211),
            BorderWidth = 1
        };
        caution.Controls.Add(CreateText(L("显存策略", "Memory policy"), new Rectangle(24, 10, 340, 27), 11F, Theme.DeepTeal, FontStyle.Bold));
        caution.Controls.Add(CreateText(L("约 8 GB VRAM · 每次只启动一个模型", "Approx. 8 GB VRAM · one model at a time"), new Rectangle(24, 44, 360, 26), 9.5F, Theme.Ink, FontStyle.Regular));
        hero.Controls.Add(caution);

        var workspace = new RoundedPanel
        {
            Size = new Size(1260, 318),
            Margin = new Padding(0, 0, 0, 12),
            FillColor = Theme.DeepTeal,
            CornerRadius = 30,
            ShadowColor = Color.FromArgb(45, 0, 44, 42),
            ShadowOffset = 7
        };
        workspace.Controls.Add(CreateText(L("启动工作台", "Launch workspace"), new Rectangle(32, 12, 360, 36), 15F, Color.White, FontStyle.Bold));
        var workspaceSubtitle = CreateText(L("选择一项，启动器会先处理已识别的端口与显存冲突。", "Choose a service. Known port and VRAM conflicts are handled before switching."), new Rectangle(34, 50, 900, 26), 9F, Color.FromArgb(191, 222, 216), FontStyle.Regular);
        workspace.Controls.Add(workspaceSubtitle);
        var models = new FlowLayoutPanel
        {
            Location = new Point(24, 84),
            Size = new Size(1212, 220),
            Padding = new Padding(0),
            BackColor = Color.Transparent,
            WrapContents = true
        };
        models.Controls.Add(CreateServiceCard("01  /  SOUND DESIGN", "Woosh-DFlow", L("从文字提示生成短音效与环境声。", "Generate short effects and ambient sound from text prompts."), Capability(_woosh, "TEXT TO SFX"), L("启动 Woosh", "Launch Woosh"), Theme.MidTeal, () => _ = StartServiceAsync(_woosh)));
        models.Controls.Add(CreateServiceCard("02  /  AUDIO GENERATION", "Stable Audio 3", L("先选择音效、音乐或 medium 模型，再启动服务。", "Choose an SFX, music, or medium model before starting."), Capability(_smallSfx, "3 LOCAL MODELS"), L("选择模型", "Choose model"), Color.FromArgb(29, 117, 105), ShowStableModelSelector));
        models.Controls.Add(CreateServiceCard("03  /  CHARACTER VOICE", "IndexTTS2", L("以授权参考音频生成角色语音与情绪化对白。", "Create character voices and emotional dialogue from authorized references."), Capability(_indexTts, "VOICE & EMOTION"), L("启动 IndexTTS", "Launch IndexTTS"), Color.FromArgb(54, 87, 139), () => _ = StartServiceAsync(_indexTts)));
        var modelNumber = 4;
        foreach (var definition in CustomModelDefinitions())
        {
            var profile = CreateCustomProfile(definition);
            var capability = definition.RecommendedVramMiB > 0
                ? $"{definition.RecommendedVramMiB / 1024D:0.#} GB VRAM"
                : "CUSTOM MODEL";
            models.Controls.Add(CreateServiceCard(
                $"{modelNumber:00}  /  {definition.Category.ToUpperInvariant()}",
                definition.DisplayName,
                string.IsNullOrWhiteSpace(definition.Description) ? L("由模型目录配置驱动的本地服务。", "Local service managed by the model catalog.") : definition.Description,
                Capability(profile, capability),
                L("启动模型", "Launch model"),
                CategoryAccent(definition.Category),
                () => _ = StartServiceAsync(profile)));
            modelNumber++;
        }
        workspace.Controls.Add(models);

        var controlRow = new RoundedPanel
        {
            Size = new Size(1260, 120),
            Margin = new Padding(0, 0, 0, 12),
            FillColor = Theme.Card,
            CornerRadius = 25,
            BorderColor = Color.FromArgb(191, 211, 205),
            BorderWidth = 1,
            ShadowColor = Color.FromArgb(26, 0, 44, 42),
            ShadowOffset = 5
        };
        var controlTitle = CreateText(L("服务控制", "Service controls"), new Rectangle(30, 12, 240, 32), 13F, Theme.DeepTeal, FontStyle.Bold);
        var controlSubtitle = CreateText(L("不会结束未知程序。", "Unknown processes are never stopped."), new Rectangle(31, 49, 290, 25), 9F, Theme.Muted, FontStyle.Regular);
        controlRow.Controls.Add(controlTitle);
        controlRow.Controls.Add(controlSubtitle);
        var stopButton = CreateActionButton(L("停止已启动的 AI", "Stop active AI"), Theme.Coral, 190);
        stopButton.Location = new Point(360, 23);
        stopButton.Click += (_, _) => StopKnownServices();
        var refreshButton = CreateActionButton(L("刷新状态", "Refresh status"), Theme.MidTeal, 160);
        refreshButton.Location = new Point(562, 23);
        refreshButton.Click += (_, _) => RefreshStatus();
        _openButton = CreateActionButton(L("打开当前网页  →", "Open current UI  →"), Theme.DeepTeal, 200);
        _openButton.Location = new Point(734, 23);
        _openButton.Enabled = false;
        _openButton.Click += (_, _) => OpenActiveService();
        var checkUpdatesButton = CreateActionButton(L("检查更新", "Check updates"), Color.FromArgb(40, 108, 126), 164);
        checkUpdatesButton.Location = new Point(946, 23);
        checkUpdatesButton.Click += async (_, _) => await CheckUpdatesAsync();
        var updateSourcesButton = CreateActionButton(L("更新源码", "Update source"), Color.FromArgb(170, 102, 49), 176);
        updateSourcesButton.Location = new Point(1122, 23);
        updateSourcesButton.Click += async (_, _) => await UpdateSourcesAsync();
        controlRow.Controls.Add(stopButton);
        controlRow.Controls.Add(refreshButton);
        controlRow.Controls.Add(_openButton);
        controlRow.Controls.Add(checkUpdatesButton);
        controlRow.Controls.Add(updateSourcesButton);
        _statusLabel.Location = new Point(31, 78);
        _statusLabel.Size = new Size(1198, 30);
        _statusLabel.ForeColor = Theme.Muted;
        _statusLabel.Font = new Font("Microsoft YaHei UI", 8F);
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        controlRow.Controls.Add(_statusLabel);

        var logCard = new RoundedPanel
        {
            Size = new Size(1260, 164),
            Margin = new Padding(0),
            FillColor = Color.FromArgb(17, 60, 58),
            CornerRadius = 26,
            ShadowColor = Color.FromArgb(45, 0, 44, 42),
            ShadowOffset = 6
        };
        logCard.Controls.Add(CreateText(L("运行日志", "Runtime log"), new Rectangle(29, 13, 220, 31), 13F, Color.FromArgb(211, 239, 232), FontStyle.Bold));
        var allLogsButton = CreateActionButton(L("全部", "All"), Color.FromArgb(35, 104, 98), 68);
        allLogsButton.Location = new Point(820, 12);
        allLogsButton.Height = 28;
        allLogsButton.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold);
        allLogsButton.Click += (_, _) => SetLogFilter(LauncherLogFilter.All);
        var errorLogsButton = CreateActionButton(L("错误", "Errors"), Theme.Coral, 72);
        errorLogsButton.Location = new Point(896, 12);
        errorLogsButton.Height = 28;
        errorLogsButton.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold);
        errorLogsButton.Click += (_, _) => SetLogFilter(LauncherLogFilter.Errors);
        var currentLogsButton = CreateActionButton(L("当前", "Current"), Color.FromArgb(47, 83, 132), 72);
        currentLogsButton.Location = new Point(976, 12);
        currentLogsButton.Height = 28;
        currentLogsButton.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold);
        currentLogsButton.Click += (_, _) => SetLogFilter(LauncherLogFilter.CurrentService);
        var copyLogsButton = CreateActionButton(L("复制", "Copy"), Color.FromArgb(31, 121, 108), 72);
        copyLogsButton.Location = new Point(1056, 12);
        copyLogsButton.Height = 28;
        copyLogsButton.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold);
        copyLogsButton.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_log.Text))
            {
                Clipboard.SetText(_log.Text);
                AppendLog(L("已复制当前日志视图。", "Current log view copied."));
            }
        };
        var clearLogsButton = CreateActionButton(L("清空", "Clear"), Color.FromArgb(96, 99, 108), 72);
        clearLogsButton.Location = new Point(1136, 12);
        clearLogsButton.Height = 28;
        clearLogsButton.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold);
        clearLogsButton.Click += (_, _) =>
        {
            _logEntries.Clear();
            _logFilter = LauncherLogFilter.All;
            RenderLog();
        };
        var liveOutputLabel = CreateText("LIVE OUTPUT", new Rectangle(1136, 19, 96, 18), 8F, Color.FromArgb(134, 200, 187), FontStyle.Bold, ContentAlignment.MiddleRight);
        logCard.Controls.Add(allLogsButton);
        logCard.Controls.Add(errorLogsButton);
        logCard.Controls.Add(currentLogsButton);
        logCard.Controls.Add(copyLogsButton);
        logCard.Controls.Add(clearLogsButton);
        logCard.Controls.Add(liveOutputLabel);
        _log.Location = new Point(28, 48);
        _log.Size = new Size(1204, 98);
        _log.Name = "logOutput";
        _log.ReadOnly = true;
        _log.BorderStyle = BorderStyle.None;
        _log.BackColor = logCard.FillColor;
        _log.ForeColor = Color.FromArgb(216, 236, 230);
        _log.Font = new Font("Cascadia Mono", 9F);
        _log.Margin = new Padding(0);
        logCard.Controls.Add(_log);

        content.Controls.Add(header);
        content.Controls.Add(hero);
        content.Controls.Add(workspace);
        content.Controls.Add(controlRow);
        content.Controls.Add(logCard);
        canvas.Controls.Add(content);
        Controls.Add(canvas);

        void ApplyResponsiveLayout()
        {
            var width = Math.Max(1000, canvas.ClientSize.Width);
            content.Location = Point.Empty;
            content.Width = width;
            header.Width = width;
            hero.Width = width;
            workspace.Width = width;
            controlRow.Width = width;
            logCard.Width = width;

            livePill.Left = width - livePill.Width - 36;
            languageButton.Left = livePill.Left - languageButton.Width - 14;
            maintenanceButton.Left = languageButton.Left - maintenanceButton.Width - 12;
            caution.Left = width - caution.Width - 40;
            heroTitle.Width = Math.Max(430, caution.Left - heroTitle.Left - 28);
            heroSubtitle.Width = Math.Max(430, caution.Left - heroSubtitle.Left - 28);
            workspaceSubtitle.Width = width - workspaceSubtitle.Left - 36;

            models.Width = workspace.Width - 38;
            var columns = width >= 1000 ? 3 : 2;
            var tileWidth = Math.Max(300, (models.Width - columns * 12) / columns);
            var rows = (models.Controls.Count + columns - 1) / columns;
            foreach (var serviceCard in models.Controls.OfType<AnimatedServiceCard>())
            {
                LayoutServiceCard(serviceCard, tileWidth);
                serviceCard.Margin = new Padding(6, 5, 6, 5);
            }
            if (columns == 2 && models.Controls.Count % 2 == 1 && models.Controls[^1] is AnimatedServiceCard lastCard)
            {
                lastCard.Margin = new Padding((models.Width - lastCard.Width) / 2, 5, 6, 5);
            }
            models.Height = rows * 216 + 8;
            workspace.Height = models.Top + models.Height + 14;

            var actionGroupWidth = stopButton.Width + refreshButton.Width + _openButton.Width + checkUpdatesButton.Width + updateSourcesButton.Width + 48;
            var actionStart = Math.Max(340, width - actionGroupWidth - 44);
            stopButton.Left = actionStart;
            refreshButton.Left = stopButton.Right + 12;
            _openButton.Left = refreshButton.Right + 12;
            checkUpdatesButton.Left = _openButton.Right + 12;
            updateSourcesButton.Left = checkUpdatesButton.Right + 12;
            _statusLabel.Width = width - 62;
            liveOutputLabel.Left = width - liveOutputLabel.Width - 35;
            copyLogsButton.Left = liveOutputLabel.Left - copyLogsButton.Width - 12;
            clearLogsButton.Left = copyLogsButton.Left - clearLogsButton.Width - 8;
            currentLogsButton.Left = clearLogsButton.Left - currentLogsButton.Width - 8;
            errorLogsButton.Left = currentLogsButton.Left - errorLogsButton.Width - 8;
            allLogsButton.Left = errorLogsButton.Left - allLogsButton.Width - 8;
            if (logCard.Controls["logOutput"] is { } logOutput)
            {
                logOutput.Width = width - 56;
            }
        }

        canvas.SizeChanged += (_, _) => ApplyResponsiveLayout();
        ApplyResponsiveLayout();
    }

    private AnimatedServiceCard CreateServiceCard(string index, string title, string description, string capability, string actionText, Color accent, Action action)
    {
        return new AnimatedServiceCard
        {
            Size = new Size(286, 200),
            Margin = new Padding(6, 5, 6, 5),
            IndexText = index,
            TitleText = title,
            DescriptionText = description,
            CapabilityText = capability,
            ActionText = actionText,
            AccentColor = accent,
            InvokeAction = action
        };
    }

    private string Capability(ServiceProfile profile, string label)
    {
        return GetMissingRequirements(profile).Count == 0
            ? $"{L("已安装", "READY")} · {label}"
            : $"{L("缺失文件", "MISSING")} · {label}";
    }

    private static Color CategoryAccent(string category)
    {
        return category.ToUpperInvariant() switch
        {
            "TTS" or "VOICE" or "CHARACTER VOICE" => Color.FromArgb(54, 87, 139),
            "MUSIC" or "AUDIO GENERATION" => Color.FromArgb(29, 117, 105),
            "SOUND" or "SOUND DESIGN" => Theme.MidTeal,
            "IMAGE GENERATION" or "VISION" => Color.FromArgb(183, 83, 70),
            "VIDEO GENERATION" or "3D GENERATION" => Color.FromArgb(170, 105, 42),
            "LLM / CHAT" or "CODING" => Color.FromArgb(49, 104, 151),
            "UTILITIES" => Color.FromArgb(91, 105, 111),
            _ => Color.FromArgb(132, 79, 145)
        };
    }

    private void ShowStableModelSelector()
    {
        using var dialog = new StableModelSelectorForm(_smallSfx, _smallMusic, _medium, _useEnglish);
        if (dialog.ShowDialog(this) == DialogResult.OK && dialog.SelectedProfile is not null)
        {
            _ = StartServiceAsync(dialog.SelectedProfile);
        }
    }

    private static void LayoutServiceCard(AnimatedServiceCard card, int width)
    {
        card.Width = width;
        card.Height = 200;
    }

    private static RoundedButton CreateActionButton(string text, Color color, int width)
    {
        return new RoundedButton
        {
            Text = text,
            Width = width,
            Height = 38,
            Margin = new Padding(0),
            FillColor = color,
            ForeColor = Color.White
        };
    }

    private static SafeTextLabel CreateText(string text, Rectangle bounds, float size, Color color, FontStyle style, ContentAlignment alignment = ContentAlignment.MiddleLeft)
    {
        return new SafeTextLabel
        {
            Text = text,
            Bounds = bounds,
            Font = new Font("Microsoft YaHei UI", size, style),
            ForeColor = color,
            BackColor = Color.Transparent,
            TextAlign = alignment,
            Margin = Padding.Empty
        };
    }

    private static ParagraphLabel CreateParagraph(string text, Rectangle bounds, float size, Color color, FontStyle style)
    {
        return new ParagraphLabel
        {
            Text = text,
            Bounds = bounds,
            Font = new Font("Microsoft YaHei UI", size, style),
            ForeColor = color,
            BackColor = Color.Transparent,
            Margin = Padding.Empty
        };
    }

    private async Task StartServiceAsync(ServiceProfile profile)
    {
        SetServiceRuntimeState(profile, ServiceRuntimeState.Checking);
        SetRuntimePhase("正在检查启动环境", $"Checking {profile.Name}");
        var definition = _modelCatalog.Models.FirstOrDefault(model =>
            Path.GetFullPath(model.RootDirectory).Equals(Path.GetFullPath(profile.WorkingDirectory), StringComparison.OrdinalIgnoreCase));
        if (definition is not null)
        {
            var trust = InstalledPluginTrustValidator.Verify(definition, _trustedPublishers);
            if (!trust.IsTrusted)
            {
                SetServiceRuntimeState(profile, ServiceRuntimeState.Error);
                SetRuntimePhase("插件信任验证失败", $"Trust validation failed for {profile.Name}");
                AppendLog(L("插件信任验证失败：", "Plugin trust validation failed: ") + trust.Message, profile, true);
                ShowActionableError(L("已阻止不可信启动命令", "Untrusted launch command blocked"), trust.Message, profile);
                return;
            }
        }
        var missing = GetMissingRequirements(profile);
        if (missing.Count > 0)
        {
            SetServiceRuntimeState(profile, ServiceRuntimeState.Missing);
            SetRuntimePhase("环境未就绪", $"{profile.Name} requires attention");
            AppendLog(L($"环境检查失败：{string.Join(", ", missing)}", $"Environment check failed: {string.Join(", ", missing)}"), profile, true);
            ShowActionableError(
                L("环境未就绪", "Environment not ready"),
                L($"{profile.Name} 无法启动，以下文件或目录缺失：\n", $"{profile.Name} cannot start. Required files or directories are missing:\n") + string.Join(Environment.NewLine, missing),
                profile);
            return;
        }

        var knownPids = GetKnownServicePids();
        var occupied = GetListeningPids(profile.Port);
        var assessment = ResourceScheduler.Assess(profile, knownPids, occupied);
        if (assessment.Conflicts.Count > 0)
        {
            var lines = assessment.Conflicts.Select(conflict =>
                $"{(conflict.Severity == ResourceConflictSeverity.Blocking ? "[BLOCK]" : "[WARN]")} {conflict.Message}");
            var resourceLine = L(
                $"系统内存：{assessment.Snapshot.AvailableMemoryMiB:N0} / {assessment.Snapshot.TotalMemoryMiB:N0} MiB 可用",
                $"System memory: {assessment.Snapshot.AvailableMemoryMiB:N0} / {assessment.Snapshot.TotalMemoryMiB:N0} MiB available");
            var message = string.Join(Environment.NewLine, lines) + Environment.NewLine + resourceLine;
            if (assessment.Snapshot.GpuTotalMiB is not null && assessment.Snapshot.GpuUsedMiB is not null)
            {
                message += Environment.NewLine + L(
                    $"GPU 显存：{assessment.Snapshot.GpuUsedMiB:N0} / {assessment.Snapshot.GpuTotalMiB:N0} MiB 已使用",
                    $"GPU memory: {assessment.Snapshot.GpuUsedMiB:N0} / {assessment.Snapshot.GpuTotalMiB:N0} MiB used");
            }
            AppendLog(L("启动资源评估：", "Launch resource assessment: ") + message.Replace(Environment.NewLine, " | "), profile, assessment.BlocksLaunch);
            if (assessment.BlocksLaunch)
            {
                SetServiceRuntimeState(profile, ServiceRuntimeState.Error);
                SetRuntimePhase("资源或端口冲突阻止启动", $"Resource conflict blocked {profile.Name}");
                ShowActionableError(L("无法安全启动", "Launch blocked"), message, profile);
                return;
            }
            if (assessment.RequiresConfirmation && MessageBox.Show(
                    message + Environment.NewLine + Environment.NewLine + L("启动器将先停止已管理的冲突进程。仍要继续吗？", "Managed conflicting processes will be stopped first. Continue?"),
                    L("启动资源评估", "Launch resource assessment"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                SetServiceRuntimeState(profile, ServiceRuntimeState.Ready);
                return;
            }
        }

        if (assessment.ManagedProcessIds.Count > 0)
        {
            SetRuntimePhase("正在释放 AI 资源", $"Stopping conflicting AI process(es): {string.Join(", ", assessment.ManagedProcessIds)}");
            StopProcesses(assessment.ManagedProcessIds);
            await Task.Delay(700);
        }

        try
        {
            SetServiceRuntimeState(profile, ServiceRuntimeState.Starting);
            SetRuntimePhase("正在启动模型进程", $"Launching {profile.Name}");
            var startInfo = new ProcessStartInfo(profile.Executable, profile.Arguments)
            {
                WorkingDirectory = profile.WorkingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            if (profile.WorkingDirectory.Equals(_settings.StableRoot, StringComparison.OrdinalIgnoreCase))
            {
                startInfo.Environment["HF_HUB_DISABLE_XET"] = "1";
                startInfo.Environment["PYTHONUNBUFFERED"] = "1";
                if (profile.IsMedium)
                {
                    startInfo.Environment["PYTORCH_CUDA_ALLOC_CONF"] = "expandable_segments:True";
                }
            }

            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) AppendLog(e.Data, profile); };
            process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) AppendLog("ERR " + e.Data, profile, true); };
            process.Exited += (_, _) => BeginInvoke(() =>
            {
                if (_activeProcess?.Id == process.Id)
                {
                    _activeProcess = null;
                    _activeService = null;
                    _openButton.Enabled = false;
                    SetRuntimePhase("服务进程已退出", $"{profile.Name} exited");
                    AppendLog(L($"服务进程已退出（PID {process.Id}）。", $"Service process exited (PID {process.Id})."), profile, true);
                    SetServiceRuntimeState(profile, ServiceRuntimeState.Error);
                }
                RefreshStatus();
            });
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _activeService = profile;
            _activeProcess = process;
            _openButton.Enabled = false;
            AppendLog(L($"已启动 {profile.Name}（PID {process.Id}），正在加载模型。", $"Started {profile.Name} (PID {process.Id}); loading model."), profile);
            SetRuntimePhase("正在加载模型并等待 WebUI", $"{profile.Name}: waiting for port {profile.Port}");
            RefreshStatus();
            await WaitForServiceReadyAsync(profile, process);
        }
        catch (Exception ex)
        {
            SetServiceRuntimeState(profile, ServiceRuntimeState.Error);
            SetRuntimePhase("服务启动失败", $"{profile.Name} could not be launched");
            AppendLog(L("启动失败：", "Launch failed: ") + ex.Message, profile, true);
            ShowActionableError(L("启动器错误", "Launcher error"), ex.Message, profile);
        }
    }

    private async Task WaitForServiceReadyAsync(ServiceProfile profile, Process process)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(90))
        {
            if (process.HasExited)
            {
                return;
            }
            if (GetListeningPids(profile.Port).Count > 0)
            {
                SetServiceRuntimeState(profile, ServiceRuntimeState.Running);
                _openButton.Enabled = true;
                SetRuntimePhase("服务已就绪", $"{profile.Name} is ready on port {profile.Port}");
                AppendLog(L($"服务就绪：http://127.0.0.1:{profile.Port}", $"Service ready: http://127.0.0.1:{profile.Port}"), profile);
                RefreshStatus();
                return;
            }
            if (timer.Elapsed.Seconds is 15 or 30 or 60)
            {
                SetRuntimePhase("模型仍在加载", $"{profile.Name}: waiting for port {profile.Port} ({timer.Elapsed.Seconds}s)");
            }
            await Task.Delay(1000);
        }

        SetRuntimePhase("加载时间较长，服务可能仍在启动", $"{profile.Name}: still waiting for port {profile.Port}");
        AppendLog(L("等待服务端口超时，请查看错误日志或稍后刷新状态。", "Timed out waiting for the service port. Check errors or refresh status later."), profile, true);
        RefreshStatus();
    }

    private void ShowActionableError(string title, string message, ServiceProfile profile)
    {
        using var dialog = new Form
        {
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(760, 250),
            MinimumSize = new Size(640, 220),
            BackColor = Theme.Card,
            Font = new Font("Microsoft YaHei UI", 10F),
            ShowInTaskbar = false
        };
        var content = new TextBox
        {
            Text = message,
            ReadOnly = true,
            Multiline = true,
            BorderStyle = BorderStyle.None,
            BackColor = Theme.Card,
            ForeColor = Theme.Ink,
            Location = new Point(28, 24),
            Size = new Size(704, 128),
            ScrollBars = ScrollBars.Vertical
        };
        var check = new Button { Text = L("环境自检", "Environment check"), Location = new Point(150, 184), Size = new Size(132, 38) };
        check.Click += (_, _) => ShowEnvironmentReport();
        var folder = new Button { Text = L("打开模型目录", "Open model folder"), Location = new Point(292, 184), Size = new Size(132, 38) };
        folder.Click += (_, _) => Process.Start(new ProcessStartInfo("explorer.exe", $"\"{profile.WorkingDirectory}\"") { UseShellExecute = true });
        var logs = new Button { Text = L("查看日志", "View logs"), Location = new Point(434, 184), Size = new Size(122, 38) };
        logs.Click += (_, _) => OpenProfileLogFolder(profile);
        var close = new Button { Text = L("关闭", "Close"), DialogResult = DialogResult.OK, Location = new Point(566, 184), Size = new Size(142, 38) };
        dialog.Controls.Add(content);
        dialog.Controls.Add(check);
        dialog.Controls.Add(folder);
        dialog.Controls.Add(logs);
        dialog.Controls.Add(close);
        dialog.AcceptButton = close;
        dialog.ShowDialog(this);
    }

    private void OpenProfileLogFolder(ServiceProfile profile)
    {
        var candidates = new[]
        {
            Path.Combine(profile.WorkingDirectory, "logs"),
            Path.Combine(profile.WorkingDirectory, "generated_audio"),
            profile.WorkingDirectory
        };
        var target = candidates.First(Directory.Exists);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{target}\"") { UseShellExecute = true });
    }

    private void StopKnownServices()
    {
        var pids = GetKnownServicePids();
        if (pids.Count == 0)
        {
            AppendLog("未检测到由本启动器管理的 AI 服务。");
            RefreshStatus();
            return;
        }

        var active = _activeService;
        if (active is not null)
        {
            SetServiceRuntimeState(active, ServiceRuntimeState.Stopping);
        }
        StopProcesses(pids);
        _activeService = null;
        _activeProcess = null;
        _openButton.Enabled = false;
        SetRuntimePhase("已停止启动器管理的服务", "Launcher-managed services stopped");
        if (active is not null)
        {
            SetServiceRuntimeState(active, ServiceRuntimeState.Ready);
        }
        RefreshStatus();
    }

    private void HandleLauncherFormClosing(object? sender, FormClosingEventArgs e)
    {
        var running = GetKnownServicePids();
        if (running.Count == 0)
        {
            return;
        }

        var answer = MessageBox.Show(
            L($"检测到 {running.Count} 个已部署 AI 服务仍在运行。\n\n是：停止服务后退出\n否：保持后台运行并退出\n取消：留在启动器", $"{running.Count} deployed AI service(s) are still running.\n\nYes: stop services and exit\nNo: keep running in the background and exit\nCancel: remain in launcher"),
            L("退出启动器", "Exit launcher"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (answer == DialogResult.Cancel)
        {
            e.Cancel = true;
            return;
        }
        if (answer == DialogResult.Yes)
        {
            StopProcesses(running);
        }
    }

    private void StopProcesses(IReadOnlyCollection<int> pids)
    {
        var result = PluginProcessService.Stop(pids);
        foreach (var failure in result.Failures)
        {
            AppendLog($"停止 PID {failure.ProcessId} 失败：{failure.Message}");
        }

        AppendLog(result.StoppedProcessIds.Count > 0
            ? $"已停止已识别 AI 服务进程：{string.Join(", ", result.StoppedProcessIds)}"
            : "未能停止任何服务进程。");
    }

    private void OpenActiveService()
    {
        if (_activeService is null)
        {
            return;
        }

        Process.Start(new ProcessStartInfo($"http://127.0.0.1:{_activeService.Port}") { UseShellExecute = true });
    }

    private void RefreshStatus()
    {
        var wooshPids = GetListeningPids(_settings.WooshPort);
        var stablePids = GetListeningPids(_settings.StablePort);
        var indexPids = GetListeningPids(_settings.IndexTtsPort);
        var known = GetKnownServicePids();
        var gpu = GetGpuMemoryUsage();
        var parts = new List<string>();
        parts.Add(wooshPids.Count > 0 ? L($"Woosh {_settings.WooshPort} 正在监听", $"Woosh {_settings.WooshPort} listening") : L($"Woosh {_settings.WooshPort} 未监听", $"Woosh {_settings.WooshPort} idle"));
        parts.Add(stablePids.Count > 0 ? L($"Stable {_settings.StablePort} 正在监听", $"Stable {_settings.StablePort} listening") : L($"Stable {_settings.StablePort} 未监听", $"Stable {_settings.StablePort} idle"));
        parts.Add(indexPids.Count > 0 ? L($"IndexTTS {_settings.IndexTtsPort} 正在监听", $"IndexTTS {_settings.IndexTtsPort} listening") : L($"IndexTTS {_settings.IndexTtsPort} 未监听", $"IndexTTS {_settings.IndexTtsPort} idle"));
        parts.Add(known.Count > 0 ? L($"已识别 AI 进程：{string.Join(",", known)}", $"Recognized AI processes: {string.Join(",", known)}") : L("未检测到已识别 AI 进程", "No recognized AI process"));
        if (gpu is not null)
        {
            parts.Add(L($"GPU {gpu.Value.UsedMiB}/{gpu.Value.TotalMiB} MiB", $"GPU {gpu.Value.UsedMiB}/{gpu.Value.TotalMiB} MiB"));
        }
        if (!string.IsNullOrWhiteSpace(_backgroundUpdateStatusChinese) || !string.IsNullOrWhiteSpace(_backgroundUpdateStatusEnglish))
        {
            parts.Add(L(_backgroundUpdateStatusChinese, _backgroundUpdateStatusEnglish));
        }
        _statusLabel.Text = string.Join("    |    ", parts);
        UpdateGpuIndicator();
        UpdatePluginUi();
    }

    private List<int> GetKnownServicePids()
    {
        var roots = new[] { _settings.WooshRoot, _settings.StableRoot, _settings.IndexTtsRoot }
            .Concat(CustomModelDefinitions().Select(definition => definition.RootDirectory))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        return PluginProcessService.FindProcessesByPluginRoots(roots);
    }

    private static List<int> GetListeningPids(int port)
        => PluginProcessService.GetListeningProcessIds(port);

    private static (int UsedMiB, int TotalMiB)? GetGpuMemoryUsage()
        => SystemResourceProbe.ReadGpuMemory();

    private void AppendLog(string message, ServiceProfile? service = null, bool isError = false)
    {
        if (IsDisposed)
        {
            return;
        }
        if (InvokeRequired)
        {
            BeginInvoke(() => AppendLog(message, service, isError));
            return;
        }
        var error = isError || message.StartsWith("ERR ", StringComparison.OrdinalIgnoreCase)
            || message.Contains("失败", StringComparison.OrdinalIgnoreCase)
            || message.Contains("error", StringComparison.OrdinalIgnoreCase)
            || message.Contains("exception", StringComparison.OrdinalIgnoreCase);
        _logEntries.Add(new LauncherLogEntry(DateTime.Now, message, service?.Name ?? _activeService?.Name, error));
        _diagnosticsService.Append(message, service?.Name ?? _activeService?.Name, error);
        RenderLog();
    }

    private void ExportDiagnostics()
    {
        using var dialog = new SaveFileDialog
        {
            Title = L("导出诊断日志", "Export diagnostics"),
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            FileName = $"bachen-launcher-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.txt"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var lines = new List<string>
        {
            $"Launcher version: {LauncherVersion}",
            $"OS: {Environment.OSVersion}",
            $"64-bit OS: {Environment.Is64BitOperatingSystem}",
            $"Process architecture: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}",
            $"Data root: {_settings.DataRoot}",
            $"Timestamp: {DateTimeOffset.Now:O}",
            "",
            "Runtime log:"
        };
        lines.AddRange(_logEntries.Select(entry => $"[{entry.Timestamp:O}] [{entry.ServiceName ?? "Launcher"}] {(entry.IsError ? "ERROR" : "INFO")} {entry.Message}"));
        lines.Add("");
        lines.AddRange(_diagnosticsService.ReadPersistentLogs());
        var output = _diagnosticsService.Export(dialog.FileName, lines);
        MessageBox.Show(L($"诊断包已导出：\n{output}", $"Diagnostics exported to:\n{output}"), L("导出完成", "Export complete"), MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
