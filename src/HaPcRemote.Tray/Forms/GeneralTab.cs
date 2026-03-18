using HaPcRemote.Service.Configuration;
using HaPcRemote.Service.Logging;
using HaPcRemote.Service.Services;
using HaPcRemote.Tray.Logging;
using HaPcRemote.Tray.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HaPcRemote.Tray.Forms;

internal sealed class GeneralTab : TabPage, ISettingsTab
{
    private readonly KestrelRestartService _restartService;
    private readonly ToolTip _toolTip = new();
    private readonly ComboBox _logLevelCombo;
    private readonly CheckBox _autoUpdateCheck;
    private readonly CheckBox _includePrereleasesCheck;
    private readonly NumericUpDown _portInput;
    private readonly Label _portStatusLabel;
    private readonly Button _portSaveButton;
    private readonly Label _soundVolumeViewLabel;
    private readonly ComboBox _displaySwitchingCombo;
    private readonly IConfigurationWriter _configWriter;
    private readonly int _currentPort;

    public GeneralTab(IServiceProvider services)
    {
        Text = "General";
        BackColor = Color.FromArgb(30, 30, 30);
        ForeColor = Color.White;
        Padding = new Padding(20);

        _restartService = services.GetRequiredService<KestrelRestartService>();
        _configWriter = services.GetRequiredService<IConfigurationWriter>();
        var options = services.GetRequiredService<IOptions<PcRemoteOptions>>().Value;
        _currentPort = options.Port;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            Padding = new Padding(0, 10, 0, 0)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        int row = 0;

        // Port input + status
        var portPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true };
        _portInput = new NumericUpDown
        {
            Minimum = 1024,
            Maximum = 65535,
            Value = _currentPort,
            Width = 80,
            BackColor = Color.FromArgb(50, 50, 50),
            ForeColor = Color.White
        };
        _portStatusLabel = new Label { AutoSize = true, Padding = new Padding(5, 3, 0, 0) };
        _portSaveButton = new Button
        {
            Text = "Save & Apply",
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(50, 50, 50),
            ForeColor = Color.White,
            AutoSize = true,
            Visible = false,
            Cursor = Cursors.Hand
        };
        _portSaveButton.Click += OnPortSave;
        _portInput.ValueChanged += (_, _) =>
        {
            _portSaveButton.Visible = (int)_portInput.Value != _currentPort;
        };
        portPanel.Controls.Add(_portInput);
        portPanel.Controls.Add(_portStatusLabel);
        portPanel.Controls.Add(_portSaveButton);
        portPanel.Controls.Add(MakeHelpIcon(_toolTip,
            "HTTP port the service listens on.\n" +
            "Home Assistant must be configured with the same port.\n" +
            "The service restarts in-process — no UAC prompt or process relaunch.\n" +
            "Valid range: 1024–65535."));
        layout.Controls.Add(MakeLabel("Port:"), 0, row);
        layout.Controls.Add(portPanel, 1, row++);
        UpdatePortStatus();

