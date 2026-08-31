namespace Aegis.Cafeteria.UI;

/// <summary>
/// Small, dependency-free startup window shown while the POS host is being prepared.
/// It intentionally contains no customer or credential information.
/// </summary>
internal sealed class StartupSplashForm : Form
{
    private readonly Label statusLabel;
    private readonly Label detailLabel;
    private readonly ProgressBar progress;

    public StartupSplashForm()
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.White;
        ClientSize = new Size(660, 360);
        ControlBox = false;
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterScreen;
        Text = "Food For The Poor Jamaica - Cafeteria";
        TopMost = false;

        var header = new Panel
        {
            BackColor = Color.FromArgb(22, 66, 105),
            Dock = DockStyle.Top,
            Height = 118
        };

        var organization = new Label
        {
            AutoSize = true,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 19F, FontStyle.Bold),
            Location = new Point(34, 24),
            Text = "FOOD FOR THE POOR JAMAICA"
        };

        var application = new Label
        {
            AutoSize = true,
            ForeColor = Color.WhiteSmoke,
            Font = new Font("Segoe UI", 11F, FontStyle.Regular),
            Location = new Point(37, 70),
            Text = "Cafeteria"
        };

        header.Controls.Add(organization);
        header.Controls.Add(application);

        var accent = new Panel
        {
            BackColor = Color.FromArgb(235, 139, 40),
            Dock = DockStyle.Top,
            Height = 7
        };

        var title = new Label
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(31, 44, 55),
            Font = new Font("Segoe UI", 16F, FontStyle.Bold),
            Location = new Point(36, 157),
            Text = "Starting cafeteria terminal"
        };

        this.statusLabel = new Label
        {
            AutoEllipsis = true,
            ForeColor = Color.FromArgb(45, 58, 69),
            Font = new Font("Segoe UI", 11F, FontStyle.Regular),
            Location = new Point(39, 211),
            Size = new Size(580, 28),
            Text = "Preparing application..."
        };

        this.detailLabel = new Label
        {
            AutoEllipsis = true,
            ForeColor = Color.FromArgb(105, 112, 118),
            Font = new Font("Segoe UI", 9F, FontStyle.Regular),
            Location = new Point(39, 244),
            Size = new Size(580, 24),
            Text = "Please wait while the terminal initializes."
        };

        this.progress = new ProgressBar
        {
            Location = new Point(40, 285),
            Size = new Size(578, 14),
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 28
        };

        var version = new Label
        {
            ForeColor = Color.FromArgb(135, 140, 145),
            Font = new Font("Segoe UI", 8.5F),
            Location = new Point(39, 316),
            Size = new Size(580, 22),
            Text = "Secure local-first point of sale"
        };

        Controls.Add(version);
        Controls.Add(this.progress);
        Controls.Add(this.detailLabel);
        Controls.Add(this.statusLabel);
        Controls.Add(title);
        Controls.Add(accent);
        Controls.Add(header);
    }

    public void SetStatus(string status, string? detail = null)
    {
        this.statusLabel.Text = status;
        if (!string.IsNullOrWhiteSpace(detail))
            this.detailLabel.Text = detail;
    }

    public void SetReady(string detail)
    {
        this.statusLabel.Text = "Ready";
        this.detailLabel.Text = detail;
        this.progress.Style = ProgressBarStyle.Continuous;
        this.progress.MarqueeAnimationSpeed = 0;
        this.progress.Value = 100;
    }
}
