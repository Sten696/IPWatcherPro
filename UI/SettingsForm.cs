using System.Drawing;
using System.Windows.Forms;
using IPWatcherPro.Core;

namespace IPWatcherPro.UI;

public class SettingsForm : Form
{
    private readonly AppConfiguration _current;

    public AppConfiguration? SavedConfiguration { get; private set; }

    private ComboBox cmbCountry = null!;
    private NumericUpDown numInterval = null!;
    private CheckBox chkNotify = null!;
    private CheckBox chkAutoStart = null!;
    private CheckBox chkAggressive = null!;
    private CheckBox chkLogAll = null!;
    private CheckBox chkExtended = null!;
    private NumericUpDown numActivityMaxItems = null!;
    private NumericUpDown numLogMaxMb = null!;

    public SettingsForm(AppConfiguration current)
    {
        _current = current;
        InitializeComponent();
        LoadValues();
    }

    private void InitializeComponent()
    {
        Text = "IPWatcherPro — Settings";
        Size = new Size(500, 520);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Padding = new Padding(20);

        int y = 10;
        int labelX = 10;
        int controlX = 220;
        int width = 220;

        var lblCountry = new Label
        {
            Text = "Target Country:",
            Location = new Point(labelX, y),
            AutoSize = true
        };

        cmbCountry = new ComboBox
        {
            Location = new Point(controlX, y - 3),
            Width = width,
            DropDownStyle = ComboBoxStyle.DropDownList
        };

        foreach (var country in CountryList.Countries)
            cmbCountry.Items.Add(country);

        Controls.Add(lblCountry);
        Controls.Add(cmbCountry);

        y += 35;

        var lblInterval = new Label
        {
            Text = "Refresh Interval (min):",
            Location = new Point(labelX, y),
            AutoSize = true
        };

        numInterval = new NumericUpDown
        {
            Location = new Point(controlX, y - 3),
            Width = 90,
            Minimum = 1,
            Maximum = 1440,
            Value = 5
        };

        Controls.Add(lblInterval);
        Controls.Add(numInterval);

        y += 35;

        chkNotify = new CheckBox
        {
            Text = "Show balloon on country change",
            Location = new Point(labelX, y),
            Width = 420
        };

        Controls.Add(chkNotify);

        y += 30;

        chkAutoStart = new CheckBox
        {
            Text = "Start IPWatcherPro with Windows",
            Location = new Point(labelX, y),
            Width = 420
        };

        Controls.Add(chkAutoStart);

        y += 45;

        var grpHunter = new GroupBox
        {
            Text = "Hunter Mode",
            Location = new Point(10, y),
            Width = 450,
            Height = 110
        };

        chkAggressive = new CheckBox
        {
            Text = "Aggressive Polling (every 30s)",
            Location = new Point(10, 20),
            Width = 420
        };

        chkLogAll = new CheckBox
        {
            Text = "Log All Changes",
            Location = new Point(10, 45),
            Width = 420
        };

        chkExtended = new CheckBox
        {
            Text = "Extended Scan (more CPU/Network)",
            Location = new Point(10, 70),
            Width = 420
        };

        grpHunter.Controls.Add(chkAggressive);
        grpHunter.Controls.Add(chkLogAll);
        grpHunter.Controls.Add(chkExtended);
        Controls.Add(grpHunter);

        y += 125;

        var lblActivityMaxItems = new Label
        {
            Text = "Activity Window max events:",
            Location = new Point(labelX, y),
            AutoSize = true
        };

        numActivityMaxItems = new NumericUpDown
        {
            Location = new Point(controlX, y - 3),
            Width = 90,
            Minimum = 100,
            Maximum = 5000,
            Increment = 100,
            Value = 500
        };

        Controls.Add(lblActivityMaxItems);
        Controls.Add(numActivityMaxItems);

        y += 35;

        var lblLogMaxMb = new Label
        {
            Text = "activity.log max size (MB):",
            Location = new Point(labelX, y),
            AutoSize = true
        };

        numLogMaxMb = new NumericUpDown
        {
            Location = new Point(controlX, y - 3),
            Width = 90,
            Minimum = 1,
            Maximum = 100,
            Value = 10
        };

        Controls.Add(lblLogMaxMb);
        Controls.Add(numLogMaxMb);

        y += 50;

        var btnSave = new Button
        {
            Text = "Save",
            Location = new Point(270, y),
            Width = 90
        };

        btnSave.Click += BtnSave_Click;

        var btnCancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(370, y),
            Width = 90
        };

        Controls.Add(btnSave);
        Controls.Add(btnCancel);

        AcceptButton = btnSave;
        CancelButton = btnCancel;
    }

    private void LoadValues()
    {
        var targetCode = string.IsNullOrWhiteSpace(_current.TargetCountryCode)
            ? "US"
            : _current.TargetCountryCode.Trim();

        var selectedIndex = 0;

        for (var i = 0; i < cmbCountry.Items.Count; i++)
        {
            if (cmbCountry.Items[i] is Country country &&
                string.Equals(country.Code, targetCode, StringComparison.OrdinalIgnoreCase))
            {
                selectedIndex = i;
                break;
            }
        }

        if (cmbCountry.Items.Count > 0)
            cmbCountry.SelectedIndex = selectedIndex;

        numInterval.Value = Math.Clamp(_current.RefreshIntervalMinutes, 1, 1440);
        chkNotify.Checked = _current.NotifyOnCountryChange;
        chkAutoStart.Checked = _current.AutoStart;
        chkAggressive.Checked = _current.HunterMode_AggressivePolling;
        chkLogAll.Checked = _current.HunterMode_LogAllChanges;
        chkExtended.Checked = _current.HunterMode_ExtendedScan;

        numActivityMaxItems.Value = Math.Clamp(_current.HunterActivityMaxItems, 100, 5000);
        numLogMaxMb.Value = Math.Clamp(_current.LogMaxFileMb, 1, 100);
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        var selectedCountry = cmbCountry.SelectedItem as Country;
        var countryCode = selectedCountry?.Code ?? "US";

        SavedConfiguration = new AppConfiguration
        {
            TargetCountryCode = countryCode,
            RefreshIntervalMinutes = (int)numInterval.Value,
            NotifyOnCountryChange = chkNotify.Checked,
            AutoStart = chkAutoStart.Checked,
            HunterMode_AggressivePolling = chkAggressive.Checked,
            HunterMode_LogAllChanges = chkLogAll.Checked,
            HunterMode_ExtendedScan = chkExtended.Checked,
            HunterActivityMaxItems = (int)numActivityMaxItems.Value,
            LogMaxFileMb = (int)numLogMaxMb.Value
        };

        AppConfiguration.Save(SavedConfiguration);

        DialogResult = DialogResult.OK;
        Close();
    }
}