        // NirSoft tools status
        _soundVolumeViewLabel = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
        var svvPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true };
        svvPanel.Controls.Add(_soundVolumeViewLabel);
        svvPanel.Controls.Add(MakeHelpIcon(_toolTip,
            "NirSoft SoundVolumeView — required for audio device switching.\n" +
            "Must be placed in the configured ToolsPath directory.\n" +
            "Download: nirsoft.net/utils/sound_volume_view.html"));
        layout.Controls.Add(MakeLabel("SoundVolumeView:"), 0, row);
        layout.Controls.Add(svvPanel, 1, row++);

        UpdateToolStatus(_soundVolumeViewLabel, Path.Combine(options.ToolsPath, "SoundVolumeView.exe"));

        // Display switching mode
        _displaySwitchingCombo = TabHelpers.MakeComboBox();
        _displaySwitchingCombo.Items.AddRange(Enum.GetNames<DisplaySwitchingMode>());
        _displaySwitchingCombo.SelectedItem = options.DisplaySwitching.ToString();
        var displaySwitchingPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true };
        displaySwitchingPanel.Controls.Add(_displaySwitchingCombo);
        displaySwitchingPanel.Controls.Add(MakeHelpIcon(_toolTip,
            "How monitor changes are applied.\n" +
            "Direct: single API call (fastest, works on most hardware)\n" +
            "Compatible: sequential steps with verification (use if Direct fails)"));
        layout.Controls.Add(MakeLabel("Display Switching:"), 0, row);
        layout.Controls.Add(displaySwitchingPanel, 1, row++);

        // Separator
        layout.Controls.Add(new Label { AutoSize = true, Height = 10 }, 0, row++);

        // Log level
        _logLevelCombo = TabHelpers.MakeComboBox();
        _logLevelCombo.Items.AddRange(["Error", "Warning", "Info", "Verbose"]);

        var settings = TraySettings.Load();
        _logLevelCombo.SelectedItem = settings.LogLevel switch
        {
            "Error" => "Error",
            "Info" => "Info",
            "Verbose" => "Verbose",
            _ => "Warning"
        };
        var logPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true };
        logPanel.Controls.Add(_logLevelCombo);
        logPanel.Controls.Add(MakeHelpIcon(_toolTip,
            "Controls how much detail is written to the log.\n" +
            "Error: only failures\n" +
            "Warning: failures and unexpected conditions\n" +
            "Info: normal operational events (recommended)\n" +
            "Verbose: full request/response detail (for debugging)"));
        layout.Controls.Add(MakeLabel("Log Level:"), 0, row);
        layout.Controls.Add(logPanel, 1, row++);

        // Auto-update
        _autoUpdateCheck = new CheckBox
        {
            Text = "Auto Update",
            ForeColor = Color.White,
            AutoSize = true,
            Checked = settings.AutoUpdate
        };
        _includePrereleasesCheck = new CheckBox
        {
            Text = "Include Prereleases",
            ForeColor = Color.White,
            AutoSize = true,
            Checked = settings.IncludePrereleases
        };

        var autoUpdatePanel = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true };
        autoUpdatePanel.Controls.Add(_autoUpdateCheck);
        autoUpdatePanel.Controls.Add(_includePrereleasesCheck);
        autoUpdatePanel.Controls.Add(MakeHelpIcon(_toolTip,
            "Auto Update: Automatically download and install new service releases from GitHub.\n" +
            "Include Prereleases: Also check for pre-release versions (e.g. v1.7.0-rc.1)."));

        layout.Controls.Add(new Label { AutoSize = true }, 0, row);
        layout.Controls.Add(autoUpdatePanel, 1, row++);

        Controls.Add(layout);
    }

    public IEnumerable<Button> CreateFooterButtons()
    {
        var applyButton = TabFooter.MakeSaveButton("Apply");
        var discardButton = TabFooter.MakeCancelButton("Discard");
        applyButton.Click += OnApply;
        discardButton.Click += OnCancel;
        return [applyButton, discardButton];
    }

    private void UpdatePortStatus()
    {
        if (KestrelStatus.Started.IsCompleted)
        {
            ApplyPortStatus();
            return;
        }

        _portStatusLabel.Text = "starting...";
        _portStatusLabel.ForeColor = Color.Orange;

        _ = Task.Run(async () =>
        {
            await KestrelStatus.Started;
            BeginInvoke(ApplyPortStatus);
        });
    }

    private void ApplyPortStatus()
    {
        if (KestrelStatus.IsRunning)
        {
            _portStatusLabel.Text = "listening";
            _portStatusLabel.ForeColor = Color.LightGreen;
        }
        else
        {
            _portStatusLabel.Text = $"failed: {KestrelStatus.Error}";
            _portStatusLabel.ForeColor = Color.Salmon;
            _portSaveButton.Visible = true;
        }
    }

    private async void OnPortSave(object? sender, EventArgs e)
    {
        var newPort = (int)_portInput.Value;
        if (MessageBox.Show(
                $"Change port to {newPort}?\nThe service will restart in-process — no process relaunch needed.",
                "Confirm Port Change",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        _portSaveButton.Enabled = false;
        _portStatusLabel.Text = "restarting...";
        _portStatusLabel.ForeColor = Color.Orange;

        try
        {
            var restart = _restartService.RestartAsync
                ?? throw new InvalidOperationException("RestartAsync delegate not set.");

            await Task.Run(() => restart(newPort));

            // Wait for Kestrel to report its new status
            _ = Task.Run(async () =>
            {
                await KestrelStatus.Started;
                BeginInvoke(() =>
                {
                    ApplyPortStatus();
                    _portSaveButton.Visible = false;
                    _portSaveButton.Enabled = true;
                });
            });
        }
        catch (Exception ex)
        {
            _portStatusLabel.Text = $"failed: {ex.Message}";
            _portStatusLabel.ForeColor = Color.Salmon;
            _portSaveButton.Enabled = true;
        }
    }

    private static void UpdateToolStatus(Label label, string toolPath)
    {
        if (File.Exists(toolPath))
        {
            label.Text = "Found";
            label.ForeColor = Color.LightGreen;
        }
        else
        {
            label.Text = $"Missing — expected at {toolPath}";
            label.ForeColor = Color.Salmon;
        }
    }

    private static Label MakeLabel(string text) => TabHelpers.MakeLabel(text, new Padding(0, 3, 0, 0));

    private static Label MakeHelpIcon(ToolTip toolTip, string helpText) => TabHelpers.MakeHelpIcon(toolTip, helpText);

    private void OnApply(object? sender, EventArgs e)
    {
        var level = _logLevelCombo.SelectedItem?.ToString() ?? "Warning";
        var logLevel = TabHelpers.ParseLogLevel(level);

        InMemoryLogProvider.MinimumLevel = logLevel;
        FileLoggerProvider.MinimumLevel = logLevel;

        var s = TraySettings.Load();
        s.LogLevel = level;
        s.AutoUpdate = _autoUpdateCheck.Checked;
        s.IncludePrereleases = _includePrereleasesCheck.Checked;
        s.Save();

        var displayMode = Enum.TryParse<DisplaySwitchingMode>(_displaySwitchingCombo.SelectedItem?.ToString(), out var dm)
            ? dm : DisplaySwitchingMode.Direct;
        _configWriter.SaveDisplaySwitching(displayMode);
    }

    private void OnCancel(object? sender, EventArgs e)
    {
        var s = TraySettings.Load();
        _logLevelCombo.SelectedItem = s.LogLevel switch
        {
            "Error" => "Error",
            "Info" => "Info",
            "Verbose" => "Verbose",
            _ => "Warning"
        };
        _autoUpdateCheck.Checked = s.AutoUpdate;
        _includePrereleasesCheck.Checked = s.IncludePrereleases;

        _displaySwitchingCombo.SelectedItem = _configWriter.Read().DisplaySwitching.ToString();
    }
}
