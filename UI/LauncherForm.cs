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

internal static class GlassPaint
{
    public static void DrawReflection(
        Graphics graphics,
        GraphicsPath clipPath,
        Rectangle bounds,
        int topOpacity,
        float sheenProgress = -1F,
        int sheenOpacity = 0)
    {
        if (bounds.Width <= 1 || bounds.Height <= 1)
        {
            return;
        }

        var state = graphics.Save();
        graphics.SetClip(clipPath);
        var highlightBounds = new Rectangle(bounds.Left, bounds.Top, bounds.Width, Math.Max(2, bounds.Height / 2));
        using (var highlight = new LinearGradientBrush(
                   highlightBounds,
                   Color.FromArgb(Math.Clamp(topOpacity, 0, 255), Color.White),
                   Color.FromArgb(0, Color.White),
                   LinearGradientMode.Vertical))
        {
            graphics.FillRectangle(highlight, highlightBounds);
        }

        if (sheenProgress >= 0F && sheenOpacity > 0)
        {
            var stripWidth = Math.Max(24, bounds.Width / 5);
            var stripLeft = bounds.Left - stripWidth +
                (int)Math.Round((bounds.Width + stripWidth * 2D) * Math.Clamp(sheenProgress, 0F, 1F));
            var stripBounds = new Rectangle(stripLeft, bounds.Top, stripWidth, bounds.Height);
            using var sheen = new LinearGradientBrush(stripBounds, Color.Transparent, Color.Transparent, 0F);
            sheen.InterpolationColors = new ColorBlend
            {
                Positions = [0F, 0.42F, 0.5F, 0.58F, 1F],
                Colors =
                [
                    Color.Transparent,
                    Color.FromArgb(0, Color.White),
                    Color.FromArgb(Math.Clamp(sheenOpacity, 0, 255), Color.White),
                    Color.FromArgb(0, Color.White),
                    Color.Transparent
                ]
            };
            graphics.FillRectangle(sheen, stripBounds);
        }
        graphics.Restore(state);
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
    private readonly System.Windows.Forms.Timer _animationTimer = new() { Interval = 16 };
    private Color _fillColor = Theme.DeepTeal;
    public Color FillColor
    {
        get => _fillColor;
        set
        {
            _fillColor = value;
            Invalidate();
        }
    }
    private bool _pressed;
    private float _hoverProgress;
    private float _hoverTarget;

    public RoundedButton()
    {
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        TabStop = true;
        _animationTimer.Tick += (_, _) => AnimateHover();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hoverTarget = 1F;
        _animationTimer.Start();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hoverTarget = 0F;
        _pressed = false;
        _animationTimer.Start();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        _pressed = Enabled && e.Button == MouseButtons.Left;
        if (_pressed) Focus();
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
        var background = BackColor.A > 0 ? BackColor : PaintSurface.ResolveParentColor(this);
        e.Graphics.Clear(background);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var pressOffset = _pressed ? 1 : 0;
        var color = !Enabled
            ? Color.FromArgb(176, 190, 187)
            : _pressed ? ControlPaint.Dark(FillColor, 0.14F)
            : Blend(FillColor, Color.White, _hoverProgress * 0.08F);
        var bodyBounds = new Rectangle(0, pressOffset, Width - 1, Height - 1 - pressOffset);
        using var path = CreatePillPath(bodyBounds);
        using var brush = new SolidBrush(color);
        e.Graphics.FillPath(brush, path);
        GlassPaint.DrawReflection(
            e.Graphics,
            path,
            bodyBounds,
            Enabled ? 8 + (int)Math.Round(_hoverProgress * 8) : 6,
            _hoverProgress * 0.35F,
            Enabled ? (int)Math.Round(_hoverProgress * 14) : 0);
        using var innerBorder = new Pen(Color.FromArgb(Enabled ? 20 : 12, Color.White), 1F);
        e.Graphics.DrawPath(innerBorder, path);
        if (Focused && Enabled)
        {
            using var focusPath = CreatePillPath(Rectangle.Inflate(bodyBounds, -3, -3));
            using var focusPen = new Pen(Color.FromArgb(170, Theme.MidTeal), 1.2F);
            e.Graphics.DrawPath(focusPen, focusPath);
        }
        PaintSurface.DrawText(
            e.Graphics,
            Text,
            Font,
            PaintSurface.TextBounds(new Rectangle(0, pressOffset, Width, Height - pressOffset)),
            Enabled ? ForeColor : Color.FromArgb(245, 248, 247),
            StringAlignment.Center);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        if (!Enabled)
        {
            _pressed = false;
            _hoverTarget = 0F;
            _animationTimer.Stop();
            _hoverProgress = 0F;
        }
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        Invalidate();
        base.OnGotFocus(e);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        _pressed = false;
        Invalidate();
        base.OnLostFocus(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Enabled && e.KeyCode is Keys.Enter or Keys.Space)
        {
            _pressed = true;
            e.Handled = true;
            Invalidate();
        }
        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (_pressed && e.KeyCode is Keys.Enter or Keys.Space)
        {
            _pressed = false;
            e.Handled = true;
            Invalidate();
            OnClick(EventArgs.Empty);
        }
        base.OnKeyUp(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _animationTimer.Dispose();
        }
        base.Dispose(disposing);
    }

    private void AnimateHover()
    {
        var delta = _hoverTarget - _hoverProgress;
        if (Math.Abs(delta) <= 0.02F)
        {
            _hoverProgress = _hoverTarget;
            _animationTimer.Stop();
        }
        else
        {
            _hoverProgress = Math.Clamp(_hoverProgress + Math.Sign(delta) * 0.12F, 0F, 1F);
        }
        Invalidate();
    }

    private static Color Blend(Color from, Color to, float amount)
    {
        amount = Math.Clamp(amount, 0F, 1F);
        return Color.FromArgb(
            from.A,
            (int)Math.Round(from.R + (to.R - from.R) * amount),
            (int)Math.Round(from.G + (to.G - from.G) * amount),
            (int)Math.Round(from.B + (to.B - from.B) * amount));
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
        var background = BackColor.A > 0 ? BackColor : PaintSurface.ResolveParentColor(this);
        e.Graphics.Clear(background);
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
        TabStop = true;
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

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) Focus();
        base.OnMouseDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            e.Handled = true;
            InvokeAction?.Invoke();
        }
        base.OnKeyUp(e);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        Invalidate();
        base.OnGotFocus(e);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        Invalidate();
        base.OnLostFocus(e);
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
        GlassPaint.DrawReflection(
            e.Graphics,
            body,
            new Rectangle(1, 1, Width - 3, Height - 3),
            _selected ? 38 : 24 + (int)Math.Round(_hoverProgress * 14),
            _hoverProgress,
            (int)Math.Round(_hoverProgress * 34));
        using var border = new Pen(_selected || Focused ? AccentColor : Color.FromArgb(210, 225, 220), _selected || Focused ? 2F : 1F);
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

internal sealed class GlassProgressBar : Control
{
    private readonly System.Windows.Forms.Timer _animationTimer = new() { Interval = 28 };
    private int _minimum;
    private int _maximum = 100;
    private int _value;
    private ProgressBarStyle _style = ProgressBarStyle.Continuous;
    private float _animationProgress;

    public Color TrackColor { get; init; } = Color.FromArgb(220, 234, 230);
    public Color FillColor { get; init; } = Color.FromArgb(36, 145, 124);
    public Color BorderColor { get; init; } = Color.FromArgb(145, 188, 178);

    public int Minimum
    {
        get => _minimum;
        set
        {
            _minimum = value;
            if (_maximum <= _minimum) _maximum = _minimum + 1;
            Value = _value;
        }
    }

    public int Maximum
    {
        get => _maximum;
        set
        {
            _maximum = Math.Max(_minimum + 1, value);
            Value = _value;
        }
    }

    public int Value
    {
        get => _value;
        set
        {
            var next = Math.Clamp(value, _minimum, _maximum);
            if (_value == next) return;
            _value = next;
            UpdateAnimationState();
            Invalidate();
        }
    }

    public ProgressBarStyle Style
    {
        get => _style;
        set
        {
            if (_style == value) return;
            _style = value;
            UpdateAnimationState();
            Invalidate();
        }
    }

    public int MarqueeAnimationSpeed
    {
        get => _animationTimer.Interval;
        set => _animationTimer.Interval = Math.Clamp(value, 16, 100);
    }

    public GlassProgressBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        _animationTimer.Tick += (_, _) =>
        {
            _animationProgress += _style == ProgressBarStyle.Marquee ? 0.045F : 0.018F;
            if (_animationProgress > 1F) _animationProgress -= 1F;
            Invalidate();
        };
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        UpdateAnimationState();
        base.OnVisibleChanged(e);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UpdateAnimationState();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        _animationTimer.Stop();
        base.OnHandleDestroyed(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        UpdateAnimationState();
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(PaintSurface.ResolveParentColor(this));
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (Width <= 2 || Height <= 2)
        {
            return;
        }

        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using var trackPath = RoundedPath(bounds, Height / 2);
        using var trackBrush = new SolidBrush(Enabled ? TrackColor : ControlPaint.Light(TrackColor, 0.3F));
        e.Graphics.FillPath(trackBrush, trackPath);
        GlassPaint.DrawReflection(e.Graphics, trackPath, bounds, 38);

        var state = e.Graphics.Save();
        e.Graphics.SetClip(trackPath);
        if (_style == ProgressBarStyle.Marquee)
        {
            var segmentWidth = Math.Max(Height * 3, Width / 3);
            var segmentLeft = -segmentWidth + (int)Math.Round((Width + segmentWidth) * _animationProgress);
            PaintFill(e.Graphics, new Rectangle(segmentLeft, 0, segmentWidth, Height - 1));
        }
        else
        {
            var ratio = Math.Clamp((_value - _minimum) / (double)(_maximum - _minimum), 0D, 1D);
            var fillWidth = (int)Math.Round(bounds.Width * ratio);
            if (fillWidth > 0)
            {
                PaintFill(e.Graphics, new Rectangle(0, 0, Math.Max(Height, fillWidth), Height - 1));
            }
        }
        e.Graphics.Restore(state);

        using var border = new Pen(BorderColor, 1F);
        e.Graphics.DrawPath(border, trackPath);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _animationTimer.Dispose();
        }
        base.Dispose(disposing);
    }

    private void PaintFill(Graphics graphics, Rectangle fillBounds)
    {
        using var fillPath = RoundedPath(fillBounds, Height / 2);
        using var fillBrush = new SolidBrush(Enabled ? FillColor : ControlPaint.Light(FillColor, 0.3F));
        graphics.FillPath(fillBrush, fillPath);
        GlassPaint.DrawReflection(graphics, fillPath, fillBounds, 72, _animationProgress, 82);
    }

    private void UpdateAnimationState()
    {
        if (IsHandleCreated && Visible && Enabled && (_style == ProgressBarStyle.Marquee || _value > _minimum))
        {
            _animationTimer.Start();
        }
        else
        {
            _animationTimer.Stop();
        }
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

internal sealed record LauncherLogEntry(DateTime Timestamp, string Message, string? EnglishMessage, string? ServiceName, bool IsError)
{
    public string DisplayMessage(bool useEnglish)
        => useEnglish && !string.IsNullOrWhiteSpace(EnglishMessage) ? EnglishMessage : Message;
}

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

    private HttpClient _githubClient = null!;
    private PluginPackageService _pluginPackageService = null!;
    private PluginDownloadService _pluginDownloadService = null!;
    private PluginCatalogService _pluginCatalogService = null!;
    private GitHubModelImportService _gitHubModelImportService = null!;
    private ExternalModelAuthorizationService _authorizationService = null!;
    private GitHubUpdateService _sourceUpdateService = null!;
    private LauncherSelfUpdateService _launcherUpdateService = null!;
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
    private readonly Dictionary<string, SourceUpdateCheck> _availableSourceUpdates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PluginListItem> _pluginItems = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Windows.Forms.Timer _gpuRefreshTimer = new() { Interval = 5000 };
    private readonly ToolTip _toolTip = new();
    private RoundedButton _openButton = new();
    private RoundedButton? _detailPrimaryButton;
    private RoundedButton? _detailUpdateButton;
    private RoundedButton? _detailStopButton;
    private RoundedButton? _detailUninstallButton;
    private RoundedPanel? _detailPanel;
    private Panel? _detailActionsPanel;
    private GlassProgressBar? _detailUpdateProgress;
    private bool _pluginUpdateInProgress;
    private string? _updatingPluginId;
    private SourceUpdateProgress? _latestPluginUpdateProgress;
    private ParagraphLabel? _detailActionHint;
    private SafeTextLabel? _gpuSummaryLabel;
    private SafeTextLabel? _gpuNameLabel;
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
    private SafeTextLabel? _logFollowStateLabel;
    private SafeTextLabel? _pluginCountLabel;
    private FlowLayoutPanel? _stableModePanel;
    private FlowLayoutPanel? _pluginList;
    private TextBox? _pluginSearchBox;
    private ComboBox? _pluginCategoryFilter;
    private Panel? _logHost;
    private GpuMeter? _gpuMeter;
    private List<PluginUiEntry> _pluginEntries = [];
    private string _selectedPluginId = "woosh-dflow";
    private bool _logAutoFollow = true;
    private int _unseenLogEntryCount;
    private ServiceProfile? _activeService;
    private Process? _activeProcess;
    private bool _useEnglish;
    private bool _updateBusy;
    private string _phaseChinese = "未启动服务";
    private string _phaseEnglish = "No service started";
    private LauncherLogFilter _logFilter = LauncherLogFilter.All;
    private string _pluginSearchQuery = string.Empty;
    private string _pluginCategory = "*";

    private ServiceProfile? _woosh;
    private ServiceProfile? _smallSfx;
    private ServiceProfile? _smallMusic;
    private ServiceProfile? _medium;
    private ServiceProfile? _indexTts;
    private ServiceProfile? _selectedStableProfile;
    private string _backgroundUpdateStatusChinese = string.Empty;
    private string _backgroundUpdateStatusEnglish = string.Empty;
    private SettingsWorkspace? _settingsWorkspace;
    private Panel? _logResizeGrip;
    private bool _resizingLogWindow;
    private int _logResizeStartScreenY;
    private int _logResizeStartHeight;
    private int _logResizeStartLogHeight;

    public LauncherForm()
    {
        AutoScaleMode = AutoScaleMode.None;
        Directory.CreateDirectory(LauncherPaths.UserConfigDirectory);
        MigrateLegacyConfiguration();
        _settings = File.Exists(SettingsPath) ? LoadSettings() : CreateFirstRunSettings();
        NormalizeSettings(_settings);
        EnsureDataDirectories(_settings);
        SaveSettings(_settings);
        ConfigureGitHubServices(_settings.GitHubProxyUrl);
        _modelCatalog = LoadModelCatalog();
        _trustedPublishers = TrustedPublisherStoreService.Load(TrustedPublishersPath);
        MigrateRenamedCatalogPaths(_modelCatalog);
        MigrateLegacyBuiltInCatalogEntries();
        SaveModelCatalog(_modelCatalog);
        ArchiveMigratedLegacyConfiguration(_settings);
        ConfigureProfiles();
        _selectedStableProfile ??= _smallSfx;
        InitializeUi();
        RefreshStatus();
        _gpuRefreshTimer.Tick += (_, _) => UpdateGpuIndicator();
        Shown += async (_, _) =>
        {
            _gpuRefreshTimer.Start();
            if (!_settings.FirstRunCompleted)
            {
                await ShowFirstRunWizardAsync();
            }
            await CheckUpdatesInBackgroundAsync();
            await CheckLauncherUpdateInBackgroundAsync();
        };
        FormClosed += (_, _) =>
        {
            _gpuRefreshTimer.Dispose();
        };
        FormClosing += HandleLauncherFormClosing;
    }

    private void ConfigureProfiles()
    {
        var updateSources = new List<GitHubUpdateSource>();
        foreach (var definition in _modelCatalog.Models.Where(definition => !string.IsNullOrWhiteSpace(definition.GitHubRepository)))
        {
            updateSources.Add(new GitHubUpdateSource(
                definition.DisplayName,
                definition.GitHubRepository.Trim(),
                string.IsNullOrWhiteSpace(definition.GitHubBranch) ? "main" : definition.GitHubBranch.Trim(),
                definition.RootDirectory,
                definition.PreservedPaths ?? [],
                ["pyproject.toml", "requirements.txt", "uv.lock"]));
        }
        _updateSources = updateSources.ToArray();
        _woosh = CreateSpecialProfile("woosh-dflow");
        var stableDefinition = _modelCatalog.Models.FirstOrDefault(IsStableAudioDefinition);
        _smallSfx = CreateStableAudioProfile(stableDefinition, "small-sfx", "Small SFX", false, 2200, 8192);
        _smallMusic = CreateStableAudioProfile(stableDefinition, "small-music", "Small Music", false, 2200, 8192);
        _medium = CreateStableAudioProfile(stableDefinition, "medium", "Medium", true, 8800, 16384);
        _indexTts = CreateSpecialProfile("indextts2");
        if (_selectedStableProfile is null || _smallSfx is null || !_selectedStableProfile.WorkingDirectory.Equals(_smallSfx.WorkingDirectory, StringComparison.OrdinalIgnoreCase))
        {
            _selectedStableProfile = KnownRepositoryAuthorizationService.GetStableAudioModel(
                stableDefinition?.GitHubRepository ?? string.Empty,
                stableDefinition?.Arguments ?? string.Empty) switch
            {
                "small-music" => _smallMusic,
                "medium" => _medium,
                _ => _smallSfx
            };
        }
    }

    private ServiceProfile? CreateSpecialProfile(string id, string? arguments = null, bool? isHighVram = null, int? recommendedVramMiB = null, int? recommendedSystemMemoryMiB = null)
    {
        var definition = _modelCatalog.Models.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        return CreateSpecialProfile(definition, arguments, isHighVram, recommendedVramMiB, recommendedSystemMemoryMiB);
    }

    private static bool IsStableAudioDefinition(LauncherModelDefinition definition)
        => definition.Id.Equals("stable-audio-3", StringComparison.OrdinalIgnoreCase) ||
           definition.GitHubRepository.Equals("Stability-AI/stable-audio-3", StringComparison.OrdinalIgnoreCase);

    internal static bool SupportsStableAudioProfiles(LauncherModelDefinition definition)
        => IsStableAudioDefinition(definition);

    private ServiceProfile? CreateStableAudioProfile(
        LauncherModelDefinition? definition,
        string model,
        string profileName,
        bool isHighVram,
        int recommendedVramMiB,
        int recommendedSystemMemoryMiB)
    {
        var profile = CreateSpecialProfile(
            definition,
            KnownRepositoryEnvironmentService.BuildStableAudioLaunchArguments(model),
            isHighVram,
            recommendedVramMiB,
            recommendedSystemMemoryMiB);
        return profile is null ? null : profile with { Name = $"Stable Audio 3 - {profileName}" };
    }

    private ServiceProfile? CreateSpecialProfile(LauncherModelDefinition? definition, string? arguments = null, bool? isHighVram = null, int? recommendedVramMiB = null, int? recommendedSystemMemoryMiB = null)
    {
        if (definition is null)
        {
            return null;
        }
        var executable = ExpandModelValue(definition.Executable, definition.RootDirectory, definition.Port);
        if (!Path.IsPathRooted(executable) && (executable.Contains('/') || executable.Contains('\\')))
        {
            executable = Path.Combine(definition.RootDirectory, executable.Replace('/', Path.DirectorySeparatorChar));
        }
        return new ServiceProfile(
            definition.DisplayName,
            definition.Description,
            definition.RootDirectory,
            executable,
            ExpandModelValue(arguments ?? definition.Arguments, definition.RootDirectory, definition.Port),
            definition.Port,
            isHighVram ?? definition.IsHighVram,
            definition.RequiredFiles,
            recommendedVramMiB ?? definition.RecommendedVramMiB,
            recommendedSystemMemoryMiB ?? definition.RecommendedSystemMemoryMiB,
            definition.Dependencies);
    }

    private IEnumerable<LauncherModelDefinition> CustomModelDefinitions()
        => _modelCatalog.Models;

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
        var followLatest = _logAutoFollow;
        var firstVisibleCharacter = followLatest ? 0 : _log.GetCharIndexFromPosition(new Point(1, 1));
        var entries = (_logFilter switch
        {
            LauncherLogFilter.Errors => _logEntries.Where(entry => entry.IsError),
            LauncherLogFilter.CurrentService when _activeService is not null => _logEntries.Where(entry => entry.ServiceName == _activeService.Name),
            LauncherLogFilter.CurrentService => Enumerable.Empty<LauncherLogEntry>(),
            _ => _logEntries
        }).ToList();
        _log.Text = string.Concat(entries.Select(entry => $"[{entry.Timestamp:HH:mm:ss}] {entry.DisplayMessage(_useEnglish)}{Environment.NewLine}"));
        _log.SelectionStart = followLatest
            ? _log.TextLength
            : Math.Clamp(firstVisibleCharacter, 0, _log.TextLength);
        _log.ScrollToCaret();
        _logAutoFollow = followLatest;
        if (followLatest)
        {
            _unseenLogEntryCount = 0;
        }
        if (_logFollowStateLabel is not null)
        {
            var errorCount = _logEntries.Count(entry => entry.IsError);
            _logFollowStateLabel.Text = _logAutoFollow
                ? L("正在跟随最新输出", "Following live output")
                : _unseenLogEntryCount > 0
                    ? L($"有 {_unseenLogEntryCount} 条新日志", $"{_unseenLogEntryCount} new entries")
                    : L("已暂停跟随", "Follow paused");
            _logFollowStateLabel.ForeColor = errorCount > 0
                ? Color.FromArgb(244, 158, 151)
                : _logAutoFollow ? Color.FromArgb(134, 200, 187) : Color.FromArgb(245, 196, 121);
        }
        if (_logSummaryLabel is not null)
        {
            var latest = entries.LastOrDefault();
            _logSummaryLabel.Text = latest is null
                ? L("暂无运行消息", "No runtime messages")
                : $"[{latest.Timestamp:HH:mm:ss}] {latest.DisplayMessage(_useEnglish)}";
            _logSummaryLabel.ForeColor = latest?.IsError == true ? Color.FromArgb(244, 158, 151) : Color.FromArgb(166, 202, 195);
        }
    }

