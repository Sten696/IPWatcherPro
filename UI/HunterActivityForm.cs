using System.Text;
using IPWatcherPro.Infrastructure;
using System.Globalization;

namespace IPWatcherPro.UI;

public sealed class HunterActivityForm : Form
{
    private readonly HunterActivityStore _store;

    private readonly Label _lblStats;
    private readonly Label _lblTop;
    private readonly ComboBox _cmbFilter;
    private readonly CheckBox _chkAutoScroll;
    private readonly ListView _list;
    private readonly System.Windows.Forms.Timer _timer;

    public HunterActivityForm(HunterActivityStore store)
    {
        _store = store;

        Text = "IPWatcherPro — Hunter Activity";
        Width = 980;
        Height = 580;
        StartPosition = FormStartPosition.CenterScreen;

        _lblStats = new Label
        {
            Dock = DockStyle.Top,
            Height = 34,
            Padding = new Padding(8),
            AutoSize = false
        };

        _lblTop = new Label
        {
            Dock = DockStyle.Top,
            Height = 34,
            Padding = new Padding(8),
            AutoSize = false
        };

        var filterPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 38
        };

        var lblFilter = new Label
        {
            Text = "Filter:",
            Left = 8,
            Top = 10,
            Width = 45
        };

        _cmbFilter = new ComboBox
        {
            Left = 58,
            Top = 6,
            Width = 150,
            DropDownStyle = ComboBoxStyle.DropDownList
        };

        _cmbFilter.Items.AddRange([
            "All",
            "Connections",
            "Geo",
            "Leaks",
            "Errors"
        ]);

        _cmbFilter.SelectedIndex = 0;
        _cmbFilter.SelectedIndexChanged += (_, _) => RefreshView();

        _chkAutoScroll = new CheckBox
        {
            Text = "Auto-scroll",
            Left = 225,
            Top = 8,
            Width = 110,
            Checked = true
        };

        filterPanel.Controls.Add(lblFilter);
        filterPanel.Controls.Add(_cmbFilter);
        filterPanel.Controls.Add(_chkAutoScroll);

        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true
        };

        _list.Columns.Add("Time", 90);
        _list.Columns.Add("Type", 95);
        _list.Columns.Add("Activity", 560);
        _list.Columns.Add("Process", 130);
        _list.Columns.Add("Country", 80);

        var bottom = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 42
        };

        var btnClear = new Button
        {
            Text = "Clear",
            Width = 90,
            Height = 28,
            Left = 8,
            Top = 7
        };

        btnClear.Click += (_, _) =>
        {
            _store.Clear();
            RefreshView();
        };

        var btnExport = new Button
        {
            Text = "Export CSV",
            Width = 100,
            Height = 28,
            Left = 108,
            Top = 7
        };

        btnExport.Click += (_, _) => ExportCsv();

        var btnClose = new Button
        {
            Text = "Close",
            Width = 90,
            Height = 28,
            Left = 218,
            Top = 7
        };

        btnClose.Click += (_, _) => Hide();

        bottom.Controls.Add(btnClear);
        bottom.Controls.Add(btnExport);
        bottom.Controls.Add(btnClose);

        Controls.Add(_list);
        Controls.Add(filterPanel);
        Controls.Add(_lblTop);
        Controls.Add(_lblStats);
        Controls.Add(bottom);

        _timer = new System.Windows.Forms.Timer
        {
            Interval = 1000
        };

        _timer.Tick += (_, _) => RefreshView();
        _timer.Start();

        RefreshView();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _timer.Dispose();

        base.Dispose(disposing);
    }

    private void RefreshView()
    {
        var snapshot = _store.GetSnapshot();

        var lastScan = snapshot.LastScanTime?.ToString("HH:mm:ss") ?? "never";

        _lblStats.Text =
            $"Last scan: {lastScan}    " +
            $"Scans: {snapshot.TotalScans}    " +
            $"Connections checked: {snapshot.ConnectionsChecked}    " +
            $"Geo lookups: {snapshot.GeoLookups}    " +
            $"Leaks: {snapshot.LeaksFound}";

        _lblTop.Text = BuildTopProcesses(snapshot.Items);

        var items = ApplyFilter(snapshot.Items).Take(300).ToList();

        _list.BeginUpdate();

        try
        {
            _list.Items.Clear();

            foreach (var item in items)
            {
                var row = new ListViewItem(item.Timestamp.ToLocalTime().ToString("HH:mm:ss"));
                row.SubItems.Add(item.Kind.ToString());
                row.SubItems.Add(item.Message);
                row.SubItems.Add(item.ProcessName ?? "");
                row.SubItems.Add(item.CountryCode ?? "-");

                if (item.Kind == HunterActivityKind.Leak)
                    row.BackColor = Color.MistyRose;
                else if (item.Kind == HunterActivityKind.Error)
                    row.BackColor = Color.LightYellow;
                else if (item.Kind == HunterActivityKind.GeoLookup)
                    row.BackColor = Color.AliceBlue;
                else if (item.Kind == HunterActivityKind.Scan)
                    row.BackColor = Color.WhiteSmoke;

                _list.Items.Add(row);
            }

            if (_chkAutoScroll.Checked && _list.Items.Count > 0)
                _list.EnsureVisible(0);
        }
        finally
        {
            _list.EndUpdate();
        }
    }

    private IEnumerable<HunterActivityItem> ApplyFilter(IReadOnlyList<HunterActivityItem> items)
    {
        var filter = _cmbFilter.SelectedItem?.ToString() ?? "All";

        return filter switch
        {
            "Connections" => items.Where(x => x.Kind == HunterActivityKind.Connection),
            "Geo" => items.Where(x => x.Kind == HunterActivityKind.GeoLookup),
            "Leaks" => items.Where(x => x.Kind == HunterActivityKind.Leak),
            "Errors" => items.Where(x => x.Kind == HunterActivityKind.Error),
            _ => items
        };
    }

    private static string BuildTopProcesses(IReadOnlyList<HunterActivityItem> items)
    {
        var top = items
            .Where(x => x.Kind == HunterActivityKind.Connection)
            .Where(x => !string.IsNullOrWhiteSpace(x.ProcessName))
            .GroupBy(x => x.ProcessName!)
            .Select(g => new { Process = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(5)
            .ToList();

        if (top.Count == 0)
            return "Top processes: no connection data yet";

        return "Top processes: " + string.Join("    ", top.Select(x => $"{x.Process}: {x.Count}"));
    }

    private void ExportCsv()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Export Hunter Activity",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            FileName = $"hunter_activity_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        var snapshot = _store.GetSnapshot();
        var items = ApplyFilter(snapshot.Items).Reverse().ToList();

        var sb = new StringBuilder();

        var separator = CultureInfo.CurrentCulture.TextInfo.ListSeparator;

        sb.AppendLine(string.Join(separator,
            "Timestamp",
            "Type",
            "Activity",
            "Process",
            "RemoteIp",
            "CountryCode",
            "CountryName"));

        foreach (var item in items)
        {
            sb.AppendLine(string.Join(separator,
                Csv(item.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")),
                Csv(item.Kind.ToString()),
                Csv(item.Message),
                Csv(item.ProcessName),
                Csv(item.RemoteIp),
                Csv(item.CountryCode),
                Csv(item.CountryName)));
        }

        File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8);

        MessageBox.Show(
            this,
            "Export completed.",
            "IPWatcherPro",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private static string Csv(string? value)
    {
        value ??= "";
        value = value.Replace("\"", "\"\"");
        return $"\"{value}\"";
    }
}