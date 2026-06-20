using System.Diagnostics;
using IPWatcherPro.Core;

namespace IPWatcherPro.UI;

public sealed class TrayStatusPopup : Form
{
    private readonly Label lblTitle;
    private readonly Label lblStatus;
    private readonly Label lblIp;
    private readonly Label lblLocation;
    private readonly Label lblHotkey;
    private readonly Label lblUpdated;
    private readonly LinkLabel linkIpleak;

    private string? _ip;

    public TrayStatusPopup()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        Width = 320;
        Height = 230;
        BackColor = Color.FromArgb(35, 35, 35);
        ForeColor = Color.White;
        Padding = new Padding(18);

        lblTitle = MakeLabel("🌐  IP Watcher", 16, 14, 260, true, Color.DeepSkyBlue);
        lblStatus = MakeLabel("◉  Connected", 20, 48, 260);
        lblIp = MakeLabel("▣  IP unknown", 20, 78, 270);
        lblLocation = MakeLabel("📍  Location unknown", 20, 108, 270);
        lblHotkey = MakeLabel("▦  HOTSKEY", 20, 138, 260, false, Color.Silver);
        lblUpdated = MakeLabel("◷  Updated: never", 20, 168, 260, false, Color.Gray);

        linkIpleak = new LinkLabel
        {
            Text = "Open ipleak.net →",
            Left = 20,
            Top = 198,
            Width = 180,
            LinkColor = Color.DeepSkyBlue,
            ActiveLinkColor = Color.White,
            BackColor = BackColor
        };

        linkIpleak.Click += (_, _) =>
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://ipleak.net",
                UseShellExecute = true
            });
        };

        lblIp.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_ip))
                Clipboard.SetText(_ip);
        };

        Controls.AddRange([
            lblTitle,
            lblStatus,
            lblIp,
            lblLocation,
            lblHotkey,
            lblUpdated,
            linkIpleak
        ]);
    }

    public void UpdateStatus(
        string? ip,
        string? countryCode,
        string? countryName,
        string? gateway,
        DateTimeOffset updated,
        AppConfiguration config)
    {
        _ip = ip;

        var country = countryName ?? countryCode ?? "Unknown";

        lblStatus.Text = $"◉  Connected  [target: {config.TargetCountryCode}]";
        lblIp.Text = $"▣  {ip ?? "Unknown"}   (click to copy)";
        lblLocation.Text = $"📍  {country}";
        lblHotkey.Text = $"▦  Gateway: {gateway ?? "Unknown"}";
        lblUpdated.Text = $"◷  Updated: {updated.LocalDateTime:HH:mm:ss}";
    }

    public void ShowNearTray()
    {
        var area = Screen.PrimaryScreen?.WorkingArea ?? Screen.GetWorkingArea(Cursor.Position);

        Left = area.Right - Width - 20;
        Top = area.Bottom - Height - 20;

        Show();
        Activate();
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        Hide();
    }

    private static Label MakeLabel(
        string text,
        int left,
        int top,
        int width,
        bool bold = false,
        Color? color = null)
    {
        return new Label
        {
            Text = text,
            Left = left,
            Top = top,
            Width = width,
            Height = 24,
            AutoSize = false,
            ForeColor = color ?? Color.White,
            BackColor = Color.FromArgb(35, 35, 35),
            Font = new Font(
    SystemFonts.DefaultFont,
    bold ? FontStyle.Bold : FontStyle.Regular)
        };
    }
}