    private void SetLogFilter(LauncherLogFilter filter)
    {
        _logFilter = filter;
        _logAutoFollow = true;
        _unseenLogEntryCount = 0;
        RenderLog();
    }

    private bool IsLogScrolledToBottom()
    {
        if (_log.TextLength == 0 || !_log.Visible)
        {
            return true;
        }
        var lastCharacter = _log.GetCharIndexFromPosition(new Point(1, Math.Max(1, _log.ClientSize.Height - 2)));
        return lastCharacter >= _log.TextLength - 2;
    }

    private void UpdateLogFollowState()
    {
        if (IsDisposed || _log.IsDisposed)
        {
            return;
        }
        var wasFollowing = _logAutoFollow;
        _logAutoFollow = IsLogScrolledToBottom();
        if (_logAutoFollow == wasFollowing)
        {
            return;
        }
        if (_logAutoFollow)
        {
            _unseenLogEntryCount = 0;
        }
        RenderLog();
    }

    private void ToggleLogFollow()
    {
        _logAutoFollow = !_logAutoFollow;
        if (_logAutoFollow)
        {
            _unseenLogEntryCount = 0;
            RenderLog();
            return;
        }
        if (_logFollowStateLabel is not null)
        {
            _logFollowStateLabel.Text = L("已暂停跟随", "Follow paused");
            _logFollowStateLabel.ForeColor = Color.FromArgb(245, 196, 121);
        }
    }

    private void ToggleLanguage()
    {
        _useEnglish = !_useEnglish;
        _pluginCategory = "*";
        Controls.Clear();
        InitializeUi();
        RefreshStatus();
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
        var settings = CreateSettingsForDataRoot(LauncherPaths.DefaultDataDirectory);
        settings.FirstRunCompleted = true;
        return settings;
    }

