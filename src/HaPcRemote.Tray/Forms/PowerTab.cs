using HaPcRemote.Service.Configuration;
using HaPcRemote.Service.Services;

namespace HaPcRemote.Tray.Forms;

internal sealed class PowerTab : TabPage, ISettingsTab
{
    private readonly IConfigurationWriter _configWriter;
    private readonly ToolTip _toolTip = new();
    private readonly NumericUpDown _autoSleepInput;
    private TabFooter? _footer;

    public PowerTab(IServiceProvider services)
    {
        Text = "Power";
        BackColor = Color.FromArgb(30, 30, 30);
        ForeColor = Color.White;
        Padding = new Padding(20);

        _configWriter = services.GetRequiredService<IConfigurationWriter>();
        var current = _configWriter.Read().Power;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            Padding = new Padding(0, 10, 0, 0)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // Auto-sleep timeout
        _autoSleepInput = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 480,
            Value = Math.Clamp(current.AutoSleepAfterMinutes, 0, 480),
            Width = 80,
            BackColor = Color.FromArgb(50, 50, 50),
            ForeColor = Color.White
        };

        var autoSleepPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true };
        autoSleepPanel.Controls.Add(_autoSleepInput);
        autoSleepPanel.Controls.Add(MakeHelpIcon(_toolTip,
            "Minutes of total inactivity before the PC sleeps automatically.\n" +
            "Requires: no Steam game running AND no mouse/keyboard/gamepad input.\n" +
            "Set to 0 to disable."));

        layout.Controls.Add(MakeLabel("Auto-Sleep (minutes):"), 0, 0);
        layout.Controls.Add(autoSleepPanel, 1, 0);

        Controls.Add(layout);
    }

    public IEnumerable<Button> CreateFooterButtons()
    {
        var applyButton = TabFooter.MakeSaveButton("Apply");
        var discardButton = TabFooter.MakeCancelButton("Discard");
        applyButton.Click += OnSave;
        discardButton.Click += OnCancel;
        return [applyButton, discardButton];
    }

    internal void SetFooter(TabFooter footer) => _footer = footer;

    private void OnSave(object? sender, EventArgs e)
    {
        _configWriter.SavePowerSettings(new PowerSettings
        {
            AutoSleepAfterMinutes = (int)_autoSleepInput.Value
        });
        _footer?.ShowStatus("Saved");
    }

    private void OnCancel(object? sender, EventArgs e)
    {
        var current = _configWriter.Read().Power;
        _autoSleepInput.Value = Math.Clamp(current.AutoSleepAfterMinutes, 0, 480);
    }

    private static Label MakeLabel(string text) => TabHelpers.MakeLabel(text, new Padding(0, 8, 0, 0));

    internal static Label MakeHelpIcon(ToolTip toolTip, string helpText) => TabHelpers.MakeHelpIcon(toolTip, helpText);
}
