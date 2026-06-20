using System.Drawing;
using System.Runtime.InteropServices;
using IPWatcherPro.Core;
using IPWatcherPro.Infrastructure;

namespace IPWatcherPro.UI;

public sealed class TrayController : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly NetworkEventHub _hub;
    private readonly ContextMenuStrip _menu;
    private readonly TrayStatusPopup _statusPopup = new();

    private AppConfiguration _config;
    private bool _disposed;

    private string? _currentIp;
    private string? _currentCountryCode;
    private string? _currentCountryName;
    private string? _currentGateway;
    private DateTimeOffset _lastUpdated;

    public Action? OnSettingsRequested;
    public Action? OnRefreshRequested;
    public Action? OnShowActivityRequested;
    public Action? OnExitRequested;

    public TrayController(NetworkEventHub hub, AppConfiguration config)
    {
        _hub = hub;
        _config = config;

        _menu = BuildMenu();

        _icon = new NotifyIcon
        {
            Visible = true,
            Text = SafeTooltip("IPWatcherPro — Initializing..."),
            Icon = CreateCircleIcon(Color.Gray),
            ContextMenuStrip = _menu
        };

        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                ShowStatusPopup();
        };

        _icon.DoubleClick += (_, _) => SafeInvoke(OnSettingsRequested);

        _hub.OnIpChanged += OnIpChanged;
        _hub.OnLeakDetected += OnLeakDetected;
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        var version =
            System.Reflection.Assembly
                .GetExecutingAssembly()
                .GetName()
                .Version;

        var title =
            new ToolStripLabel(
                $"IPWatcherPro v{version?.Major}.{version?.Minor}.{version?.Build}");

        if (title.Font != null)
            title.Font = new Font(title.Font, FontStyle.Bold);

        menu.Items.Add(title);
        menu.Items.Add(new ToolStripSeparator());

        var refresh = new ToolStripMenuItem("&Check IP Now");
        refresh.Click += (_, _) => SafeInvoke(OnRefreshRequested);
        menu.Items.Add(refresh);

        var activity = new ToolStripMenuItem("Show &Activity");
        activity.Click += (_, _) => SafeInvoke(OnShowActivityRequested);
        menu.Items.Add(activity);

        var settings = new ToolStripMenuItem("&Settings");
        settings.Click += (_, _) => SafeInvoke(OnSettingsRequested);
        menu.Items.Add(settings);

        menu.Items.Add(new ToolStripSeparator());

        var exit = new ToolStripMenuItem("E&xit");
        exit.Click += (_, _) => SafeInvoke(OnExitRequested);
        menu.Items.Add(exit);

        return menu;
    }

    private void OnIpChanged(IpChangedEventArgs args)
    {
        if (_disposed) return;

        Ui(() =>
        {
            _currentIp = args.NewIp;
            _currentCountryCode = args.NewCountryCode;
            _currentCountryName = args.NewCountryName;
            _currentGateway = args.Gateway;
            _lastUpdated = args.Timestamp;

            var country = args.NewCountryName ?? args.NewCountryCode ?? "Unknown";

            var isForeign =
                !string.IsNullOrWhiteSpace(args.NewCountryCode) &&
                !args.NewCountryCode.Equals(
                    _config.TargetCountryCode,
                    StringComparison.OrdinalIgnoreCase);

            SetIcon(isForeign ? Color.Red : Color.LimeGreen);

            _icon.Text = SafeTooltip(
                $"{args.NewCountryCode ?? "??"} {country} | {args.NewIp}");

            if (isForeign && _config.NotifyOnCountryChange)
            {
                ShowBalloon(
                    "Country Mismatch",
                    $"IP: {args.NewIp}\nCountry: {country}",
                    ToolTipIcon.Warning);
            }
        });
    }

    private void OnLeakDetected(LeakDetectedEventArgs args)
    {
        if (_disposed) return;

        Ui(() =>
        {
            SetIcon(Color.Red);

            ShowBalloon(
                "Geo-Leak Detected",
                $"Process: {args.OwnerProcessName}\nRemote IP: {args.RemoteIp}",
                ToolTipIcon.Error);
        });
    }

    private void ShowStatusPopup()
    {
        if (_disposed)
            return;

        Ui(() =>
        {
            _statusPopup.UpdateStatus(
                _currentIp,
                _currentCountryCode,
                _currentCountryName,
                _currentGateway,
                _lastUpdated == default ? DateTimeOffset.Now : _lastUpdated,
                _config);

            _statusPopup.ShowNearTray();
        });
    }

    public void UpdateConfig(AppConfiguration config)
    {
        _config = config;
    }

    public void ShowBalloon(
        string title,
        string text,
        ToolTipIcon icon = ToolTipIcon.Info,
        int timeout = 3000)
    {
        if (_disposed) return;

        try
        {
            _icon.BalloonTipTitle = SafeText(title, 63);
            _icon.BalloonTipText = SafeText(text, 255);
            _icon.BalloonTipIcon = icon;
            _icon.ShowBalloonTip(timeout);
        }
        catch
        {
        }
    }

    private void SetIcon(Color color)
    {
        try
        {
            var oldIcon = _icon.Icon;
            _icon.Icon = CreateCircleIcon(color);
            oldIcon?.Dispose();
        }
        catch
        {
        }
    }

    private static Icon CreateCircleIcon(Color color)
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);

        graphics.Clear(Color.Transparent);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        using var shadowBrush = new SolidBrush(Color.FromArgb(80, Color.Black));
        graphics.FillEllipse(shadowBrush, 6, 6, 22, 22);

        using var brush = new SolidBrush(color);
        graphics.FillEllipse(brush, 4, 4, 22, 22);

        using var pen = new Pen(Color.White, 2);
        graphics.DrawEllipse(pen, 4, 4, 22, 22);

        var handle = bitmap.GetHicon();

        try
        {
            using var tempIcon = Icon.FromHandle(handle);
            return (Icon)tempIcon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private void Ui(Action action)
    {
        if (_disposed)
            return;

        try
        {
            if (_menu.InvokeRequired)
                _menu.BeginInvoke(action);
            else
                action();
        }
        catch
        {
        }
    }

    private static void SafeInvoke(Action? action)
    {
        try
        {
            action?.Invoke();
        }
        catch
        {
        }
    }

    private static string SafeTooltip(string value)
    {
        return SafeText(value, 63)
            .Replace("\r", " ")
            .Replace("\n", " ");
    }

    private static string SafeText(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "IPWatcherPro";

        value = value.Trim();

        return value.Length > maxLength
            ? value[..maxLength]
            : value;
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;

        try
        {
            _hub.OnIpChanged -= OnIpChanged;
            _hub.OnLeakDetected -= OnLeakDetected;

            _statusPopup.Dispose();

            _icon.Visible = false;
            _icon.Icon?.Dispose();
            _icon.Dispose();
            _menu.Dispose();
        }
        catch
        {
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}