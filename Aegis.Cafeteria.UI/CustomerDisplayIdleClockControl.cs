namespace Aegis.Cafeteria.UI;

public sealed class CustomerDisplayIdleClockControl : UserControl
{
    private static readonly Color Navy = Color.FromArgb(24, 45, 68);
    private static readonly Color Blue = Color.FromArgb(31, 111, 190);
    private static readonly Color Canvas = Color.FromArgb(244, 247, 250);
    private static readonly Color Muted = Color.FromArgb(104, 117, 132);

    private readonly Label clock = new()
    {
        AutoSize = true,
        Font = new Font("Segoe UI", 88, FontStyle.Bold),
        ForeColor = Navy,
        TextAlign = ContentAlignment.MiddleCenter,
        Margin = new Padding(0)
    };

    private readonly Label date = new()
    {
        AutoSize = true,
        Font = new Font("Segoe UI", 24, FontStyle.Regular),
        ForeColor = Muted,
        TextAlign = ContentAlignment.MiddleCenter,
        Margin = new Padding(0, 16, 0, 0)
    };

    private readonly System.Windows.Forms.Timer timer = new()
    {
        Interval = 1000
    };

    public CustomerDisplayIdleClockControl()
    {
        Dock = DockStyle.Fill;
        BackColor = Canvas;
        BuildLayout();
        this.timer.Tick += (_, _) => UpdateClock();
        UpdateClock();
    }

    public void StartClock()
    {
        UpdateClock();
        this.timer.Start();
    }

    public void StopClock()
    {
        this.timer.Stop();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.timer.Stop();
            this.timer.Dispose();
        }

        base.Dispose(disposing);
    }

    private void BuildLayout()
    {
        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Canvas,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(40)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 30F));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 70F));

        Label brand = new()
        {
            Dock = DockStyle.Fill,
            Text = "CAFETERIA",
            Font = new Font("Segoe UI", 22, FontStyle.Bold),
            ForeColor = Blue,
            TextAlign = ContentAlignment.BottomCenter,
            Padding = new Padding(0, 0, 0, 22)
        };
        root.Controls.Add(brand, 0, 0);

        FlowLayoutPanel clockStack = new()
        {
            AutoSize = true,
            Anchor = AnchorStyles.None,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(0)
        };
        this.clock.Anchor = AnchorStyles.None;
        this.date.Anchor = AnchorStyles.None;
        clockStack.Controls.Add(this.clock);
        clockStack.Controls.Add(this.date);
        root.Controls.Add(clockStack, 0, 1);

        Label greeting = new()
        {
            Dock = DockStyle.Fill,
            Text = "Welcome to Food For The Poor Jamaica",
            Font = new Font("Segoe UI", 16, FontStyle.Regular),
            ForeColor = Muted,
            TextAlign = ContentAlignment.TopCenter,
            Padding = new Padding(0, 34, 0, 0)
        };
        root.Controls.Add(greeting, 0, 2);

        Controls.Add(root);
    }

    private void UpdateClock()
    {
        DateTime now = DateTime.Now;
        this.clock.Text = now.ToString("h:mm:ss tt");
        this.date.Text = now.ToString("dddd, MMMM d, yyyy");
    }
}