    private static LauncherSettings CreateSettingsForDataRoot(string dataRoot)
    {
        var normalizedRoot = Path.GetFullPath(dataRoot);
        var plugins = Path.Combine(normalizedRoot, "plugins");
        return new LauncherSettings
        {
            SchemaVersion = 5,
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
        settings.SchemaVersion = 5;
        settings.GitHubProxyUrl = TryValidateProxyUrl(settings.GitHubProxyUrl, out _) ? settings.GitHubProxyUrl.Trim() : string.Empty;
        settings.DataRoot = MigrateRenamedPath(settings.DataRoot);
        settings.RuntimeLogHeight = Math.Clamp(settings.RuntimeLogHeight, 180, 480);
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
        => new();

    private static void SaveModelCatalog(LauncherModelCatalog catalog)
    {
        catalog.SchemaVersion = 3;
        LauncherConfigurationStore.SaveAtomic(ModelCatalogPath, catalog);
    }

    private void MigrateLegacyBuiltInCatalogEntries()
    {
        foreach (var definition in _modelCatalog.Models.Where(model => model.IsBuiltIn).ToArray())
        {
            if (!Directory.Exists(definition.RootDirectory))
            {
                _modelCatalog.Models.Remove(definition);
                continue;
            }
            definition.IsBuiltIn = false;
            definition.TrustSource = "LegacyLocal";
            definition.IsManifestTrusted = true;
        }
    }

    private static HttpClient CreateGitHubClient(string? proxyUrl = null, TimeSpan? timeout = null)
    {
        HttpMessageHandler handler = new HttpClientHandler();
        if (!string.IsNullOrWhiteSpace(proxyUrl))
        {
            if (!TryValidateProxyUrl(proxyUrl, out var proxyUri))
            {
                throw new InvalidDataException("The GitHub proxy URL is invalid.");
            }
            handler = new HttpClientHandler { Proxy = new System.Net.WebProxy(proxyUri), UseProxy = true };
        }
        var client = new HttpClient(handler) { Timeout = timeout ?? TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"BaChen-AI-Launcher/{LauncherVersion}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private void ConfigureGitHubServices(string? proxyUrl)
    {
        var previousClient = _githubClient;
        _githubClient = CreateGitHubClient(proxyUrl);
        _pluginPackageService = new PluginPackageService(_githubClient);
        _pluginDownloadService = new PluginDownloadService(_githubClient);
        _pluginCatalogService = new PluginCatalogService(_githubClient);
        _gitHubModelImportService = new GitHubModelImportService(_githubClient);
        _authorizationService = new ExternalModelAuthorizationService(_githubClient);
        _sourceUpdateService = new GitHubUpdateService(_githubClient);
        _launcherUpdateService = new LauncherSelfUpdateService(_githubClient);
        previousClient?.Dispose();
    }

    internal static bool TryValidateProxyUrl(string? value, out Uri? proxyUri)
    {
        proxyUri = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }
        return Uri.TryCreate(value.Trim(), UriKind.Absolute, out proxyUri) &&
            (proxyUri.Scheme == Uri.UriSchemeHttp || proxyUri.Scheme == Uri.UriSchemeHttps) &&
            string.IsNullOrEmpty(proxyUri.UserInfo) && !string.IsNullOrWhiteSpace(proxyUri.Host);
    }

    private async Task CheckUpdatesAsync()
    {
        if (_updateBusy)
        {
            return;
        }

        _updateBusy = true;
        SetRuntimePhase("正在检查 GitHub 源码更新", "Checking GitHub source updates");
        AppendLocalizedLog("正在检查 GitHub 源码更新……", "Checking GitHub source updates...");
        try
        {
            var checks = new List<SourceUpdateCheck>();
            foreach (var source in _updateSources)
            {
                checks.Add(await FetchUpdateCheckAsync(source));
            }

            _availableSourceUpdates.Clear();
            foreach (var check in checks.Where(check => check.UpdateAvailable))
            {
                _availableSourceUpdates[Path.GetFullPath(check.Source.DeploymentRoot)] = check;
            }
            UpdatePluginUi();

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
            if (checks.Any(check => check.UpdateAvailable))
            {
                summary += Environment.NewLine + Environment.NewLine + L("请选择有更新的插件，然后点击“启动插件”旁的“更新插件”。", "Select a plugin with an available update, then click Update plugin next to Launch plugin.");
            }
            AppendLog(summary.Replace(Environment.NewLine, " | "));
            SetRuntimePhase("更新检查完成", "Update check complete");
            MessageBox.Show(summary, L("GitHub 更新检查", "GitHub update check"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppendLocalizedLog($"检查更新失败：{ex.Message}", $"Update check failed: {ex.Message}");
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
            AppendLocalizedLog("正在验证启动器更新清单……", "Verifying launcher update manifest...");
            var check = await CheckLauncherVersionFromAllSourcesAsync();
            if (!check.IsUpdateAvailable)
            {
                var conflict = check.HasSourceConflict
                    ? L("\n不同来源仍有版本差异，已采用签名有效的最高版本。", "\nSources disagree; the highest valid signed version was selected.")
                    : string.Empty;
                MessageBox.Show(
                    L(
                        $"当前版本 {check.CurrentVersion.ToString(3)} 已是最新版本。\n远端最高版本：{check.LatestVersion.ToString(3)}\n采用来源：{check.SelectedSource}{conflict}",
                        $"Version {check.CurrentVersion.ToString(3)} is up to date.\nHighest remote version: {check.LatestVersion.ToString(3)}\nSelected source: {check.SelectedSource}{conflict}"),
                    L("启动器更新", "Launcher update"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
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
            AppendLocalizedLog($"启动器更新失败：{message}", $"Launcher update failed: {message}", null, true);
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
            var check = await CheckLauncherVersionFromAllSourcesAsync();
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
        try
        {
            ShowLauncherUpdateProgress();
            var progress = new Progress<LauncherUpdateProgress>(UpdateLauncherUpdateProgress);
            var packagePath = await _launcherUpdateService.DownloadVerifiedAsync(check.Manifest, progress);
            UpdateLauncherUpdateProgress(new LauncherUpdateProgress(LauncherUpdateProgressStage.Verifying, 1, 1));
            AppendLocalizedLog($"启动器 {check.LatestVersion.ToString(3)} 校验通过，准备重启。", $"Launcher {check.LatestVersion.ToString(3)} verified; preparing to restart.");
            LauncherSelfUpdateService.BeginApply(packagePath, check.Manifest);
            Application.Exit();
        }
        catch
        {
            throw;
        }
    }

    private void ShowLauncherUpdateProgress()
    {
        _settingsWorkspace?.ReportProgress(null, L("正在准备启动器更新", "Preparing launcher update"));
    }

    private void UpdateLauncherUpdateProgress(LauncherUpdateProgress progress)
    {
        var totalBytes = progress.TotalBytes.GetValueOrDefault();
        if (totalBytes <= 0)
        {
            _settingsWorkspace?.ReportProgress(null, L("正在准备启动器更新", "Preparing launcher update"));
            return;
        }

        var completed = Math.Clamp(progress.CompletedBytes, 0, totalBytes);
        var sourcePercent = (int)Math.Round(completed * 100D / totalBytes, MidpointRounding.AwayFromZero);
        var progressValue = progress.Stage == LauncherUpdateProgressStage.Downloading
            ? Math.Min(85, (int)Math.Round(sourcePercent * 0.85D, MidpointRounding.AwayFromZero))
            : 85 + (int)Math.Round(sourcePercent * 0.15D, MidpointRounding.AwayFromZero);
        var speed = FormatTransferSpeed(progress.BytesPerSecond);
        _settingsWorkspace?.ReportProgress(
            Math.Clamp(progressValue, 0, 100),
            progress.Stage == LauncherUpdateProgressStage.Downloading
                ? L($"正在下载启动器 {sourcePercent}% · {speed}", $"Downloading launcher {sourcePercent}% · {speed}")
                : L($"正在校验启动器 {sourcePercent}%", $"Verifying launcher {sourcePercent}%"));
    }

    internal static string FormatTransferSpeed(double? bytesPerSecond)
    {
        if (bytesPerSecond is null || bytesPerSecond <= 0 || double.IsNaN(bytesPerSecond.Value) || double.IsInfinity(bytesPerSecond.Value))
        {
            return "-- MB/s";
        }

        if (bytesPerSecond.Value >= 1024D * 1024D)
        {
            return $"{bytesPerSecond.Value / (1024D * 1024D):0.0} MB/s";
        }

        if (bytesPerSecond.Value >= 1024D)
        {
            return $"{bytesPerSecond.Value / 1024D:0.0} KB/s";
        }

        return $"{bytesPerSecond.Value:0} B/s";
    }

    private async Task<LauncherUpdateCheck> CheckLauncherVersionFromAllSourcesAsync()
    {
        var highestObserved = _settings.LauncherUpdateChannel == LauncherUpdateChannel.Stable &&
            Version.TryParse(_settings.HighestObservedStableVersion, out var parsed)
                ? parsed
                : null;
        var check = await _launcherUpdateService.CheckAsync(
            _settings.LauncherUpdateChannel,
            highestObservedVersion: highestObserved);
        var sourceSummaryChinese = string.Join(" | ", check.Sources.Select(source =>
            source.IsValid
                ? $"{source.Name}={source.Version!.ToString(3)}"
                : $"{source.Name}=失败({source.Detail})"));
        var sourceSummaryEnglish = string.Join(" | ", check.Sources.Select(source =>
            source.IsValid
                ? $"{source.Name}={source.Version!.ToString(3)}"
                : $"{source.Name}=failed({source.Detail})"));
        AppendLocalizedLog(
            $"启动器更新源：{sourceSummaryChinese}；采用 {check.SelectedSource} {check.LatestVersion.ToString(3)}",
            $"Launcher update sources: {sourceSummaryEnglish}; selected {check.SelectedSource} {check.LatestVersion.ToString(3)}");
        if (_settings.LauncherUpdateChannel == LauncherUpdateChannel.Stable &&
            (highestObserved is null || check.LatestVersion > highestObserved))
        {
            _settings.HighestObservedStableVersion = check.LatestVersion.ToString(3);
            _settings.HighestObservedStableVersionAt = DateTimeOffset.Now;
            _settings.HighestObservedStableVersionSource = check.SelectedSource;
            SaveSettings(_settings);
        }
        return check;
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
            var details = unavailable.InnerException?.Message;
            return unavailable.Channel == LauncherUpdateChannel.Stable
                ? L(
                    $"无法从任何更新源确认稳定版，请检查网络或代理后重试。\n\n来源详情：{details}",
                    $"No update source could confirm a stable release. Check the network or proxy and try again.\n\nSource details: {details}")
                : L($"目前没有可用的预览版，请稍后重试。\n\n来源详情：{details}", $"No preview release is currently available. Try again later.\n\nSource details: {details}");
        }
        if (exception is LauncherUpdateStaleException stale) return L(
            $"更新源只返回了 {stale.RemoteVersion.ToString(3)}，低于本机曾验证过的 {stale.HighestObservedVersion.ToString(3)}。这通常是缓存或发布同步延迟，当前无法确认最新版，请稍后重试。",
            $"Update sources returned {stale.RemoteVersion.ToString(3)}, older than the previously verified {stale.HighestObservedVersion.ToString(3)}. This usually indicates stale cache or release propagation; the latest version cannot be confirmed yet.");
        if (exception is TaskCanceledException) return L("网络请求超时。请在“设置”中填写本机可用的 HTTP/HTTPS 代理并测试连接。", "The request timed out. Configure and test an HTTP/HTTPS proxy in Settings.");
        if (exception is HttpRequestException { StatusCode: System.Net.HttpStatusCode.Forbidden } rateLimitError) return L(
            $"GitHub API 拒绝了匿名请求，通常是当前网络出口触发访问频率限制。启动器会优先改用 Stable 直连更新；添加模型可稍后重试或配置代理。\n\n失败详情：{rateLimitError.Message}",
            $"GitHub rejected the anonymous API request, usually because the network reached its rate limit. Stable direct updates are preferred; retry model import later or configure a proxy.\n\nDetails: {rateLimitError.Message}");
        if (exception is HttpRequestException httpError) return L(
            $"无法连接 GitHub 更新服务。请在“设置”中测试 GitHub 连接或填写代理。\n\n失败详情：{httpError.Message}\n\n手动下载：https://github.com/Bachen-beep/bachen-ai-launcher/releases/latest",
            $"GitHub update services could not be reached. Test the connection or configure a proxy in Settings.\n\nDetails: {httpError.Message}\n\nManual download: https://github.com/Bachen-beep/bachen-ai-launcher/releases/latest");
        if (exception is IOException downloadError && downloadError.Message.Contains("download failed after", StringComparison.OrdinalIgnoreCase)) return L(
            "启动器安装包下载连接中断，启动器已自动重试 5 次仍未完成。请检查代理、防火墙或网络后再次点击更新；已下载内容不会被安装。",
            "The launcher package connection ended during download. Five automatic retries did not complete the file. Check the proxy, firewall, or network and retry; incomplete data was not installed.");
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
            _availableSourceUpdates.Clear();
            foreach (var check in checks.Where(check => check.UpdateAvailable))
            {
                _availableSourceUpdates[Path.GetFullPath(check.Source.DeploymentRoot)] = check;
            }
            var count = checks.Count(check => check.UpdateAvailable || !check.HasLocalBaseline);
            _backgroundUpdateStatusChinese = count == 0 ? "源码已是最新记录" : $"{count} 个源码可检查更新";
            _backgroundUpdateStatusEnglish = count == 0 ? "Sources match tracked versions" : $"{count} source update(s) available";
            AppendLocalizedLog(_backgroundUpdateStatusChinese, _backgroundUpdateStatusEnglish);
            RefreshStatus();
        }
        catch (Exception ex)
        {
            AppendLocalizedLog($"后台更新检查跳过：{ex.Message}", $"Background update check skipped: {ex.Message}");
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
            AppendLocalizedLog("正在生成更新预览……", "Building update preview...");
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
                    AppendLocalizedLog($"正在更新 {source.DisplayName}……", $"Updating {source.DisplayName}...");
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

    private async Task UpdateSelectedPluginAsync()
    {
        if (_updateBusy)
        {
            return;
        }

        var selected = _pluginEntries.FirstOrDefault(entry => entry.Id.Equals(_selectedPluginId, StringComparison.OrdinalIgnoreCase));
        if (selected is null || !_availableSourceUpdates.TryGetValue(Path.GetFullPath(selected.Profile.WorkingDirectory), out var check))
        {
            return;
        }

        var runningPids = GetKnownServicePids();
        if (runningPids.Count > 0 && MessageBox.Show(
                L("检测到 AI 服务正在运行。更新插件前需要停止已识别的服务，是否继续？", "AI services are running. Stop recognized services before updating this plugin?"),
                L("需要停止服务", "Services must stop"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        _updateBusy = true;
        _pluginUpdateInProgress = true;
        _updatingPluginId = selected.Id;
        _latestPluginUpdateProgress = null;
        try
        {
            if (runningPids.Count > 0)
            {
                StopProcesses(runningPids);
                await Task.Delay(700);
            }
            SetServiceRuntimeState(selected.Profile, ServiceRuntimeState.Updating);
            if (_detailUpdateProgress is not null)
            {
                _detailUpdateProgress.Value = 0;
                _detailUpdateProgress.Style = ProgressBarStyle.Marquee;
                _detailUpdateProgress.Visible = true;
            }
            SetRuntimePhase($"正在更新 {check.Source.DisplayName}", $"Updating {check.Source.DisplayName}");
            var progress = new Progress<SourceUpdateProgress>(UpdateSelectedPluginProgress);
            var backupPath = await ApplySourceUpdateAsync(check, progress);
            _availableSourceUpdates.Remove(Path.GetFullPath(check.Source.DeploymentRoot));
            SetServiceRuntimeState(selected.Profile, ServiceRuntimeState.Ready);
            var result = L($"{check.Source.DisplayName} 已更新。备份：{backupPath}", $"{check.Source.DisplayName} updated. Backup: {backupPath}");
            AppendLog(result);
            MessageBox.Show(result + Environment.NewLine + Environment.NewLine + L("模型权重和本地运行环境已保留。若依赖文件有变化，请运行环境自检。", "Model weights and local runtime environments were preserved. Run environment check if dependencies changed."), L("插件更新完成", "Plugin update complete"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            SetServiceRuntimeState(selected.Profile, ServiceRuntimeState.Error);
            AppendLocalizedLog($"{check.Source.DisplayName} 更新失败：{ex.Message}", $"{check.Source.DisplayName} update failed: {ex.Message}", selected.Profile, true);
            MessageBox.Show(ex.Message, L("插件更新失败", "Plugin update failed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _updateBusy = false;
            _pluginUpdateInProgress = false;
            _updatingPluginId = null;
            _latestPluginUpdateProgress = null;
            if (_detailUpdateProgress is not null)
            {
                _detailUpdateProgress.Visible = false;
            }
            SetRuntimePhase("插件更新流程完成", "Plugin update workflow complete");
            _settingsWorkspace?.CompleteProgress(L("插件更新流程完成", "Plugin update workflow complete"));
            RefreshStatus();
        }
    }

    private void UpdateSelectedPluginProgress(SourceUpdateProgress progress)
    {
        _latestPluginUpdateProgress = progress;
        if (_detailUpdateProgress is null || !_selectedPluginId.Equals(_updatingPluginId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        var total = progress.Total.GetValueOrDefault();
        if (total <= 0)
        {
            _detailUpdateProgress.Style = ProgressBarStyle.Marquee;
            _settingsWorkspace?.ReportProgress(null, FormatPluginUpdateStatus(progress, _useEnglish));
            var unknownTotalSelected = _pluginEntries.FirstOrDefault(entry => entry.Id.Equals(_selectedPluginId, StringComparison.OrdinalIgnoreCase));
            if (_detailStatusLabel is not null && unknownTotalSelected is not null)
            {
                _detailStatusLabel.Text = FormatPluginUpdateStatus(progress, _useEnglish);
            }
            return;
        }
        var percent = (int)Math.Round(Math.Clamp(progress.Completed, 0, total) * 100D / total, MidpointRounding.AwayFromZero);
        var value = progress.Stage == SourceUpdateProgressStage.Downloading
            ? (int)Math.Round(percent * 0.65D, MidpointRounding.AwayFromZero)
            : 65 + (int)Math.Round(percent * 0.35D, MidpointRounding.AwayFromZero);
        _detailUpdateProgress.Style = ProgressBarStyle.Continuous;
        _detailUpdateProgress.Value = Math.Clamp(value, 0, 100);
        _settingsWorkspace?.ReportProgress(Math.Clamp(value, 0, 100), FormatPluginUpdateStatus(progress, _useEnglish));
        var selected = _pluginEntries.FirstOrDefault(entry => entry.Id.Equals(_selectedPluginId, StringComparison.OrdinalIgnoreCase));
        if (_detailStatusLabel is not null && selected is not null)
        {
            _detailStatusLabel.Text = FormatPluginUpdateStatus(progress, _useEnglish);
        }
    }

    internal static string FormatPluginUpdateStatus(SourceUpdateProgress progress, bool useEnglish)
    {
        var total = progress.Total.GetValueOrDefault();
        var percentText = total > 0
            ? $" {Math.Round(Math.Clamp(progress.Completed, 0, total) * 100D / total, MidpointRounding.AwayFromZero):0}%"
            : string.Empty;
        if (progress.Stage == SourceUpdateProgressStage.Installing)
        {
            return useEnglish ? $"Replacing source{percentText}" : $"正在替换源码{percentText}";
        }

        var speed = FormatTransferSpeed(progress.BytesPerSecond);
        return useEnglish
            ? $"Downloading update{percentText} · {speed}"
            : $"正在下载更新{percentText} · {speed}";
    }

    private static string UpdateStatePath(GitHubUpdateSource source) => GitHubUpdateService.UpdateStatePath(source);
    private SourceUpdateState? LoadUpdateState(GitHubUpdateSource source) => _sourceUpdateService.LoadState(source);
    private void SaveUpdateState(GitHubUpdateSource source, string commitSha) => _sourceUpdateService.SaveState(source, commitSha);
    private Task<SourceUpdateCheck> FetchUpdateCheckAsync(GitHubUpdateSource source) => _sourceUpdateService.FetchCheckAsync(source);
    private Task<string[]> GetChangedDependencyFilesAsync(GitHubUpdateSource source) => _sourceUpdateService.GetChangedDependencyFilesAsync(source);

    private async Task<string> ApplySourceUpdateAsync(SourceUpdateCheck check, IProgress<SourceUpdateProgress>? progress = null)
    {
        var source = check.Source;
        var tempRoot = Path.Combine(Path.GetTempPath(), "bachen-ai-update-" + Guid.NewGuid().ToString("N"));
        var archivePath = Path.Combine(tempRoot, "source.zip");
        var extractPath = Path.Combine(tempRoot, "extract");
        var backupStaging = Path.Combine(tempRoot, "backup");
        Directory.CreateDirectory(tempRoot);
        try
        {
            using (var response = await _githubClient.GetAsync($"https://github.com/{source.Repository}/archive/refs/heads/{source.Branch}.zip", HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                await using var input = await response.Content.ReadAsStreamAsync();
                await using var output = new FileStream(archivePath, FileMode.Create, FileAccess.Write, FileShare.None);
                var totalBytes = response.Content.Headers.ContentLength;
                progress?.Report(new SourceUpdateProgress(SourceUpdateProgressStage.Downloading, 0, totalBytes));
                var buffer = new byte[128 * 1024];
                long downloadedBytes = 0;
                var lastReportedBytes = 0L;
                var transferRate = new TransferRateTracker();
                while (true)
                {
                    var read = await input.ReadAsync(buffer);
                    if (read == 0)
                    {
                        break;
                    }
                    await output.WriteAsync(buffer.AsMemory(0, read));
                    downloadedBytes += read;
                    if (downloadedBytes - lastReportedBytes >= 256 * 1024 || totalBytes == downloadedBytes)
                    {
                        var bytesPerSecond = transferRate.Sample(downloadedBytes);
                        progress?.Report(new SourceUpdateProgress(SourceUpdateProgressStage.Downloading, downloadedBytes, totalBytes, bytesPerSecond));
                        lastReportedBytes = downloadedBytes;
                    }
                }
                if (downloadedBytes != lastReportedBytes)
                {
                    var bytesPerSecond = transferRate.Sample(downloadedBytes);
                    progress?.Report(new SourceUpdateProgress(SourceUpdateProgressStage.Downloading, downloadedBytes, totalBytes, bytesPerSecond));
                }
            }

            ZipFile.ExtractToDirectory(archivePath, extractPath);
            var extractedRoot = Directory.GetDirectories(extractPath).SingleOrDefault()
                ?? throw new InvalidDataException("The GitHub archive did not contain a source directory.");
            Directory.CreateDirectory(backupStaging);
            var sourceFiles = Directory.EnumerateFiles(extractedRoot, "*", SearchOption.AllDirectories).ToArray();
            progress?.Report(new SourceUpdateProgress(SourceUpdateProgressStage.Installing, 0, sourceFiles.Length));
            var installedFiles = 0;
            foreach (var sourceFile in sourceFiles)
            {
                var relative = Path.GetRelativePath(extractedRoot, sourceFile).Replace('\\', '/');
                if (ShouldPreserveUpdatePath(source, relative))
                {
                    installedFiles++;
                    progress?.Report(new SourceUpdateProgress(SourceUpdateProgressStage.Installing, installedFiles, sourceFiles.Length));
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
                installedFiles++;
                progress?.Report(new SourceUpdateProgress(SourceUpdateProgressStage.Installing, installedFiles, sourceFiles.Length));
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
        var profiles = _modelCatalog.Models.Select(CreateCustomProfile).ToList();
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
            : L($"GPU：{resources.GpuName}\r\nGPU 显存：{resources.GpuUsedMiB} / {resources.GpuTotalMiB} MiB", $"GPU: {resources.GpuName}\r\nGPU memory: {resources.GpuUsedMiB} / {resources.GpuTotalMiB} MiB"));
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
            ClientSize = new Size(940, 465),
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            BackColor = Theme.Card,
            Font = new Font("Microsoft YaHei UI", 10F)
        };
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 145,
            Padding = new Padding(24, 24, 24, 8),
            ColumnCount = 3,
            RowCount = 2,
            BackColor = Theme.Card
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 195));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));

        var dataRootBox = AddPathSettingRow(table, 0, L("数据根目录", "Data directory"), _settings.DataRoot, dialog);
        var proxyBox = new TextBox { Text = _settings.GitHubProxyUrl, Dock = DockStyle.Fill, Margin = new Padding(6, 8, 6, 8), PlaceholderText = "http://127.0.0.1:7890" };
        var testConnection = new Button { Text = L("测试连接", "Test"), Dock = DockStyle.Fill, Margin = new Padding(6) };
        table.Controls.Add(new Label { Text = L("GitHub 代理（可选）", "GitHub proxy (optional)"), TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 1);
        table.Controls.Add(proxyBox, 1, 1);
        table.Controls.Add(testConnection, 2, 1);
        testConnection.Click += async (_, _) =>
        {
            if (!TryValidateProxyUrl(proxyBox.Text, out _))
            {
                MessageBox.Show(L("代理地址应为 http://主机:端口 或 https://主机:端口，且不要包含账号密码。", "Use http://host:port or https://host:port without embedded credentials."), L("代理地址无效", "Invalid proxy"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            testConnection.Enabled = false;
            try
            {
                using var testClient = CreateGitHubClient(proxyBox.Text, TimeSpan.FromSeconds(15));
                using var response = await testClient.GetAsync(LauncherSelfUpdateService.DefaultManifestUri, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                MessageBox.Show(L("GitHub 更新服务连接成功。", "GitHub update service connection succeeded."), L("连接测试", "Connection test"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(L("连接失败：", "Connection failed: ") + ex.Message, L("连接测试", "Connection test"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                testConnection.Enabled = true;
            }
        };
        dialog.Controls.Add(table);

        var automaticUpdates = new CheckBox
        {
            Text = L("启动时自动检查启动器更新", "Automatically check launcher updates at startup"),
            Checked = _settings.AutomaticallyCheckLauncherUpdates,
            Location = new Point(28, 184),
            Size = new Size(520, 32),
            ForeColor = Theme.Ink,
            BackColor = Theme.Card
        };
        dialog.Controls.Add(automaticUpdates);

        var updateChannelLabel = new Label
        {
            Text = L("启动器更新通道", "Launcher update channel"),
            Location = new Point(560, 184),
            Size = new Size(180, 30),
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Theme.Ink,
            BackColor = Theme.Card
        };
        var updateChannel = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(742, 184),
            Size = new Size(170, 30)
        };
        updateChannel.Items.AddRange([L("稳定版", "Stable"), L("预览版", "Preview")]);
        updateChannel.SelectedIndex = _settings.LauncherUpdateChannel == LauncherUpdateChannel.Preview ? 1 : 0;
        dialog.Controls.Add(updateChannelLabel);
        dialog.Controls.Add(updateChannel);

        var note = new Label
        {
            AutoSize = false,
            Text = L("插件端口和启动命令在各插件的添加配置中管理。更改数据目录不会移动现有插件文件。", "Plugin ports and launch commands are managed per plugin. Changing the data directory does not move existing plugin files."),
            ForeColor = Theme.Muted,
            Location = new Point(28, 240),
            Size = new Size(860, 52)
        };
        dialog.Controls.Add(note);
        var cancel = new Button { Text = L("取消", "Cancel"), DialogResult = DialogResult.Cancel, Size = new Size(110, 38), Location = new Point(682, 397) };
        var save = new Button { Text = L("保存", "Save"), DialogResult = DialogResult.OK, Size = new Size(110, 38), Location = new Point(806, 397) };
        dialog.Controls.Add(cancel);
        dialog.Controls.Add(save);
        dialog.AcceptButton = save;
        dialog.CancelButton = cancel;

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        if (!TryValidateProxyUrl(proxyBox.Text, out _))
        {
            MessageBox.Show(L("代理地址格式无效。", "The proxy URL is invalid."), L("无法保存", "Cannot save"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var updated = new LauncherSettings
        {
            SchemaVersion = 5,
            DataRoot = dataRootBox.Text.Trim(),
            WooshRoot = _settings.WooshRoot,
            StableRoot = _settings.StableRoot,
            IndexTtsRoot = _settings.IndexTtsRoot,
            WooshPort = _settings.WooshPort,
            StablePort = _settings.StablePort,
            IndexTtsPort = _settings.IndexTtsPort
            ,AutomaticallyCheckLauncherUpdates = automaticUpdates.Checked
            ,LauncherUpdateChannel = updateChannel.SelectedIndex == 1 ? LauncherUpdateChannel.Preview : LauncherUpdateChannel.Stable
            ,GitHubProxyUrl = proxyBox.Text.Trim()
            ,SkippedLauncherVersion = _settings.SkippedLauncherVersion
            ,LauncherUpdateDeferredUntil = _settings.LauncherUpdateDeferredUntil
            ,HighestObservedStableVersion = _settings.HighestObservedStableVersion
            ,HighestObservedStableVersionAt = _settings.HighestObservedStableVersionAt
            ,HighestObservedStableVersionSource = _settings.HighestObservedStableVersionSource
            ,FirstRunCompleted = _settings.FirstRunCompleted
            ,FirstRunWizardStep = _settings.FirstRunWizardStep
            ,FirstRunSelectedPluginIds = _settings.FirstRunSelectedPluginIds
            ,RuntimeLogHeight = _settings.RuntimeLogHeight
        };
        if (updated.LauncherUpdateChannel != _settings.LauncherUpdateChannel)
        {
            updated.SkippedLauncherVersion = string.Empty;
            updated.LauncherUpdateDeferredUntil = null;
        }
        NormalizeSettings(updated);
        EnsureDataDirectories(updated);
        _settings = updated;
        SaveSettings(_settings);
        ConfigureGitHubServices(_settings.GitHubProxyUrl);
        SaveModelCatalog(_modelCatalog);
        ConfigureProfiles();
        _selectedStableProfile ??= _smallSfx;
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

    private async Task ShowFirstRunWizardAsync()
    {
        PluginCatalogIndex catalog;
        try
        {
            SetRuntimePhase("正在验证插件目录", "Verifying plugin catalog");
            catalog = await _pluginCatalogService.LoadAsync();
        }
        catch (Exception ex)
        {
            AppendLog(L("首次设置无法载入可信插件目录：", "First-time setup could not load the trusted plugin catalog: ") + ex.Message, null, true);
            MessageBox.Show(ex.Message, L("插件目录验证失败", "Plugin catalog verification failed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        using var wizard = new FirstRunWizardForm(
            _settings,
            catalog.Plugins,
            _useEnglish,
            (dataRoot, selectedPluginIds, step) =>
            {
                ApplyFirstRunState(dataRoot, selectedPluginIds, step);
                return Task.CompletedTask;
            },
            async (manifest, setupProgress, downloadProgress, cancellationToken) =>
                await InstallCatalogPluginAsync(catalog, manifest, setupProgress, downloadProgress, cancellationToken));
        var completed = wizard.ShowDialog(this) == DialogResult.OK;
        if (completed)
        {
            _settings.FirstRunCompleted = true;
            _settings.FirstRunWizardStep = 0;
            _settings.FirstRunSelectedPluginIds = [];
            SaveSettings(_settings);
            AppendLog(L("首次运行设置与环境验证已完成。", "First-time setup and environment verification completed."));
        }

        ConfigureProfiles();
        _selectedStableProfile ??= _smallSfx;
        Controls.Clear();
        InitializeUi();
        RefreshStatus();
    }

    private void ApplyFirstRunState(string dataRoot, string[] selectedPluginIds, int step)
    {
        var normalizedRoot = Path.GetFullPath(dataRoot.Trim());
        var previousRoot = Path.GetFullPath(_settings.DataRoot);
        _settings.DataRoot = normalizedRoot;
        if (!_settings.FirstRunCompleted && !normalizedRoot.Equals(previousRoot, StringComparison.OrdinalIgnoreCase))
        {
            _settings.WooshRoot = RebaseUninstalledRoot(_settings.WooshRoot, previousRoot, normalizedRoot, "woosh-dflow");
            _settings.StableRoot = RebaseUninstalledRoot(_settings.StableRoot, previousRoot, normalizedRoot, "Stable Audio 3");
            _settings.IndexTtsRoot = RebaseUninstalledRoot(_settings.IndexTtsRoot, previousRoot, normalizedRoot, "IndexTTS");
        }
        _settings.FirstRunSelectedPluginIds = selectedPluginIds;
        _settings.FirstRunWizardStep = Math.Clamp(step, 0, 5);
        EnsureDataDirectories(_settings);
        SaveSettings(_settings);
    }

    private static string RebaseUninstalledRoot(string currentPath, string previousDataRoot, string newDataRoot, string directoryName)
    {
        var normalizedCurrent = Path.GetFullPath(currentPath);
        var normalizedPrevious = Path.GetFullPath(previousDataRoot) + Path.DirectorySeparatorChar;
        return !Directory.Exists(normalizedCurrent) && normalizedCurrent.StartsWith(normalizedPrevious, StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(newDataRoot, "plugins", directoryName)
            : currentPath;
    }

    private async Task<FirstRunInstallOutcome> InstallCatalogPluginAsync(
        PluginCatalogIndex catalog,
        PluginPackageManifest manifest,
        IProgress<string> setupProgress,
        IProgress<PluginDownloadProgress> downloadProgress,
        CancellationToken cancellationToken)
    {
        if (!await EnsureExternalAuthorizationAsync(manifest))
        {
            throw new OperationCanceledException("External model authorization was not completed.", cancellationToken);
        }
        var existing = _modelCatalog.Models.FirstOrDefault(model => model.Id.Equals(manifest.Id, StringComparison.OrdinalIgnoreCase));
        var result = await _pluginPackageService.InstallAsync(
            manifest,
            null,
            _settings.DataRoot,
            setupProgress,
            downloadProgress,
            cancellationToken);
        PluginCatalogIndexVerifier.WriteInstalledCopy(catalog, result.Definition.RootDirectory);
        result.Definition.TrustSource = "SignedCatalog";
        result.Definition.SigningKeyId = catalog.Signature.KeyId;
        result.Definition.IsManifestTrusted = true;
        result.Definition.IsBuiltIn = false;
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
        if (manifest.Id.Equals("woosh-dflow", StringComparison.OrdinalIgnoreCase))
        {
            _settings.WooshRoot = result.Definition.RootDirectory;
            _settings.WooshPort = result.Definition.Port;
        }
        SaveModelCatalog(_modelCatalog);
        SaveSettings(_settings);

        var checks = PluginDependencyChecker.Check(result.Definition.Dependencies, result.Definition.RootDirectory).ToList();
        var trust = InstalledPluginTrustValidator.Verify(result.Definition, _trustedPublishers);
        checks.Add(new DependencyCheckResult("signed-catalog", trust.IsTrusted, true, trust.Message));
        return new FirstRunInstallOutcome(result.Definition, checks);
    }

    private IReadOnlyList<MaintenanceCategoryDefinition> BuildSettingsCategories()
    {
        Task Run(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        var updateAndRecovery = new MaintenanceCategoryDefinition(
            L("更新与恢复", "Updates & recovery"),
            L("检查启动器版本，并在出现问题时恢复经过验证的备份。", "Check launcher releases and restore verified backups when needed."),
            [
                new(L("检查启动器更新", "Check launcher update"),
                    L("验证多个更新源、签名和安装包完整性。", "Verify update sources, signatures, and package integrity."),
                    L("检查更新", "Check"), Color.FromArgb(31, 121, 108), CheckLauncherUpdateAsync),
                new(L("恢复上一版启动器", "Restore previous launcher"),
                    L("退出启动器并恢复最近保留的可执行文件。", "Exit and restore the most recently retained launcher executable."),
                    L("恢复", "Restore"), Theme.Coral, RollbackLauncherAsync,
                    () => LauncherSelfUpdateService.GetRollbackPath() is not null),
                new(L("恢复插件版本", "Restore plugin version"),
                    L("从安装或卸载备份中选择一个插件版本。", "Choose a plugin version from install or uninstall backups."),
                    L("选择版本", "Choose"), Color.FromArgb(170, 102, 49), () => Run(RestorePluginVersion), HasRestorablePluginVersion),
                new(L("恢复源码备份", "Restore source backup"),
                    L("恢复插件源码更新前创建的 ZIP 备份。", "Restore a ZIP backup created before a plugin source update."),
                    L("选择备份", "Choose"), Color.FromArgb(170, 102, 49), RestoreBackupAsync, HasRestorableSourceBackup)
            ]);

        var pluginManagement = new MaintenanceCategoryDefinition(
            L("插件管理", "Plugin management"),
            L("安装可信插件、更新当前插件，或导入高级 GitHub 模型。", "Install trusted plugins, update the selection, or import an advanced GitHub model."),
            [
                new(L("更新所选插件", "Update selected plugin"),
                    L("下载最新版源码，实时显示进度、百分比和网速。", "Download the latest source with live progress, percentage, and speed."),
                    L("更新插件", "Update"), Color.FromArgb(170, 102, 49), UpdateSelectedPluginAsync, CanUpdateSelectedPlugin),
                new(L("安装签名插件", "Install signed plugin"),
                    L("使用经过签名验证的 BaChen 插件清单安装。", "Install from a signature-verified BaChen plugin manifest."),
                    L("选择清单", "Choose"), Color.FromArgb(31, 121, 108), ShowInstallPluginWizardAsync),
                new(L("从 GitHub 导入（高级）", "Import from GitHub (advanced)"),
                    L("分析第三方仓库并生成本地启动配置，安装前需确认。", "Analyze a third-party repository and review its generated launch configuration."),
                    L("分析仓库", "Analyze"), Color.FromArgb(47, 83, 132), ShowAnalyzedGitHubModelDialogAsync),
                new(L("卸载所选插件", "Uninstall selected plugin"),
                    L("停止关联进程并将插件移入可恢复备份。", "Stop related processes and move the plugin into a restorable backup."),
                    L("卸载", "Uninstall"), Theme.Coral, UninstallSelectedPluginAsync,
                    () => _modelCatalog.Models.Any(model => model.Id.Equals(_selectedPluginId, StringComparison.OrdinalIgnoreCase)))
            ]);

        var diagnostics = new MaintenanceCategoryDefinition(
            L("环境与诊断", "Environment & diagnostics"),
            L("查看运行环境、日志和可用于问题排查的诊断信息。", "Inspect the runtime environment, logs, and troubleshooting information."),
            [
                new(L("运行环境自检", "Run environment check"),
                    L("检查 Python、依赖、模型文件、GPU 和插件路径。", "Check Python, dependencies, model files, GPU, and plugin paths."),
                    L("开始检查", "Run check"), Color.FromArgb(31, 121, 108), () => Run(ShowEnvironmentReport)),
                new(L("打开日志目录", "Open logs folder"),
                    L("在资源管理器中打开当前可用的日志位置。", "Open the current available log location in File Explorer."),
                    L("打开目录", "Open"), Color.FromArgb(47, 83, 132), () => Run(OpenLogsFolder)),
                new(L("导出诊断包", "Export diagnostics"),
                    L("导出脱敏后的配置、状态和运行日志。", "Export redacted configuration, status, and runtime logs."),
                    L("导出", "Export"), Color.FromArgb(31, 121, 108), () => Run(ExportDiagnostics))
            ]);

        var securityAndSetup = new MaintenanceCategoryDefinition(
            L("安全与设置", "Security & setup"),
            L("管理可信来源、登录凭据和启动器基础配置。", "Manage trusted sources, credentials, and launcher configuration."),
            [
                new(L("受信任发布者", "Trusted publishers"),
                    L("查看允许签名插件和目录的发布者密钥。", "Review publisher keys trusted for signed plugins and catalogs."),
                    L("查看", "View"), Color.FromArgb(47, 83, 132), () => Run(ShowTrustedPublishersDialog)),
                new(L("重新运行首次设置", "Run first-time setup again"),
                    L("重新选择数据目录并安装可信目录中的插件。", "Choose storage again and install plugins from the trusted catalog."),
                    L("重新设置", "Run setup"), Color.FromArgb(170, 102, 49), ShowFirstRunWizardAsync),
                new(L("删除 Hugging Face 凭据", "Delete Hugging Face credential"),
                    L("删除保存在 Windows 凭据管理器中的登录令牌。", "Delete the login token stored in Windows Credential Manager."),
                    L("删除凭据", "Delete"), Theme.Coral, () => Run(DeleteHuggingFaceCredential),
                    () => WindowsCredentialStore.Read(ExternalModelAuthorizationService.HuggingFaceCredentialTarget) is not null)
            ]);

        return [updateAndRecovery, pluginManagement, diagnostics, securityAndSetup];
    }

    private void ShowSettingsWorkspace(int category = 0)
    {
        _settingsWorkspace?.ShowCategory(category);
    }

    private void ShowPluginWorkspace()
    {
        _settingsWorkspace?.Hide();
        UpdatePluginUi();
    }

    private Control CreateInlineSettingsCard()
    {
        var card = new Panel
        {
            Height = 226,
            BackColor = Color.FromArgb(244, 249, 247),
            Tag = "settings-card"
        };
        var dataRootLabel = new SafeTextLabel
        {
            Text = L("数据目录", "Data directory"),
            Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold),
            ForeColor = Theme.Ink,
            BackColor = card.BackColor
        };
        var dataRootBox = new TextBox { Text = _settings.DataRoot, Font = new Font("Microsoft YaHei UI", 8.5F) };
        var browseButton = CreateWorkspaceButton(L("浏览", "Browse"), Color.FromArgb(47, 83, 132), 74, 30);
        browseButton.Click += (_, _) =>
        {
            using var picker = new FolderBrowserDialog
            {
                InitialDirectory = Directory.Exists(dataRootBox.Text) ? dataRootBox.Text : Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };
            if (picker.ShowDialog(this) == DialogResult.OK)
            {
                dataRootBox.Text = picker.SelectedPath;
            }
        };

        var proxyLabel = new SafeTextLabel
        {
            Text = L("GitHub 代理", "GitHub proxy"),
            Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold),
            ForeColor = Theme.Ink,
            BackColor = card.BackColor
        };
        var proxyBox = new TextBox
        {
            Text = _settings.GitHubProxyUrl,
            PlaceholderText = "http://127.0.0.1:7890",
            Font = new Font("Microsoft YaHei UI", 8.5F)
        };
        var testButton = CreateWorkspaceButton(L("测试连接", "Test"), Color.FromArgb(31, 121, 108), 90, 30);
        testButton.Click += async (_, _) =>
        {
            if (!TryValidateProxyUrl(proxyBox.Text, out _))
            {
                MessageBox.Show(L("代理地址应为 http://主机:端口 或 https://主机:端口，且不应包含账号密码。", "Use http://host:port or https://host:port without embedded credentials."), L("代理地址无效", "Invalid proxy"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            testButton.Enabled = false;
            try
            {
                using var testClient = CreateGitHubClient(proxyBox.Text, TimeSpan.FromSeconds(15));
                using var response = await testClient.GetAsync(LauncherSelfUpdateService.DefaultManifestUri, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                MessageBox.Show(L("GitHub 更新服务连接成功。", "GitHub update service connection succeeded."), L("连接测试", "Connection test"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(L("连接失败：", "Connection failed: ") + ex.Message, L("连接测试", "Connection test"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                testButton.Enabled = true;
            }
        };

        var automaticUpdates = new CheckBox
        {
            Text = L("启动时自动检查启动器更新", "Check launcher updates at startup"),
            Checked = _settings.AutomaticallyCheckLauncherUpdates,
            Font = new Font("Microsoft YaHei UI", 8.5F),
            ForeColor = Theme.Ink,
            BackColor = card.BackColor,
            AutoSize = true
        };
        var channelLabel = new SafeTextLabel
        {
            Text = L("更新通道", "Update channel"),
            Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold),
            ForeColor = Theme.Ink,
            BackColor = card.BackColor
        };
        var channelBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Microsoft YaHei UI", 8.5F)
        };
        channelBox.Items.AddRange([L("稳定版", "Stable"), L("预览版", "Preview")]);
        channelBox.SelectedIndex = _settings.LauncherUpdateChannel == LauncherUpdateChannel.Preview ? 1 : 0;
        var saveButton = CreateWorkspaceButton(L("保存设置", "Save settings"), Theme.DeepTeal, 116, 34);
        saveButton.Click += (_, _) => SaveInlineSettings(dataRootBox.Text, proxyBox.Text, automaticUpdates.Checked, channelBox.SelectedIndex == 1);

        card.Controls.AddRange([dataRootLabel, dataRootBox, browseButton, proxyLabel, proxyBox, testButton, automaticUpdates, channelLabel, channelBox, saveButton]);
        void LayoutCard()
        {
            var inputLeft = 136;
            var right = card.ClientSize.Width - 16;
            dataRootLabel.SetBounds(16, 18, 108, 26);
            browseButton.Location = new Point(right - browseButton.Width, 16);
            dataRootBox.SetBounds(inputLeft, 18, Math.Max(120, browseButton.Left - inputLeft - 8), 26);
            proxyLabel.SetBounds(16, 58, 108, 26);
            testButton.Location = new Point(right - testButton.Width, 56);
            proxyBox.SetBounds(inputLeft, 58, Math.Max(120, testButton.Left - inputLeft - 8), 26);
            automaticUpdates.Location = new Point(16, 104);
            channelLabel.SetBounds(16, 140, 108, 26);
            channelBox.SetBounds(inputLeft, 140, 148, 28);
            saveButton.Location = new Point(right - saveButton.Width, 178);
        }
        card.SizeChanged += (_, _) => LayoutCard();
        LayoutCard();
        return card;
    }

    private void SaveInlineSettings(string dataRoot, string proxyUrl, bool automaticallyCheckUpdates, bool usePreviewChannel)
    {
        if (string.IsNullOrWhiteSpace(dataRoot))
        {
            MessageBox.Show(L("数据目录不能为空。", "The data directory cannot be empty."), L("无法保存", "Cannot save"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!TryValidateProxyUrl(proxyUrl, out _))
        {
            MessageBox.Show(L("代理地址格式无效。", "The proxy URL is invalid."), L("无法保存", "Cannot save"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var channel = usePreviewChannel ? LauncherUpdateChannel.Preview : LauncherUpdateChannel.Stable;
        _settings.DataRoot = dataRoot.Trim();
        _settings.GitHubProxyUrl = proxyUrl.Trim();
        _settings.AutomaticallyCheckLauncherUpdates = automaticallyCheckUpdates;
        if (_settings.LauncherUpdateChannel != channel)
        {
            _settings.SkippedLauncherVersion = string.Empty;
            _settings.LauncherUpdateDeferredUntil = null;
        }
        _settings.LauncherUpdateChannel = channel;
        NormalizeSettings(_settings);
        EnsureDataDirectories(_settings);
        SaveSettings(_settings);
        ConfigureGitHubServices(_settings.GitHubProxyUrl);
        SaveModelCatalog(_modelCatalog);
        ConfigureProfiles();
        _selectedStableProfile ??= _smallSfx;
        Controls.Clear();
        InitializeUi();
        BeginInvoke(new Action(() =>
        {
            ShowSettingsWorkspace(3);
            RefreshStatus();
            AppendLog(L("启动器设置已保存。", "Launcher settings saved."));
        }));
    }

    private void BeginLogWindowResize(object? sender, MouseEventArgs eventArgs)
    {
        if (eventArgs.Button != MouseButtons.Left || _logHost is null || _logResizeGrip is null)
        {
            return;
        }

        _resizingLogWindow = true;
        _logResizeStartScreenY = _logResizeGrip.PointToScreen(eventArgs.Location).Y;
        _logResizeStartHeight = Height;
        _logResizeStartLogHeight = _logHost.Height;
        _logResizeGrip.Capture = true;
    }

    private void ResizeLogWindow(object? sender, MouseEventArgs eventArgs)
    {
        if (!_resizingLogWindow || _logHost is null)
        {
            return;
        }

        var delta = _logResizeStartScreenY - Cursor.Position.Y;
        var workingArea = Screen.FromControl(this).WorkingArea;
        var maximumWindowHeight = Math.Max(MinimumSize.Height, workingArea.Bottom - Top - 8);
        var targetWindowHeight = Math.Clamp(_logResizeStartHeight + delta, MinimumSize.Height, maximumWindowHeight);
        var actualDelta = targetWindowHeight - _logResizeStartHeight;
        Height = targetWindowHeight;
        _logHost.Height = Math.Max(180, _logResizeStartLogHeight + actualDelta);
    }

    private void EndLogWindowResize(object? sender, MouseEventArgs eventArgs)
    {
        if (!_resizingLogWindow)
        {
            return;
        }

        _resizingLogWindow = false;
        if (_logResizeGrip is not null)
        {
            _logResizeGrip.Capture = false;
        }
        if (_logHost is not null)
        {
            _settings.RuntimeLogHeight = Math.Clamp(_logHost.Height, 180, 480);
            SaveSettings(_settings);
        }
    }

    private bool CanUpdateSelectedPlugin()
    {
        var selected = _pluginEntries.FirstOrDefault(entry => entry.Id.Equals(_selectedPluginId, StringComparison.OrdinalIgnoreCase));
        return selected is not null && _availableSourceUpdates.ContainsKey(Path.GetFullPath(selected.Profile.WorkingDirectory));
    }

    private bool HasRestorableSourceBackup()
        => _updateSources.Any(source =>
            Directory.Exists(Path.Combine(source.DeploymentRoot, "launcher-update-backups")) &&
            Directory.EnumerateFiles(Path.Combine(source.DeploymentRoot, "launcher-update-backups"), "*.zip").Any());

    private bool HasRestorablePluginVersion()
    {
        var backupRoots = new[]
        {
            Path.Combine(_settings.DataRoot, "backups", "plugin-installs"),
            Path.Combine(_settings.DataRoot, "backups", "uninstalled-plugins")
        };
        return backupRoots.Where(Directory.Exists)
            .SelectMany(Directory.EnumerateDirectories)
            .Any(path => File.Exists(Path.Combine(path, ".bachen-plugin-definition.json")));
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
        if (!await EnsureExternalAuthorizationAsync(manifest))
        {
            return;
        }

        var preflight = PluginInstallPreflightService.Assess(manifest, _settings.DataRoot);
        if (!preflight.CanInstall)
        {
            MessageBox.Show(string.Join(Environment.NewLine, preflight.Issues.Select(issue => issue.Message)), L("安装预检失败", "Installation preflight failed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        if (preflight.Issues.Count > 0 && MessageBox.Show(
                string.Join(Environment.NewLine, preflight.Issues.Select(issue => issue.Message)) + Environment.NewLine + Environment.NewLine + L("仍然继续安装吗？", "Continue with installation?"),
                L("安装预检警告", "Installation preflight warning"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
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

        if (packagePath is null)
        {
            using var downloadDialog = new PluginDownloadDialog(_pluginDownloadService, manifest, _settings.DataRoot, _useEnglish);
            if (downloadDialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(downloadDialog.PackagePath))
            {
                return;
            }
            packagePath = downloadDialog.PackagePath;
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
            var setupProgress = new Progress<string>(message =>
            {
                SetRuntimePhase(message, message);
                AppendLog(message);
            });
            var result = await _pluginPackageService.InstallAsync(manifest, packagePath, _settings.DataRoot, setupProgress);
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

    private sealed record ExternalAuthorizationPromptResult(bool IsAuthorized, string Token);

    private async Task<bool> EnsureExternalAuthorizationAsync(PluginPackageManifest manifest)
        => (await RequestExternalAuthorizationAsync(manifest, trySavedCredentialFirst: false)).IsAuthorized;

    private Task<ExternalAuthorizationPromptResult> EnsureLaunchAuthorizationAsync(PluginPackageManifest manifest)
        => RequestExternalAuthorizationAsync(manifest, trySavedCredentialFirst: true);

    private async Task<ExternalAuthorizationPromptResult> RequestExternalAuthorizationAsync(
        PluginPackageManifest manifest,
        bool trySavedCredentialFirst)
    {
        if (!manifest.RequiresExternalAuthorization)
        {
            return new ExternalAuthorizationPromptResult(true, string.Empty);
        }
        var target = ExternalModelAuthorizationService.HuggingFaceCredentialTarget;
        var savedToken = WindowsCredentialStore.Read(target) ?? string.Empty;
        if (trySavedCredentialFirst && !string.IsNullOrWhiteSpace(savedToken))
        {
            SetRuntimePhase("正在验证 Hugging Face 权限", "Verifying Hugging Face access");
            var savedResult = await _authorizationService.VerifyAsync(manifest, savedToken);
            if (savedResult.IsAuthorized)
            {
                AppendLog(L("Hugging Face 身份与模型访问权限验证通过。", "Hugging Face identity and model access verified."));
                return new ExternalAuthorizationPromptResult(true, savedToken.Trim());
            }
        }
        while (true)
        {
            using var dialog = new Form
            {
                Text = L("外部模型授权", "External model authorization"),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                ClientSize = new Size(720, 330),
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false,
                BackColor = Theme.Card,
                Font = new Font("Microsoft YaHei UI", 10F)
            };
            var message = new Label
            {
                Text = L(
                    $"{manifest.DisplayName} 使用需要 Hugging Face 账户授权的模型。请自行注册、登录并在官方页面接受条款；启动器不会替你提交授权。",
                    $"{manifest.DisplayName} uses a model that requires Hugging Face account authorization. Register, sign in, and accept the upstream terms yourself; the launcher will not submit authorization for you."),
                Location = new Point(26, 22),
                Size = new Size(668, 78),
                ForeColor = Theme.Ink
            };
            var open = new LinkLabel { Text = L("打开官方授权页面", "Open official authorization page"), Location = new Point(26, 104), Size = new Size(300, 28), LinkColor = Theme.MidTeal };
            open.Click += (_, _) => Process.Start(new ProcessStartInfo(manifest.AuthorizationUrl) { UseShellExecute = true });
            var tokenLabel = new Label { Text = L("只读访问令牌", "Read-only access token"), Location = new Point(26, 148), Size = new Size(150, 28), ForeColor = Theme.Ink };
            var token = new TextBox { Location = new Point(180, 144), Size = new Size(514, 30), UseSystemPasswordChar = true, Text = savedToken };
            var remember = new CheckBox { Text = L("保存到 Windows 凭据管理器", "Save in Windows Credential Manager"), Location = new Point(180, 188), Size = new Size(360, 30), Checked = !string.IsNullOrWhiteSpace(savedToken), BackColor = Theme.Card };
            var remove = new Button { Text = L("删除已保存令牌", "Delete saved token"), Location = new Point(26, 252), Size = new Size(160, 40) };
            remove.Click += (_, _) =>
            {
                WindowsCredentialStore.Delete(target);
                savedToken = string.Empty;
                token.Clear();
                remember.Checked = false;
            };
            var cancel = new Button { Text = L("取消", "Cancel"), DialogResult = DialogResult.Cancel, Location = new Point(466, 252), Size = new Size(104, 40) };
            var verify = new Button { Text = L("验证权限", "Verify access"), DialogResult = DialogResult.OK, Location = new Point(582, 252), Size = new Size(112, 40) };
            dialog.Controls.AddRange([message, open, tokenLabel, token, remember, remove, cancel, verify]);
            dialog.AcceptButton = verify;
            dialog.CancelButton = cancel;
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return new ExternalAuthorizationPromptResult(false, string.Empty);
            }
            SetRuntimePhase("正在验证 Hugging Face 权限", "Verifying Hugging Face access");
            var result = await _authorizationService.VerifyAsync(manifest, token.Text);
            if (result.IsAuthorized)
            {
                if (remember.Checked)
                {
                    WindowsCredentialStore.Save(target, "HuggingFace", token.Text.Trim());
                }
                else
                {
                    WindowsCredentialStore.Delete(target);
                }
                AppendLog(L("Hugging Face 身份与模型访问权限验证通过。", "Hugging Face identity and model access verified."));
                return new ExternalAuthorizationPromptResult(true, token.Text.Trim());
            }
            MessageBox.Show(result.Message, L("授权验证失败", "Authorization verification failed"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            savedToken = token.Text;
        }
    }

    private void DeleteHuggingFaceCredential()
    {
        if (MessageBox.Show(
                L("删除保存在 Windows 凭据管理器中的 Hugging Face 令牌？", "Delete the Hugging Face token stored in Windows Credential Manager?"),
                L("删除登录凭据", "Delete credential"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }
        WindowsCredentialStore.Delete(ExternalModelAuthorizationService.HuggingFaceCredentialTarget);
        AppendLog(L("已删除 Hugging Face 登录凭据。", "Hugging Face credential deleted."));
        MessageBox.Show(L("登录凭据已删除。", "The credential was deleted."), L("删除完成", "Credential deleted"), MessageBoxButtons.OK, MessageBoxIcon.Information);
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

    private async Task UninstallSelectedPluginAsync()
    {
        var definition = _modelCatalog.Models.FirstOrDefault(model => model.Id.Equals(_selectedPluginId, StringComparison.OrdinalIgnoreCase));
        if (definition is null)
        {
            MessageBox.Show(L("请先选择一个已安装插件。", "Select an installed plugin first."), L("无法卸载", "Cannot uninstall"), MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            var pluginProcesses = PluginProcessService.FindProcessesByPluginRoots([definition.RootDirectory]);
            if (pluginProcesses.Count > 0)
            {
                AppendLog(L(
                    $"卸载前正在停止插件残留进程：{string.Join(", ", pluginProcesses)}",
                    $"Stopping remaining plugin processes before uninstall: {string.Join(", ", pluginProcesses)}"));
                var stopped = PluginProcessService.Stop(pluginProcesses);
                if (stopped.Failures.Count > 0)
                {
                    throw new InvalidOperationException(string.Join(Environment.NewLine, stopped.Failures.Select(failure => $"PID {failure.ProcessId}: {failure.Message}")));
                }
                await Task.Delay(500);
                var remaining = PluginProcessService.FindProcessesByPluginRoots([definition.RootDirectory]);
                if (remaining.Count > 0)
                {
                    throw new InvalidOperationException($"Plugin processes are still running: {string.Join(", ", remaining)}");
                }
            }
            if (_activeService is not null && _activeService.WorkingDirectory.Equals(definition.RootDirectory, StringComparison.OrdinalIgnoreCase))
            {
                _activeService = null;
                _activeProcess = null;
                _openButton.Enabled = false;
            }
            var result = await _pluginPackageService.UninstallAsync(definition, _settings.DataRoot);
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

    private async Task ShowAnalyzedGitHubModelDialogAsync()
    {
        using var dialog = new Form
        {
            Text = L("从 GitHub 添加模型", "Add model from GitHub"),
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(820, 400),
            MinimumSize = new Size(740, 380),
            BackColor = Theme.Card,
            Font = new Font("Microsoft YaHei UI", 10F),
            ShowInTaskbar = false,
            AutoScaleMode = AutoScaleMode.Dpi
        };
        var title = new Label
        {
            Text = L("添加一个 GitHub AI 项目", "Add a GitHub AI project"),
            Location = new Point(28, 24),
            AutoSize = true,
            Font = new Font(dialog.Font, FontStyle.Bold),
            ForeColor = Theme.DeepTeal
        };
        var subtitle = new Label
        {
            Text = L("只需提供仓库和安装目录。下载后会分析项目，并请求一次配置确认。", "Provide the repository and install directory. The launcher analyzes it and asks for one confirmation."),
            Location = new Point(28, 56),
            Size = new Size(700, 48),
            ForeColor = Theme.Muted
        };
        var repositoryLabel = new Label { Text = L("GitHub 仓库", "GitHub repository"), Location = new Point(28, 118), Size = new Size(150, 28), TextAlign = ContentAlignment.MiddleLeft };
        var repository = new TextBox { Location = new Point(184, 118), Size = new Size(602, 30), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, PlaceholderText = "https://github.com/owner/repository.git" };
        var installLabel = new Label { Text = L("安装目录", "Install directory"), Location = new Point(28, 166), Size = new Size(150, 28), TextAlign = ContentAlignment.MiddleLeft };
        var installDirectory = new TextBox { Location = new Point(184, 166), Size = new Size(498, 52), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, Multiline = true, WordWrap = true };
        var browse = new Button { Text = L("浏览", "Browse"), Location = new Point(690, 164), Size = new Size(96, 38), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        var pathWasChosen = false;
        repository.TextChanged += (_, _) =>
        {
            if (pathWasChosen || !GitHubModelImportService.TryNormalizeRepository(repository.Text, out var normalized)) return;
            installDirectory.Text = Path.Combine(_settings.DataRoot, "plugins", normalized.Replace('/', '-').ToLowerInvariant());
        };
        browse.Click += (_, _) =>
        {
            using var picker = new FolderBrowserDialog
            {
                Description = L("选择插件安装目录", "Choose the plugin install directory"),
                UseDescriptionForTitle = true,
                SelectedPath = Directory.Exists(installDirectory.Text) ? installDirectory.Text : Path.GetDirectoryName(installDirectory.Text) ?? _settings.DataRoot
            };
            if (picker.ShowDialog(dialog) != DialogResult.OK) return;
            pathWasChosen = true;
            installDirectory.Text = picker.SelectedPath;
        };
        var status = new Label { Location = new Point(28, 238), Size = new Size(758, 66), ForeColor = Theme.Muted };
        var cancel = new Button { Text = L("取消", "Cancel"), DialogResult = DialogResult.Cancel, Location = new Point(564, 334), Size = new Size(104, 38), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
        var analyze = new Button { Text = L("分析仓库", "Analyze repository"), Location = new Point(676, 334), Size = new Size(110, 38), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
        dialog.Controls.AddRange([title, subtitle, repositoryLabel, repository, installLabel, installDirectory, browse, status, cancel, analyze]);
        dialog.CancelButton = cancel;
        dialog.AcceptButton = analyze;

        analyze.Click += async (_, _) =>
        {
            if (!GitHubModelImportService.TryNormalizeRepository(repository.Text, out var normalizedRepository))
            {
                MessageBox.Show(L("请输入有效的 GitHub 仓库地址。", "Enter a valid GitHub repository URL."), L("仓库地址无效", "Invalid repository"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(installDirectory.Text))
            {
                MessageBox.Show(L("请选择安装目录。", "Choose an install directory."), L("安装目录缺失", "Install directory missing"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var id = normalizedRepository.Replace('/', '-').ToLowerInvariant();
            if (_modelCatalog.Models.Any(model => model.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(L("这个 GitHub 仓库已经添加。", "This GitHub repository has already been added."), L("重复插件", "Duplicate plugin"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            analyze.Enabled = false;
            cancel.Enabled = false;
            browse.Enabled = false;
            repository.Enabled = false;
            installDirectory.Enabled = false;
            try
            {
                var importProgress = new Progress<string>(message =>
                {
                    status.Text = message;
                    AppendLog(message);
                });
                var imported = await _gitHubModelImportService.ImportAsync(
                    normalizedRepository,
                    string.Empty,
                    _settings.DataRoot,
                    installDirectory.Text,
                    importProgress);
                var hasNvidiaGpu = SystemResourceProbe.ReadPrimaryGpu() is not null;
                status.Text = L("正在分析仓库结构和启动方式……", "Analyzing repository structure and launch behavior...");
                var analysis = GitHubRepositoryAnalyzer.Analyze(normalizedRepository, imported.RootDirectory, hasNvidiaGpu);
                var usedPorts = _modelCatalog.Models.Select(model => model.Port).ToHashSet();
                var port = Enumerable.Range(7860, 1000).First(candidate => !usedPorts.Contains(candidate));
                using var confirmation = new RepositoryAnalysisConfirmationForm(
                    analysis,
                    normalizedRepository,
                    $"{imported.Branch} / {imported.CommitSha[..12]}",
                    imported.RootDirectory,
                    port,
                    _useEnglish);
                if (confirmation.ShowDialog(dialog) != DialogResult.OK)
                {
                    status.Text = L("已取消安装；下载的源码可在下次分析时复用。", "Installation canceled; the downloaded source can be reused next time.");
                    return;
                }

                var selectedLaunch = confirmation.SelectedLaunchOption;
                await KnownRepositoryAssetService.EnsureAssetsAsync(
                    normalizedRepository,
                    selectedLaunch.Arguments,
                    imported.RootDirectory,
                    _settings.DataRoot,
                    _githubClient,
                    importProgress);
                status.Text = L("正在创建运行环境并安装依赖……", "Creating the runtime environment and installing dependencies...");
                await PythonEnvironmentService.EnsureRepositoryAsync(analysis, imported.RootDirectory, _settings.DataRoot, _githubClient, importProgress);
                var definition = new LauncherModelDefinition
                {
                    Id = id,
                    DisplayName = selectedLaunch.DisplayName.Equals(analysis.DisplayName, StringComparison.OrdinalIgnoreCase)
                        ? analysis.DisplayName
                        : $"{analysis.DisplayName} - {selectedLaunch.DisplayName}",
                    Description = analysis.Description,
                    Category = analysis.Category,
                    RootDirectory = imported.RootDirectory,
                    Executable = analysis.Executable,
                    Arguments = selectedLaunch.Arguments,
                    Runtime = analysis.Runtime,
                    RuntimeVersion = analysis.RuntimeVersion,
                    Port = port,
                    RecommendedVramMiB = analysis.RecommendedVramMiB,
                    RecommendedSystemMemoryMiB = analysis.RecommendedSystemMemoryMiB,
                    IsHighVram = analysis.IsHighVram,
                    RequiredFiles = new[] { selectedLaunch.EntryScript }
                        .Concat(KnownRepositoryAssetService.GetRequiredFiles(normalizedRepository, selectedLaunch.Arguments))
                        .ToArray(),
                    Dependencies = analysis.Dependencies,
                    GitHubRepository = normalizedRepository,
                    GitHubBranch = imported.Branch,
                    InstalledVersion = imported.CommitSha[..12],
                    Publisher = normalizedRepository.Split('/')[0],
                    TrustSource = "GitHubUserImport",
                    PreservedPaths = [".venv", "models", "checkpoints", "outputs", "logs"]
                };
                var missing = GetMissingRequirements(CreateCustomProfile(definition));
                if (missing.Count > 0)
                {
                    throw new InvalidOperationException(L("自动配置完成，但仍缺少：\n", "Automatic configuration completed, but these items are missing:\n") + string.Join(Environment.NewLine, missing));
                }
                _modelCatalog.Models.Add(definition);
                SaveModelCatalog(_modelCatalog);
                ConfigureProfiles();
                Controls.Clear();
                InitializeUi();
                RefreshStatus();
                AppendLog(L($"已自动配置 GitHub 模型：{definition.DisplayName} ({definition.InstalledVersion})", $"GitHub model configured automatically: {definition.DisplayName} ({definition.InstalledVersion})"));
                dialog.DialogResult = DialogResult.OK;
                dialog.Close();
            }
            catch (Exception ex)
            {
                status.Text = ex.Message;
                AppendLog(L("GitHub 模型分析或安装失败：", "GitHub model analysis or installation failed: ") + ex.Message, null, true);
                MessageBox.Show(ex.Message, L("分析或安装失败", "Analysis or installation failed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (!dialog.IsDisposed)
                {
                    analyze.Enabled = true;
                    cancel.Enabled = true;
                    browse.Enabled = true;
                    repository.Enabled = true;
                    installDirectory.Enabled = true;
                }
            }
        };
        dialog.ShowDialog(this);
    }

    private async Task ShowAddModelDialogAsync()
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
            RowCount = 16,
            AutoScroll = true,
            BackColor = Theme.Card
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));

        var name = AddTextRow(table, 0, L("模型名称 *", "Model name *"), string.Empty);
        var description = AddTextRow(table, 1, L("说明", "Description"), string.Empty);
        var category = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown, Margin = new Padding(6) };
        category.Items.AddRange([
            "Experimental", "Image generation", "Video generation", "LLM / Chat", "Vision", "Coding",
            "3D generation", "Sound design", "Audio generation", "Music", "TTS", "Voice", "Utilities", "Other"]);
        category.Text = "Experimental";
        table.Controls.Add(new Label { Text = L("分类", "Category"), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
        table.Controls.Add(category, 1, 2);

        var root = AddTextRow(table, 3, L("托管安装目录", "Managed install root"), Path.Combine(_settings.DataRoot, "plugins"));
        root.ReadOnly = true;
        var rootBrowse = new Button { Text = L("浏览…", "Browse..."), Dock = DockStyle.Fill, Margin = new Padding(6) };
        rootBrowse.Enabled = false;
        rootBrowse.Text = L("自动", "Automatic");
        table.Controls.Add(rootBrowse, 2, 3);

        var executable = AddTextRow(table, 4, L("启动程序 *", "Executable *"), ".venv/Scripts/python.exe");
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
        var usedPorts = _modelCatalog.Models.Select(model => model.Port).ToHashSet();
        var suggestedPort = Enumerable.Range(7860, 1000).First(candidate => !usedPorts.Contains(candidate));
        var port = new NumericUpDown { Minimum = 1024, Maximum = 65535, Value = suggestedPort, Width = 180, Margin = new Padding(6) };
        table.Controls.Add(new Label { Text = L("WebUI 端口", "WebUI port"), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 6);
        table.Controls.Add(port, 1, 6);
        var vram = new NumericUpDown { Minimum = 0, Maximum = 32, DecimalPlaces = 1, Increment = 0.5M, Value = 4, Width = 180, Margin = new Padding(6) };
        table.Controls.Add(new Label { Text = L("建议显存 (GB)", "Recommended VRAM (GB)"), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 7);
        table.Controls.Add(vram, 1, 7);
        var highVram = new CheckBox { Text = L("高显存模型，启动前警告", "High VRAM warning before launch"), AutoSize = true, Margin = new Padding(6, 9, 6, 6) };
        table.Controls.Add(highVram, 2, 7);
        var required = AddTextRow(table, 8, L("必需文件", "Required files"), string.Empty);
        var repository = AddTextRow(table, 9, L("GitHub 仓库或链接 *", "GitHub repository or URL *"), string.Empty);
        repository.PlaceholderText = "https://github.com/owner/repository";
        repository.TextChanged += (_, _) =>
        {
            if (!GitHubModelImportService.TryNormalizeRepository(repository.Text, out var normalizedRepository))
            {
                root.Text = Path.Combine(_settings.DataRoot, "plugins", "owner-repository");
                return;
            }
            var safeName = normalizedRepository.Replace('/', '-').ToLowerInvariant();
            root.Text = Path.Combine(_settings.DataRoot, "plugins", string.IsNullOrWhiteSpace(safeName) ? "owner-repository" : safeName);
            if (string.IsNullOrWhiteSpace(name.Text))
            {
                name.Text = normalizedRepository.Split('/')[1];
            }
        };
        var branch = AddTextRow(table, 10, L("更新分支（留空自动）", "Update branch (blank = default)"), string.Empty);
        var version = AddTextRow(table, 11, L("安装版本", "Installed version"), L("自动使用 commit", "Pinned commit automatically"));
        version.ReadOnly = true;
        var dependencies = AddTextRow(table, 12, L("依赖声明", "Dependencies"), string.Empty);
        var systemMemory = new NumericUpDown { Minimum = 0, Maximum = 256, DecimalPlaces = 1, Increment = 1, Value = 8, Width = 180, Margin = new Padding(6) };
        table.Controls.Add(new Label { Text = L("建议内存 (GB)", "Recommended RAM (GB)"), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 13);
        table.Controls.Add(systemMemory, 1, 13);
        var requirementsFile = AddTextRow(table, 14, L("Python 依赖文件", "Python requirements"), "requirements.txt");
        var setupPython = new CheckBox
        {
            Text = L("安装托管 Python，并安装 requirements.txt 或 pyproject.toml", "Install managed Python and requirements.txt or pyproject.toml"),
            Checked = true,
            AutoSize = true,
            Margin = new Padding(6, 9, 6, 6)
        };
        table.Controls.Add(new Label { Text = L("自动配置环境", "Environment setup"), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 15);
        table.Controls.Add(setupPython, 1, 15);
        dialog.Controls.Add(table);

        var hint = new Label
        {
            Text = L("可粘贴完整 GitHub 链接或 owner/repository。分支留空会自动读取默认分支；常见 Python 入口脚本会自动识别。", "Paste a full GitHub URL or owner/repository. Leave branch blank to use the default branch; common Python entry scripts are detected automatically."),
            Location = new Point(26, 602),
            Size = new Size(650, 48),
            ForeColor = Theme.Muted
        };
        dialog.Controls.Add(hint);
        var cancel = new Button { Text = L("取消", "Cancel"), DialogResult = DialogResult.Cancel, Location = new Point(674, 620), Size = new Size(104, 38), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
        var add = new Button { Text = L("下载并添加", "Download and add"), Location = new Point(766, 620), Size = new Size(128, 38), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
        add.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(name.Text) || string.IsNullOrWhiteSpace(repository.Text) || string.IsNullOrWhiteSpace(executable.Text))
            {
                MessageBox.Show(L("请填写模型名称、GitHub 仓库和启动程序。", "Enter the model name, GitHub repository, and executable."), L("信息不完整", "Information incomplete"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!GitHubModelImportService.TryNormalizeRepository(repository.Text, out var normalizedRepository))
            {
                MessageBox.Show(L("请输入 owner/repository、https://github.com/owner/repository 或 GitHub .git 地址。", "Enter owner/repository, https://github.com/owner/repository, or a GitHub .git URL."), L("仓库地址无效", "Invalid repository"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (Path.IsPathRooted(executable.Text.Trim()) || executable.Text.Contains("..", StringComparison.Ordinal))
            {
                MessageBox.Show(L("启动程序必须是下载目录内的安全相对路径。", "Executable must be a safe path relative to the downloaded repository."), L("启动路径无效", "Invalid launch path"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var configuredPorts = _modelCatalog.Models.Select(definition => definition.Port);
            if (configuredPorts.Contains((int)port.Value))
            {
                MessageBox.Show(L("该端口已经由现有模型配置使用。请为新模型分配一个不同端口。", "That port is already assigned to an existing model configuration. Choose a different port."), L("端口冲突", "Port conflict"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var id = normalizedRepository.Replace('/', '-').ToLowerInvariant();
            if (_modelCatalog.Models.Any(model => model.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(L("这个 GitHub 仓库已经添加。", "This GitHub repository has already been added."), L("重复插件", "Duplicate plugin"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            add.Enabled = false;
            cancel.Enabled = false;
            try
            {
                var importProgress = new Progress<string>(message =>
                {
                    hint.Text = message;
                    AppendLog(message);
                });
                var imported = await _gitHubModelImportService.ImportAsync(
                    normalizedRepository,
                    branch.Text,
                    _settings.DataRoot,
                    progress: importProgress);
                root.Text = imported.RootDirectory;

                var executableValue = executable.Text.Trim().Replace('\\', '/');
                var argumentsValue = arguments.Text.Trim();
                if (executableValue.EndsWith("python.exe", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(argumentsValue))
                {
                    argumentsValue = DetectPythonEntryPoint(imported.RootDirectory) ?? throw new InvalidOperationException(L(
                        "仓库已下载，但无法自动识别启动入口。请查看仓库 README，并在“启动参数”中填写入口脚本，例如 app.py。",
                        "The repository was downloaded, but no launch entry point was detected. Check its README and enter the script in Launch arguments, for example app.py."));
                    arguments.Text = argumentsValue;
                }

                if (setupPython.Checked)
                {
                    var requirementsRelative = requirementsFile.Text.Trim().Replace('\\', '/');
                    var hasRequirements = !string.IsNullOrWhiteSpace(requirementsRelative) && File.Exists(Path.Combine(imported.RootDirectory, requirementsRelative.Replace('/', Path.DirectorySeparatorChar)));
                    var hasPyProject = File.Exists(Path.Combine(imported.RootDirectory, "pyproject.toml"));
                    var runtimeConstraint = GitHubRepositoryAnalyzer.ReadPythonConstraint(imported.RootDirectory);
                    var selectedRuntime = ManagedPythonRuntimeService.SelectForConstraint(runtimeConstraint);
                    ((IProgress<string>)importProgress).Report(L(
                        $"项目要求 Python {runtimeConstraint}；已选择托管 Python {selectedRuntime.Version}",
                        $"Project requires Python {runtimeConstraint}; selected managed Python {selectedRuntime.Version}"));
                    var environmentManifest = new PluginPackageManifest
                    {
                        Runtime = "python",
                        RuntimeVersion = runtimeConstraint,
                        CreateVirtualEnvironment = true,
                        VirtualEnvironmentPath = ".venv",
                        RequirementsFile = hasRequirements ? requirementsRelative : string.Empty,
                        ManagedRuntimeId = selectedRuntime.Id,
                        PythonInstallArguments = hasPyProject ? ["-m", "pip", "install", "--disable-pip-version-check", "-e", "."] : []
                    };
                    await PythonEnvironmentService.EnsureAsync(environmentManifest, imported.RootDirectory, _settings.DataRoot, _githubClient, importProgress);
                }

                var declaredDependencies = dependencies.Text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                if (setupPython.Checked && !declaredDependencies.Any(item => item.StartsWith("python", StringComparison.OrdinalIgnoreCase)))
                {
                    declaredDependencies.Insert(0, $"python{GitHubRepositoryAnalyzer.ReadPythonConstraint(imported.RootDirectory)}");
                }
                var definition = new LauncherModelDefinition
                {
                    Id = id,
                    DisplayName = name.Text.Trim(),
                    Description = description.Text.Trim(),
                    Category = string.IsNullOrWhiteSpace(category.Text) ? "Experimental" : category.Text.Trim(),
                    RootDirectory = imported.RootDirectory,
                    Executable = executableValue,
                    Arguments = argumentsValue,
                    Runtime = setupPython.Checked ? "python" : "custom",
                    RuntimeVersion = setupPython.Checked ? GitHubRepositoryAnalyzer.ReadPythonConstraint(imported.RootDirectory) : string.Empty,
                    Port = (int)port.Value,
                    RecommendedVramMiB = (int)(vram.Value * 1024M),
                    RecommendedSystemMemoryMiB = (int)(systemMemory.Value * 1024M),
                    IsHighVram = highVram.Checked,
                    RequiredFiles = required.Text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(item => item.Replace('\\', '/')).ToArray(),
                    Dependencies = declaredDependencies.ToArray(),
                    GitHubRepository = normalizedRepository,
                    GitHubBranch = imported.Branch,
                    InstalledVersion = imported.CommitSha[..12],
                    Publisher = normalizedRepository.Split('/')[0],
                    TrustSource = "GitHubUserImport",
                    PreservedPaths = [".venv", "models", "checkpoints", "outputs", "logs"]
                };
                var missing = GetMissingRequirements(CreateCustomProfile(definition));
                if (missing.Count > 0)
                {
                    throw new InvalidOperationException(L("下载完成，但启动配置仍缺少：\n", "Download completed, but the launch configuration is missing:\n") + string.Join(Environment.NewLine, missing));
                }
                _modelCatalog.Models.Add(definition);
                SaveModelCatalog(_modelCatalog);
                ConfigureProfiles();
                Controls.Clear();
                InitializeUi();
                RefreshStatus();
                AppendLog(L($"已从 GitHub 安装模型：{definition.DisplayName} ({definition.InstalledVersion})", $"Model installed from GitHub: {definition.DisplayName} ({definition.InstalledVersion})"));
                dialog.DialogResult = DialogResult.OK;
                dialog.Close();
            }
            catch (Exception ex)
            {
                hint.Text = ex.Message;
                AppendLog(L("GitHub 模型安装失败：", "GitHub model installation failed: ") + ex.Message, null, true);
                MessageBox.Show(ex.Message, L("安装失败", "Installation failed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                add.Enabled = true;
                cancel.Enabled = true;
            }
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

    internal static string? DetectPythonEntryPoint(string repositoryRoot)
    {
        var candidates = new[]
        {
            "gradio_app.py", "app.py", "webui.py", "launch.py", "demo.py", "main.py",
            "gradio_demo.py", "infer_gradio.py"
        };
        return candidates.FirstOrDefault(candidate => File.Exists(Path.Combine(repositoryRoot, candidate)));
    }

    private void InitializeUi()
    {
        Text = "BaChen AI Launcher";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1280, 900);
        Size = new Size(1440, 1020);
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

        var gpuPanel = new Panel { Size = new Size(400, 54), BackColor = Theme.DeepTeal };
        _gpuNameLabel = CreateText(L("正在检测 GPU", "Detecting GPU"), new Rectangle(0, 2, 226, 21), 8F, Color.FromArgb(166, 221, 210), FontStyle.Bold);
        gpuPanel.Controls.Add(_gpuNameLabel);
        _gpuSummaryLabel = CreateText(L("正在读取显存", "Reading GPU memory"), new Rectangle(230, 2, 168, 21), 8F, Color.White, FontStyle.Bold, ContentAlignment.MiddleRight);
        gpuPanel.Controls.Add(_gpuSummaryLabel);
        _gpuMeter = new GpuMeter { Location = new Point(0, 31), Size = new Size(268, 9) };
        gpuPanel.Controls.Add(_gpuMeter);
        header.Controls.Add(gpuPanel);

        var languageButton = CreateActionButton(_useEnglish ? "中文" : "EN", Color.FromArgb(53, 127, 118), 74);
        languageButton.Height = 36;
        languageButton.Click += (_, _) => ToggleLanguage();
        header.Controls.Add(languageButton);

        void LayoutHeader()
        {
            languageButton.Left = header.ClientSize.Width - languageButton.Width - 24;
            languageButton.Top = 23;
            gpuPanel.Left = languageButton.Left - gpuPanel.Width - 24;
            gpuPanel.Top = 15;
            _phaseLabel.Width = Math.Max(230, gpuPanel.Left - _phaseLabel.Left - 24);
        }
        header.SizeChanged += (_, _) => LayoutHeader();

        _logHost = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = Math.Clamp(_settings.RuntimeLogHeight, 180, 480),
            BackColor = BackColor,
            Padding = new Padding(12, 10, 12, 8)
        };
        _logResizeGrip = new Panel
        {
            Dock = DockStyle.Top,
            Height = 6,
            Cursor = Cursors.SizeNS,
            BackColor = Color.FromArgb(190, 211, 205)
        };
        _logResizeGrip.MouseDown += BeginLogWindowResize;
        _logResizeGrip.MouseMove += ResizeLogWindow;
        _logResizeGrip.MouseUp += EndLogWindowResize;
        _logHost.Controls.Add(_logResizeGrip);
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
        _logFollowStateLabel = CreateText(L("正在跟随最新输出", "Following live output"), new Rectangle(20, 48, 200, 24), 8F, Color.FromArgb(134, 200, 187), FontStyle.Bold);
        logCard.Controls.Add(_logFollowStateLabel);

        var allLogsButton = CreateActionButton(L("全部", "All"), Color.FromArgb(35, 104, 98), 68);
        var errorLogsButton = CreateActionButton(L("错误", "Errors"), Theme.Coral, 76);
        var currentLogsButton = CreateActionButton(L("当前", "Current"), Color.FromArgb(47, 83, 132), 76);
        var copyLogsButton = CreateActionButton(L("复制", "Copy"), Color.FromArgb(31, 121, 108), 76);
        var clearLogsButton = CreateActionButton(L("清空", "Clear"), Color.FromArgb(96, 99, 108), 76);
        var followLogsButton = CreateActionButton(L("暂停跟随", "Pause follow"), Color.FromArgb(44, 103, 96), 88);
        var diagnosticsButton = CreateActionButton(L("诊断", "Diagnose"), Color.FromArgb(170, 102, 49), 72);
        var logButtons = new[] { allLogsButton, errorLogsButton, currentLogsButton, followLogsButton, copyLogsButton, diagnosticsButton, clearLogsButton };
        foreach (var button in logButtons)
        {
            button.Height = 28;
            button.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold);
            button.Visible = true;
            logCard.Controls.Add(button);
        }
        allLogsButton.Click += (_, _) => SetLogFilter(LauncherLogFilter.All);
        errorLogsButton.Click += (_, _) => SetLogFilter(LauncherLogFilter.Errors);
        currentLogsButton.Click += (_, _) => SetLogFilter(LauncherLogFilter.CurrentService);
        followLogsButton.Click += (_, _) =>
        {
            ToggleLogFollow();
            followLogsButton.Text = _logAutoFollow ? L("暂停跟随", "Pause follow") : L("跟随最新", "Follow live");
        };
        diagnosticsButton.Click += (_, _) => ShowEnvironmentReport();
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
            _unseenLogEntryCount = 0;
            RenderLog();
        };

        _log.Name = "logOutput";
        _log.ReadOnly = true;
        _log.BorderStyle = BorderStyle.None;
        _log.BackColor = logCard.FillColor;
        _log.ForeColor = Color.FromArgb(216, 236, 230);
        _log.Font = new Font("Cascadia Mono", 9F);
        _log.DetectUrls = true;
        _log.LinkClicked += (_, eventArgs) =>
        {
            if (!string.IsNullOrWhiteSpace(eventArgs.LinkText))
            {
                Process.Start(new ProcessStartInfo(eventArgs.LinkText) { UseShellExecute = true });
            }
        };
        _log.VScroll += (_, _) => BeginInvoke(new Action(UpdateLogFollowState));
        _log.MouseWheel += (_, _) => BeginInvoke(new Action(UpdateLogFollowState));
        _log.Visible = true;
        logCard.Controls.Add(_log);

        void LayoutLogCard()
        {
            _logSummaryLabel.Width = Math.Max(180, logCard.ClientSize.Width - _logSummaryLabel.Left - 20);
            var x = _logFollowStateLabel.Right + 12;
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
            Height = 44,
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
        mainShell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        mainShell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
        mainShell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68));
        mainShell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var navigation = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 10, 0),
            FillColor = Theme.DeepTeal,
            CornerRadius = 16
        };
        navigation.Controls.Add(CreateText(L("工作区", "WORKSPACE"), new Rectangle(20, 18, 145, 24), 8F, Color.FromArgb(155, 205, 196), FontStyle.Bold));
        var pluginsButton = CreateActionButton(L("插件", "Plugins"), Color.FromArgb(37, 125, 115), 140);
        pluginsButton.Location = new Point(20, 54);
        var settingsButton = CreateActionButton(L("设置", "Settings"), Color.FromArgb(31, 91, 87), 140);
        settingsButton.Location = new Point(20, 102);
        settingsButton.Click += (_, _) => ShowSettingsWorkspace();
        pluginsButton.Click += (_, _) => ShowPluginWorkspace();
        navigation.Controls.Add(pluginsButton);
        navigation.Controls.Add(settingsButton);
        var workspaceButtons = new[] { pluginsButton, settingsButton };
        void LayoutWorkspaceButtons()
        {
            var buttonWidth = Math.Min(140, Math.Max(116, navigation.ClientSize.Width - 28));
            var left = Math.Max(14, (navigation.ClientSize.Width - buttonWidth) / 2);
            foreach (var button in workspaceButtons)
            {
                button.Width = buttonWidth;
                button.Left = left;
            }
        }
        navigation.SizeChanged += (_, _) => LayoutWorkspaceButtons();
        LayoutWorkspaceButtons();
        navigation.Controls.Add(CreateText(L("单模型安全模式", "SINGLE MODEL MODE"), new Rectangle(20, 174, 150, 25), 8F, Color.FromArgb(159, 211, 201), FontStyle.Bold));
        navigation.Controls.Add(CreateParagraph(L("启动新插件前会检查端口与显存占用。", "Ports and GPU memory are checked before launch."), new Rectangle(20, 206, 150, 72), 8F, Color.FromArgb(202, 229, 223), FontStyle.Regular));
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
        _detailPanel = detailPanel;
        _detailTitleLabel = CreateText(string.Empty, new Rectangle(30, 22, 500, 39), 17F, Theme.Ink, FontStyle.Bold);
        _detailDescriptionLabel = CreateParagraph(string.Empty, new Rectangle(30, 66, 500, 54), 9F, Theme.Muted, FontStyle.Regular);
        _detailStatusLabel = CreateText(string.Empty, new Rectangle(30, 126, 500, 32), 10F, Theme.DeepTeal, FontStyle.Bold);
        detailPanel.Controls.Add(_detailTitleLabel);
        detailPanel.Controls.Add(_detailDescriptionLabel);
        detailPanel.Controls.Add(_detailStatusLabel);
        detailPanel.Controls.Add(CreateText(L("插件信息", "PLUGIN INFO"), new Rectangle(30, 158, 180, 22), 8F, Color.FromArgb(89, 130, 124), FontStyle.Bold));
        _detailRootLabel = CreateParagraph(string.Empty, new Rectangle(30, 184, 500, 36), 8F, Theme.Ink, FontStyle.Regular);
        _detailPortLabel = CreateText(string.Empty, new Rectangle(30, 224, 240, 25), 9F, Theme.Ink, FontStyle.Bold);
        _detailMemoryLabel = CreateText(string.Empty, new Rectangle(280, 224, 250, 25), 9F, Theme.Ink, FontStyle.Bold);
        _detailVersionLabel = CreateText(string.Empty, new Rectangle(30, 253, 500, 24), 8.5F, Theme.Ink, FontStyle.Bold);
        _detailDependencyLabel = CreateParagraph(string.Empty, new Rectangle(30, 281, 500, 40), 8F, Theme.Muted, FontStyle.Regular);
        _detailTrustLabel = CreateText(string.Empty, new Rectangle(30, 325, 500, 24), 8.5F, Theme.MidTeal, FontStyle.Bold);
        detailPanel.Controls.Add(_detailRootLabel);
        detailPanel.Controls.Add(_detailPortLabel);
        detailPanel.Controls.Add(_detailMemoryLabel);
        detailPanel.Controls.Add(_detailVersionLabel);
        detailPanel.Controls.Add(_detailDependencyLabel);
        detailPanel.Controls.Add(_detailTrustLabel);
        detailPanel.Controls.Add(CreateText(L("启动配置", "LAUNCH PROFILE"), new Rectangle(30, 360, 180, 22), 8F, Color.FromArgb(89, 130, 124), FontStyle.Bold));

        _stableModePanel = new FlowLayoutPanel
        {
            Location = new Point(30, 386),
            Size = new Size(500, 40),
            BackColor = Color.White,
            WrapContents = false
        };
        if (_smallSfx is not null) AddStableModeButton(_stableModePanel, "small-sfx", _smallSfx);
        if (_smallMusic is not null) AddStableModeButton(_stableModePanel, "small-music", _smallMusic);
        if (_medium is not null) AddStableModeButton(_stableModePanel, "medium", _medium);
        detailPanel.Controls.Add(_stableModePanel);

        _detailActionsPanel = new Panel
        {
            Location = new Point(30, 438),
            Size = new Size(500, 42),
            BackColor = Color.White
        };
        _detailPrimaryButton = CreateActionButton(L("启动插件", "Launch plugin"), Theme.DeepTeal, 115);
        _detailPrimaryButton.Location = Point.Empty;
        _detailPrimaryButton.Height = 42;
        _detailPrimaryButton.Click += (_, _) => HandlePrimaryPluginAction();
        _openButton = _detailPrimaryButton;
        _detailUpdateButton = CreateActionButton(L("更新插件", "Update plugin"), Color.FromArgb(170, 102, 49), 115);
        _detailUpdateButton.Location = Point.Empty;
        _detailUpdateButton.Height = 42;
        _detailUpdateButton.Visible = false;
        _detailUpdateButton.Click += async (_, _) => await UpdateSelectedPluginAsync();
        var stopButton = CreateActionButton(L("停止AI", "Stop AI"), Theme.Coral, 115);
        stopButton.Location = Point.Empty;
        stopButton.Height = 42;
        stopButton.Click += (_, _) => StopKnownServices();
        _detailStopButton = stopButton;
        _detailUninstallButton = CreateActionButton(L("卸载插件", "Uninstall"), Color.FromArgb(96, 99, 108), 115);
        _detailUninstallButton.Location = Point.Empty;
        _detailUninstallButton.Height = 42;
        _detailUninstallButton.Click += async (_, _) => await UninstallSelectedPluginAsync();
        _detailActionsPanel.Controls.Add(_detailPrimaryButton);
        _detailActionsPanel.Controls.Add(_detailUpdateButton);
        _detailActionsPanel.Controls.Add(stopButton);
        _detailActionsPanel.Controls.Add(_detailUninstallButton);
        detailPanel.Controls.Add(_detailActionsPanel);
        _detailUpdateProgress = new GlassProgressBar
        {
            Location = new Point(30, 492),
            Size = new Size(490, 9),
            Minimum = 0,
            Maximum = 100,
            TrackColor = Color.FromArgb(220, 234, 230),
            FillColor = Color.FromArgb(38, 151, 126),
            BorderColor = Color.FromArgb(155, 197, 187),
            Visible = false
        };
        detailPanel.Controls.Add(_detailUpdateProgress);
        _detailActionHint = CreateParagraph(L("模型启动后，主按钮会自动切换为打开 WebUI。", "The primary action changes to Open WebUI when the service is ready."), new Rectangle(30, 492, 500, 42), 8.5F, Theme.Muted, FontStyle.Regular);
        detailPanel.Controls.Add(_detailActionHint);
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
            _detailActionsPanel.Width = width;
            _detailUpdateProgress.Width = width;
            _detailActionHint.Width = width;
            LayoutPluginActionButtons();
            LayoutDetailActionArea(_detailUpdateButton?.Visible == true);
        };
        _settingsWorkspace = new SettingsWorkspace(
            BuildSettingsCategories(),
            LauncherVersion,
            _useEnglish,
            CreateInlineSettingsCard);
        detailPanel.Controls.Add(_settingsWorkspace);
        mainShell.Controls.Add(detailPanel, 2, 0);

        Controls.Add(mainShell);
        Controls.Add(statusStrip);
        Controls.Add(_logHost);
        Controls.Add(header);
        LayoutHeader();
        LayoutLogCard();
        if (_pluginEntries.Count > 0)
        {
            SelectPlugin(_pluginEntries.Any(entry => entry.Id == _selectedPluginId) ? _selectedPluginId : _pluginEntries[0].Id);
        }
        else
        {
            UpdatePluginUi();
        }
        RenderLog();
        UpdateGpuIndicator();
    }

    private List<PluginUiEntry> BuildPluginEntries()
    {
        return _modelCatalog.Models.Select(definition =>
        {
            var hasStableSelector = IsStableAudioDefinition(definition) &&
                _selectedStableProfile is not null && _smallSfx is not null && _smallMusic is not null && _medium is not null;
            var profile = hasStableSelector ? _selectedStableProfile! : CreateCustomProfile(definition);
            return new PluginUiEntry(
                definition.Id,
                definition.DisplayName,
                string.IsNullOrWhiteSpace(definition.Description) ? L("从 GitHub 导入的本地 AI 服务。", "Local AI service imported from GitHub.") : definition.Description,
                definition.Category,
                profile,
                definition.RecommendedVramMiB,
                CategoryAccent(definition.Category),
                hasStableSelector);
        }).ToList();
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
                Text = _pluginEntries.Count == 0
                    ? L("尚未安装插件。\r\n请从 GitHub 添加模型。", "No plugins installed.\r\nAdd a model from GitHub.")
                    : L("没有符合当前条件的插件。", "No plugins match the current filters."),
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
        _settingsWorkspace?.Hide();
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
            var index = _pluginEntries.FindIndex(entry => entry.HasModelSelector);
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
        if (GetMissingRequirements(profile).Count > 0 ||
            PluginDependencyChecker.Check(profile.Dependencies, profile.WorkingDirectory).Any(check => check.IsEnforced && !check.IsSatisfied))
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

    internal static int[] CalculateEqualActionWidths(int availableWidth, int count, int gap)
    {
        if (count <= 0)
        {
            return [];
        }

        var contentWidth = Math.Max(count, availableWidth - Math.Max(0, count - 1) * Math.Max(0, gap));
        var baseWidth = contentWidth / count;
        var remainder = contentWidth % count;
        return Enumerable.Range(0, count)
            .Select(index => baseWidth + (index < remainder ? 1 : 0))
            .ToArray();
    }

    private void LayoutPluginActionButtons()
    {
        if (_detailActionsPanel is null)
        {
            return;
        }

        var buttons = new[] { _detailPrimaryButton, _detailUpdateButton, _detailStopButton, _detailUninstallButton }
            .Where(button => button?.Visible == true)
            .Cast<RoundedButton>()
            .ToArray();
        const int gap = 10;
        var widths = CalculateEqualActionWidths(_detailActionsPanel.ClientSize.Width, buttons.Length, gap);
        var x = 0;
        for (var index = 0; index < buttons.Length; index++)
        {
            buttons[index].Bounds = new Rectangle(x, 0, widths[index], _detailActionsPanel.ClientSize.Height);
            x += widths[index] + gap;
        }
    }

    private void LayoutDetailActionArea(bool sourceUpdateAvailable)
    {
        if (_detailPanel is null || _detailActionsPanel is null || _detailUpdateProgress is null || _detailActionHint is null)
        {
            return;
        }

        const int left = 30;
        const int preferredActionTop = 438;
        const int actionGap = 12;
        const int progressGap = 8;
        const int bottomPadding = 10;
        var showProgress = _detailUpdateProgress.Visible;
        var hintHeight = sourceUpdateAvailable ? 34 : 38;
        var requiredHeight = _detailActionsPanel.Height + actionGap + hintHeight + bottomPadding;
        if (showProgress)
        {
            requiredHeight += _detailUpdateProgress.Height + progressGap;
        }

        var topAfterLaunchProfile = _stableModePanel?.Visible == true
            ? _stableModePanel.Bottom + 6
            : preferredActionTop;
        var actionTop = Math.Min(preferredActionTop, _detailPanel.ClientSize.Height - requiredHeight);
        actionTop = Math.Max(topAfterLaunchProfile, actionTop);

        _detailActionsPanel.Location = new Point(left, actionTop);
        if (showProgress)
        {
            _detailUpdateProgress.Location = new Point(left, _detailActionsPanel.Bottom + actionGap);
            _detailActionHint.Location = new Point(left, _detailUpdateProgress.Bottom + progressGap);
        }
        else
        {
            _detailUpdateProgress.Location = new Point(left, _detailActionsPanel.Bottom + actionGap);
            _detailActionHint.Location = new Point(left, _detailActionsPanel.Bottom + actionGap);
        }

        _detailActionHint.Height = hintHeight;
    }

    private void UpdatePluginUi()
    {
        if (_pluginEntries.Count == 0)
        {
            if (_detailTitleLabel is not null) _detailTitleLabel.Text = L("尚未安装插件", "No plugins installed");
            if (_detailDescriptionLabel is not null) _detailDescriptionLabel.Text = L("使用“工具 > 从 GitHub 添加模型”下载并配置第一个模型。", "Use Tools > Add model from GitHub to download and configure your first model.");
            if (_detailStatusLabel is not null) _detailStatusLabel.Text = L("插件库为空", "Plugin library is empty");
            if (_detailRootLabel is not null) _detailRootLabel.Text = string.Empty;
            if (_detailPortLabel is not null) _detailPortLabel.Text = string.Empty;
            if (_detailMemoryLabel is not null) _detailMemoryLabel.Text = string.Empty;
            if (_detailVersionLabel is not null) _detailVersionLabel.Text = string.Empty;
            if (_detailDependencyLabel is not null) _detailDependencyLabel.Text = string.Empty;
            if (_detailTrustLabel is not null) _detailTrustLabel.Text = string.Empty;
            if (_stableModePanel is not null) _stableModePanel.Visible = false;
            if (_detailPrimaryButton is not null) _detailPrimaryButton.Enabled = false;
            if (_detailUninstallButton is not null) _detailUninstallButton.Enabled = false;
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
        var sourceUpdateAvailable = _availableSourceUpdates.ContainsKey(Path.GetFullPath(selected.Profile.WorkingDirectory));
        if (_detailTitleLabel is not null) _detailTitleLabel.Text = selected.Title;
        if (_detailDescriptionLabel is not null) _detailDescriptionLabel.Text = selected.Description;
        if (_detailStatusLabel is not null)
        {
            _detailStatusLabel.Text = $"●  {RuntimeStateText(selectedState)}";
            _detailStatusLabel.ForeColor = RuntimeStateColor(selectedState);
        }
        if (_detailRootLabel is not null) _detailRootLabel.Text = L("目录：", "Directory: ") + selected.Profile.WorkingDirectory;
        if (_detailPortLabel is not null) _detailPortLabel.Text = $"PORT  {selected.Profile.Port}";
        if (_detailMemoryLabel is not null) _detailMemoryLabel.Text = selected.RecommendedVramMiB > 0 ? $"VRAM  {selected.RecommendedVramMiB / 1024D:0.#} GiB" : "VRAM  --";
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
        if (_detailUpdateButton is not null)
        {
            _detailUpdateButton.Visible = sourceUpdateAvailable;
            _detailUpdateButton.Enabled = sourceUpdateAvailable && selectedState is not (ServiceRuntimeState.Checking or ServiceRuntimeState.Starting or ServiceRuntimeState.Stopping or ServiceRuntimeState.Updating);
            _detailUpdateButton.Invalidate();
        }
        if (_detailUninstallButton is not null)
        {
            _detailUninstallButton.Enabled = selectedState is not (ServiceRuntimeState.Checking or ServiceRuntimeState.Starting or ServiceRuntimeState.Stopping or ServiceRuntimeState.Updating);
            _detailUninstallButton.Invalidate();
        }
        if (_detailUpdateProgress is not null)
        {
            _detailUpdateProgress.Visible = selectedState == ServiceRuntimeState.Updating &&
                _pluginUpdateInProgress && selected.Id.Equals(_updatingPluginId, StringComparison.OrdinalIgnoreCase);
            if (_detailUpdateProgress.Visible && _latestPluginUpdateProgress is not null)
            {
                UpdateSelectedPluginProgress(_latestPluginUpdateProgress);
            }
        }
        LayoutPluginActionButtons();
        LayoutDetailActionArea(sourceUpdateAvailable);
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
        var gpu = SystemResourceProbe.ReadPrimaryGpu();
        if (_gpuNameLabel is null || _gpuSummaryLabel is null || _gpuMeter is null)
        {
            return;
        }
        if (gpu is null)
        {
            _gpuNameLabel.Text = L("未检测到 NVIDIA GPU", "NVIDIA GPU not detected");
            _gpuSummaryLabel.Text = L("显存不可用", "GPU unavailable");
            _gpuMeter.SetValue(0, 1);
            return;
        }
        _gpuNameLabel.Text = gpu.Name;
        _gpuSummaryLabel.Text = SystemResourceProbe.FormatGpuUsageGiB(gpu.UsedMiB, gpu.TotalMiB);
        _toolTip.SetToolTip(_gpuSummaryLabel, L(
            $"NVIDIA 专用显存（nvidia-smi 原始值）：{gpu.UsedMiB:N0} / {gpu.TotalMiB:N0} MiB",
            $"Dedicated NVIDIA VRAM (raw nvidia-smi values): {gpu.UsedMiB:N0} / {gpu.TotalMiB:N0} MiB"));
        _gpuMeter.SetValue(gpu.UsedMiB, gpu.TotalMiB);
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
        maintenanceButton.Click += (_, _) => ShowSettingsWorkspace();
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
        var modelNumber = 1;
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
        var updateSourcesButton = CreateActionButton(L("更新插件源码", "Update plugin source"), Color.FromArgb(170, 102, 49), 176);
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
            var width = Math.Max(760, canvas.ClientSize.Width);
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

            var controlLayout = ServiceControlLayoutPlanner.Calculate(
                width,
                stopButton.Width,
                refreshButton.Width,
                _openButton.Width,
                checkUpdatesButton.Width,
                updateSourcesButton.Width);
            controlRow.Height = controlLayout.Height;
            controlTitle.Visible = controlLayout.ShowLabels;
            controlSubtitle.Visible = controlLayout.ShowLabels;
            stopButton.Location = controlLayout.StopButton;
            refreshButton.Location = controlLayout.RefreshButton;
            _openButton.Location = controlLayout.OpenButton;
            checkUpdatesButton.Location = controlLayout.CheckUpdatesButton;
            updateSourcesButton.Location = controlLayout.UpdateSourceButton;
            _statusLabel.Location = controlLayout.StatusLabel;
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
        if (_smallSfx is null || _smallMusic is null || _medium is null)
        {
            return;
        }
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

    private static SafeTextLabel CreateWorkspaceButton(string text, Color color, int width, int height)
    {
        return new SafeTextLabel
        {
            Text = text,
            Size = new Size(width, height),
            BackColor = color,
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleCenter,
            Cursor = Cursors.Hand,
            Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold)
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
        var launchHuggingFaceToken = string.Empty;
        var definition = _modelCatalog.Models.FirstOrDefault(model =>
            Path.GetFullPath(model.RootDirectory).Equals(Path.GetFullPath(profile.WorkingDirectory), StringComparison.OrdinalIgnoreCase));
        if (definition is not null)
        {
            var normalizedArguments = KnownRepositoryEnvironmentService.NormalizeLaunchArguments(definition.GitHubRepository, definition.Arguments);
            if (!normalizedArguments.Equals(definition.Arguments, StringComparison.Ordinal))
            {
                definition.Arguments = normalizedArguments;
                SaveModelCatalog(_modelCatalog);
            }
            var normalizedProfileArguments = KnownRepositoryEnvironmentService.NormalizeLaunchArguments(
                definition.GitHubRepository,
                profile.Arguments);
            if (!normalizedProfileArguments.Equals(profile.Arguments, StringComparison.Ordinal))
            {
                profile = profile with
                {
                    Arguments = ExpandModelValue(normalizedProfileArguments, profile.WorkingDirectory, profile.Port)
                };
            }

            var trust = InstalledPluginTrustValidator.Verify(definition, _trustedPublishers);
            if (!trust.IsTrusted)
            {
                SetServiceRuntimeState(profile, ServiceRuntimeState.Error);
                SetRuntimePhase("插件信任验证失败", $"Trust validation failed for {profile.Name}");
                AppendLog(L("插件信任验证失败：", "Plugin trust validation failed: ") + trust.Message, profile, true);
                ShowActionableError(L("已阻止不可信启动命令", "Untrusted launch command blocked"), trust.Message, profile);
                return;
            }

            if (KnownRepositoryAssetService.HasMissingAssets(definition.GitHubRepository, profile.Arguments, profile.WorkingDirectory))
            {
                try
                {
                    SetRuntimePhase("正在补全 Woosh 模型权重", $"Downloading required Woosh model assets for {profile.Name}");
                    var progress = new Progress<string>(message => AppendLog(message, profile));
                    await KnownRepositoryAssetService.EnsureAssetsAsync(
                        definition.GitHubRepository,
                        profile.Arguments,
                        profile.WorkingDirectory,
                        _settings.DataRoot,
                        _githubClient,
                        progress);
                }
                catch (Exception ex)
                {
                    SetServiceRuntimeState(profile, ServiceRuntimeState.Missing);
                    SetRuntimePhase("Woosh 模型权重补全失败", $"Woosh model asset repair failed for {profile.Name}");
                    AppendLog(L("Woosh 模型权重补全失败：", "Woosh model asset repair failed: ") + ex.Message, profile, true);
                    ShowActionableError(L("Woosh 权重未就绪", "Woosh model assets are not ready"), ex.Message, profile);
                    return;
                }
            }

            if (KnownRepositoryEnvironmentService.HasMissingEnvironment(definition.GitHubRepository, profile.Arguments, profile.WorkingDirectory))
            {
                try
                {
                    SetRuntimePhase("正在修复 Stable Audio 3 运行环境", $"Repairing the Stable Audio 3 environment for {profile.Name}");
                    var progress = new Progress<string>(message => AppendLog(message, profile));
                    await KnownRepositoryEnvironmentService.EnsureEnvironmentAsync(
                        definition.GitHubRepository,
                        profile.Arguments,
                        profile.WorkingDirectory,
                        _settings.DataRoot,
                        _githubClient,
                        SystemResourceProbe.ReadPrimaryGpu() is not null,
                        progress);
                }
                catch (Exception ex)
                {
                    SetServiceRuntimeState(profile, ServiceRuntimeState.Missing);
                    SetRuntimePhase("Stable Audio 3 运行环境修复失败", $"Stable Audio 3 environment repair failed for {profile.Name}");
                    AppendLog(L("Stable Audio 3 运行环境修复失败：", "Stable Audio 3 environment repair failed: ") + ex.Message, profile, true);
                    ShowActionableError(L("Stable Audio 3 环境未就绪", "Stable Audio 3 environment is not ready"), ex.Message, profile);
                    return;
                }
            }

            var authorizationManifest = KnownRepositoryAuthorizationService.CreateLaunchManifest(
                definition.GitHubRepository,
                profile.Arguments,
                profile.Name);
            if (authorizationManifest is not null)
            {
                var authorization = await EnsureLaunchAuthorizationAsync(authorizationManifest);
                if (!authorization.IsAuthorized)
                {
                    SetServiceRuntimeState(profile, ServiceRuntimeState.Ready);
                    SetRuntimePhase("Hugging Face 授权尚未完成", $"Hugging Face authorization was not completed for {profile.Name}");
                    return;
                }
                launchHuggingFaceToken = authorization.Token;
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
        var dependencyFailures = PluginDependencyChecker.Check(profile.Dependencies, profile.WorkingDirectory)
            .Where(check => check.IsEnforced && !check.IsSatisfied)
            .ToArray();
        if (dependencyFailures.Length > 0)
        {
            var details = string.Join(Environment.NewLine, dependencyFailures.Select(check => $"{check.Requirement}: {check.Details}"));
            SetServiceRuntimeState(profile, ServiceRuntimeState.Missing);
            SetRuntimePhase("插件依赖未就绪", $"{profile.Name} dependencies are not ready");
            AppendLog(L("依赖检查失败：", "Dependency check failed: ") + details.Replace(Environment.NewLine, " | "), profile, true);
            ShowActionableError(L("插件依赖未就绪", "Plugin dependencies are not ready"), details, profile);
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
            if (definition?.Runtime.Equals("python", StringComparison.OrdinalIgnoreCase) == true ||
                profile.Executable.EndsWith("python.exe", StringComparison.OrdinalIgnoreCase))
            {
                startInfo.Environment["HF_HUB_DISABLE_XET"] = "1";
                startInfo.Environment["PYTHONUNBUFFERED"] = "1";
                startInfo.Environment["PYTHONUTF8"] = "1";
                startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
                startInfo.Environment["GRADIO_SERVER_NAME"] = "127.0.0.1";
                startInfo.Environment["GRADIO_SERVER_PORT"] = profile.Port.ToString();
                startInfo.Environment["PORT"] = profile.Port.ToString();
                if (profile.IsMedium)
                {
                    startInfo.Environment["PYTORCH_CUDA_ALLOC_CONF"] = "expandable_segments:True";
                }
                KnownRepositoryAuthorizationService.ApplyCredential(startInfo, launchHuggingFaceToken);
            }

            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) AppendLog(e.Data, profile); };
            process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) AppendLog("ERR " + e.Data, profile, true); };
            process.Exited += (_, _) => BeginInvoke(() =>
            {
                if (_activeProcess?.Id == process.Id)
                {
                    var exitCode = process.ExitCode;
                    var wasStopping = _runtimeStates.TryGetValue(ServiceKey(profile), out var priorState) && priorState == ServiceRuntimeState.Stopping;
                    _activeProcess = null;
                    _activeService = null;
                    _openButton.Enabled = false;
                    SetRuntimePhase(wasStopping ? "服务已停止" : "服务进程已退出", wasStopping ? $"{profile.Name} stopped" : $"{profile.Name} exited");
                    AppendLog(L($"服务进程已退出（PID {process.Id}，退出码 {exitCode}）。", $"Service process exited (PID {process.Id}, exit code {exitCode})."), profile, !wasStopping);
                    SetServiceRuntimeState(profile, wasStopping ? ServiceRuntimeState.Ready : ServiceRuntimeState.Error);
                    if (!wasStopping)
                    {
                        var details = BuildLaunchFailureDetails(profile, exitCode);
                        ShowActionableError(L("插件启动失败", "Plugin launch failed"), details, profile);
                    }
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
        var copy = new Button { Text = L("复制失败详情", "Copy details"), Location = new Point(18, 184), Size = new Size(122, 38) };
        copy.Click += (_, _) => Clipboard.SetText(message);
        var check = new Button { Text = L("环境自检", "Environment check"), Location = new Point(150, 184), Size = new Size(132, 38) };
        check.Click += (_, _) => ShowEnvironmentReport();
        var folder = new Button { Text = L("打开模型目录", "Open model folder"), Location = new Point(292, 184), Size = new Size(132, 38) };
        folder.Click += (_, _) => Process.Start(new ProcessStartInfo("explorer.exe", $"\"{profile.WorkingDirectory}\"") { UseShellExecute = true });
        var logs = new Button { Text = L("查看日志", "View logs"), Location = new Point(434, 184), Size = new Size(122, 38) };
        logs.Click += (_, _) => OpenProfileLogFolder(profile);
        var close = new Button { Text = L("关闭", "Close"), DialogResult = DialogResult.OK, Location = new Point(566, 184), Size = new Size(142, 38) };
        dialog.Controls.Add(content);
        dialog.Controls.Add(copy);
        dialog.Controls.Add(check);
        dialog.Controls.Add(folder);
        dialog.Controls.Add(logs);
        dialog.Controls.Add(close);
        dialog.AcceptButton = close;
        dialog.ShowDialog(this);
    }

    private string BuildLaunchFailureDetails(ServiceProfile profile, int exitCode)
    {
        var recentErrors = _logEntries
            .Where(entry => entry.ServiceName?.Equals(profile.Name, StringComparison.OrdinalIgnoreCase) == true && entry.IsError)
            .TakeLast(12)
            .Select(entry => $"[{entry.Timestamp:HH:mm:ss}] {entry.Message}");
        var nativeCrash = exitCode == unchecked((int)0xC0000005)
            ? L(
                "检测到原生访问冲突 0xC0000005。通常来自 CUDA、PyTorch 原生扩展或显卡驱动，而不是普通 Python 异常。请关闭其他占用显存的程序、更新 NVIDIA 驱动，并优先测试 Small SFX；Medium 已自动启用半精度模式。",
                "Native access violation 0xC0000005 detected. This usually comes from CUDA, a native PyTorch extension, or the GPU driver rather than a normal Python exception. Close other GPU applications, update the NVIDIA driver, and test Small SFX first; Medium automatically uses half precision.")
            : string.Empty;
        var formattedExitCode = exitCode < 0
            ? $"{exitCode} (0x{unchecked((uint)exitCode):X8})"
            : exitCode.ToString();
        return string.Join(Environment.NewLine,
            [
                L($"插件 {profile.Name} 启动后立即退出。", $"{profile.Name} exited before its WebUI became ready."),
                $"Exit code: {formattedExitCode}",
                $"Executable: {profile.Executable}",
                $"Arguments: {profile.Arguments}",
                $"Working directory: {profile.WorkingDirectory}",
                $"Port: {profile.Port}",
                .. (string.IsNullOrWhiteSpace(nativeCrash) ? [] : new[] { "", nativeCrash }),
                "",
                L("最近错误：", "Recent errors:"),
                .. recentErrors
            ]);
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
        var known = GetKnownServicePids();
        var gpu = GetGpuMemoryUsage();
        var parts = new List<string>();
        foreach (var definition in _modelCatalog.Models.Take(3))
        {
            var listening = GetListeningPids(definition.Port).Count > 0;
            parts.Add(listening
                ? L($"{definition.DisplayName} {definition.Port} 正在监听", $"{definition.DisplayName} {definition.Port} listening")
                : L($"{definition.DisplayName} {definition.Port} 未监听", $"{definition.DisplayName} {definition.Port} idle"));
        }
        if (_modelCatalog.Models.Count == 0)
        {
            parts.Add(L("尚未安装插件", "No plugins installed"));
        }
        parts.Add(known.Count > 0 ? L($"已识别 AI 进程：{string.Join(",", known)}", $"Recognized AI processes: {string.Join(",", known)}") : L("未检测到已识别 AI 进程", "No recognized AI process"));
        if (gpu is not null)
        {
            parts.Add("GPU " + SystemResourceProbe.FormatGpuUsageGiB(gpu.Value.UsedMiB, gpu.Value.TotalMiB));
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
        var roots = _modelCatalog.Models.Select(definition => definition.RootDirectory)
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
        _logEntries.Add(new LauncherLogEntry(DateTime.Now, message, null, service?.Name ?? _activeService?.Name, error));
        if (!_logAutoFollow)
        {
            _unseenLogEntryCount++;
        }
        _diagnosticsService.Append(message, service?.Name ?? _activeService?.Name, error);
        RenderLog();
    }

    private void AppendLocalizedLog(string chinese, string english, ServiceProfile? service = null, bool isError = false)
    {
        if (IsDisposed)
        {
            return;
        }
        if (InvokeRequired)
        {
            BeginInvoke(() => AppendLocalizedLog(chinese, english, service, isError));
            return;
        }
        var error = isError || chinese.Contains("失败", StringComparison.OrdinalIgnoreCase)
            || english.Contains("error", StringComparison.OrdinalIgnoreCase)
            || english.Contains("exception", StringComparison.OrdinalIgnoreCase);
        _logEntries.Add(new LauncherLogEntry(DateTime.Now, chinese, english, service?.Name ?? _activeService?.Name, error));
        if (!_logAutoFollow)
        {
            _unseenLogEntryCount++;
        }
        _diagnosticsService.Append(chinese, service?.Name ?? _activeService?.Name, error);
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
            $"GPU: {SystemResourceProbe.ReadPrimaryGpu()?.Name ?? "Unavailable"}",
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
