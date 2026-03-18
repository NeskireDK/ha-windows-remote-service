using HaPcRemote.Service.Configuration;
using HaPcRemote.Service.Services;
using Microsoft.Extensions.Options;

namespace HaPcRemote.Tray.Forms;

internal sealed class ModesTab : TabPage
{
    private readonly IConfigurationWriter _configWriter;
    private readonly IAudioService _audioService;
    private readonly IMonitorService _monitorService;
    private readonly IOptions<PcRemoteOptions> _options;

    private readonly ListBox _modeList;
    private readonly TextBox _modeNameBox;
    private readonly ComboBox _audioDeviceCombo;
    private readonly ComboBox _soloMonitorCombo;
    private readonly TrackBar _volumeSlider;
    private readonly Label _volumeLabel;
    private readonly ComboBox _launchAppCombo;
    private readonly ComboBox _killAppCombo;
    private readonly Button _deleteButton;
    private readonly Button _newButton;
    private readonly ToolTip _toolTip = new();

    private const string NewModePlaceholder = "(new mode)";
    private bool _hasPendingNew;

    public ModesTab(IServiceProvider services)
    {
        Text = "PC Modes";
        BackColor = Color.FromArgb(30, 30, 30);
        ForeColor = Color.White;
        Padding = new Padding(10);

        _configWriter = services.GetRequiredService<IConfigurationWriter>();
        _audioService = services.GetRequiredService<IAudioService>();
        _monitorService = services.GetRequiredService<IMonitorService>();
        _options = services.GetRequiredService<IOptions<PcRemoteOptions>>();

        // Left panel: mode list + buttons
        var leftPanel = new Panel { Dock = DockStyle.Left, Width = 180, Padding = new Padding(0, 0, 10, 0) };

        _modeList = new ListBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(45, 45, 45),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 9.5f)
        };
        _modeList.SelectedIndexChanged += OnModeSelected;

        var listButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 35,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = false
        };
        _newButton = CreateButton("New");
        _newButton.Click += OnNewMode;
        _deleteButton = CreateButton("Delete");
        _deleteButton.Click += OnDeleteMode;
        listButtons.Controls.Add(_newButton);
        listButtons.Controls.Add(_deleteButton);

        leftPanel.Controls.Add(_modeList);
        leftPanel.Controls.Add(listButtons);

        // Right panel: mode editor
        var rightPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 0, 0, 0) };
        var editLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            Padding = new Padding(0)
        };
        editLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        editLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        int row = 0;

        // Mode name
        _modeNameBox = new TextBox { BackColor = Color.FromArgb(50, 50, 50), ForeColor = Color.White, Width = 250, BorderStyle = BorderStyle.FixedSingle };
        editLayout.Controls.Add(MakeLabel("Name:"), 0, row);
        editLayout.Controls.Add(WithHelp(_modeNameBox, _toolTip, "Unique identifier for this mode.\nUsed in HA automations and the PC Mode select entity.\nExample: couch, desktop"), 1, row++);

        // Audio device (with "Don't change" option)
        _audioDeviceCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 250,
            BackColor = Color.FromArgb(50, 50, 50),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        editLayout.Controls.Add(MakeLabel("Audio Device:"), 0, row);
        editLayout.Controls.Add(WithHelp(_audioDeviceCombo, _toolTip, "Audio output to switch to when this mode is activated.\nSelect \"(Don't change)\" to leave the current device untouched."), 1, row++);

        // Solo monitor
        _soloMonitorCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 250,
            BackColor = Color.FromArgb(50, 50, 50),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        editLayout.Controls.Add(MakeLabel("Solo Monitor:"), 0, row);
        editLayout.Controls.Add(WithHelp(_soloMonitorCombo, _toolTip, "Monitor to keep as sole active display. Disables all others."), 1, row++);

        // Volume
        var volumePanel = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true };
        _volumeSlider = new TrackBar { Minimum = 0, Maximum = 100, Width = 200, TickFrequency = 10, Value = 50 };
        _volumeLabel = new Label { Text = "50", ForeColor = Color.White, AutoSize = true, Padding = new Padding(5, 5, 0, 0) };
        _volumeSlider.ValueChanged += (_, _) => _volumeLabel.Text = _volumeSlider.Value.ToString();
        volumePanel.Controls.Add(_volumeSlider);
        volumePanel.Controls.Add(_volumeLabel);
        volumePanel.Controls.Add(MakeHelpIcon(_toolTip, "System volume (0–100) to set when this mode is activated."));
        editLayout.Controls.Add(MakeLabel("Volume:"), 0, row);
        editLayout.Controls.Add(volumePanel, 1, row++);

        // Launch app
        _launchAppCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDown,
            Width = 250,
            BackColor = Color.FromArgb(50, 50, 50),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        editLayout.Controls.Add(MakeLabel("Launch App:"), 0, row);
        editLayout.Controls.Add(WithHelp(_launchAppCombo, _toolTip, "App to launch when this mode is activated.\nApps are defined in the Apps config section."), 1, row++);

        // Kill app
        _killAppCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDown,
            Width = 250,
            BackColor = Color.FromArgb(50, 50, 50),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        editLayout.Controls.Add(MakeLabel("Kill App:"), 0, row);
        editLayout.Controls.Add(WithHelp(_killAppCombo, _toolTip, "App to terminate when this mode is activated.\nUseful for killing Steam Big Picture when switching to desktop mode."), 1, row++);

        rightPanel.Controls.Add(editLayout);

        var saveButton = TabFooter.MakeSaveButton("Save Mode", 100);
        saveButton.Click += OnSaveMode;
        var footer = new TabFooter();
        footer.Add(saveButton);

        Controls.Add(rightPanel);
        Controls.Add(leftPanel);
        Controls.Add(footer);

        LoadModes();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _launchAppCombo.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        _launchAppCombo.AutoCompleteSource = AutoCompleteSource.CustomSource;
        _killAppCombo.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        _killAppCombo.AutoCompleteSource = AutoCompleteSource.CustomSource;
    }

    protected override async void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible) await RefreshDropdownsAsync();
    }

    private async Task RefreshDropdownsAsync()
    {
        try
        {
            _audioDeviceCombo.Items.Clear();
            _audioDeviceCombo.Items.Add("(Don't change)");
            var devices = await _audioService.GetDevicesAsync();
            foreach (var d in devices)
                _audioDeviceCombo.Items.Add(d.Name);

            _soloMonitorCombo.Items.Clear();
            _soloMonitorCombo.Items.Add(new MonitorDropdownItem(null, "(Don't change)"));
            var monitors = await _monitorService.GetMonitorsAsync();
            foreach (var m in monitors)
                _soloMonitorCombo.Items.Add(new MonitorDropdownItem(m.MonitorId, $"{m.Name} ({m.MonitorId})"));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to refresh dropdowns: {ex.Message}");
        }

        RefreshAppDropdowns();
    }

    private static readonly string[] BuiltInAppKeys = ["steam", "steam-bigpicture"];

    private void RefreshAppDropdowns()
    {
        var apps = _options.Value.Apps;

        _launchAppCombo.Items.Clear();
        _launchAppCombo.Items.Add(new AppDropdownItem(null, "(None)"));
        _killAppCombo.Items.Clear();
        _killAppCombo.Items.Add(new AppDropdownItem(null, "(None)"));

        var autoCompleteKeys = new AutoCompleteStringCollection();

        foreach (var (key, app) in apps)
        {
            var item = new AppDropdownItem(key, app.DisplayName);
            _launchAppCombo.Items.Add(item);
            _killAppCombo.Items.Add(item);
            autoCompleteKeys.Add(key);
        }

        foreach (var builtIn in BuiltInAppKeys)
        {
            if (!apps.ContainsKey(builtIn))
                autoCompleteKeys.Add(builtIn);
        }

        _launchAppCombo.AutoCompleteCustomSource = autoCompleteKeys;
        _killAppCombo.AutoCompleteCustomSource = autoCompleteKeys;
    }

    private static void SelectAppItem(ComboBox combo, string? appKey)
    {
        if (string.IsNullOrEmpty(appKey))
        {
            combo.SelectedIndex = 0;
            return;
        }

        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is AppDropdownItem item && item.Key == appKey)
            {
                combo.SelectedIndex = i;
                return;
            }
        }

        // Free-text key not in list — set text directly
        combo.SelectedIndex = -1;
        combo.Text = appKey;
    }

    private static string? GetSelectedAppKey(ComboBox combo)
    {
        if (combo.SelectedItem is AppDropdownItem item)
            return item.Key;

        // Free-text path
        var text = combo.Text.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private void LoadModes()
    {
        _modeList.Items.Clear();
        var options = _configWriter.Read();
        foreach (var name in options.Modes.Keys)
            _modeList.Items.Add(name);
    }

    private void OnModeSelected(object? sender, EventArgs e)
    {
        if (_modeList.SelectedItem is not string name) return;

        if (name == NewModePlaceholder)
        {
            // Blank fields already set when placeholder was added — nothing to load
            return;
        }

        // Selecting a real mode — discard the pending new placeholder
        if (_hasPendingNew)
            DiscardPendingNew();

        var options = _configWriter.Read();
        if (!options.Modes.TryGetValue(name, out var mode)) return;

        _modeNameBox.Text = name;
        _audioDeviceCombo.SelectedItem = mode.AudioDevice ?? "(Don't change)";
        SelectMonitorItem(_soloMonitorCombo, mode.SoloMonitor);
        _volumeSlider.Value = mode.Volume ?? 50;
        SelectAppItem(_launchAppCombo, mode.LaunchApp);
        SelectAppItem(_killAppCombo, mode.KillApp);
    }

    private void DiscardPendingNew()
    {
        _hasPendingNew = false;
        var idx = _modeList.Items.IndexOf(NewModePlaceholder);
        if (idx >= 0)
            _modeList.Items.RemoveAt(idx);
    }

    private void OnNewMode(object? sender, EventArgs e)
    {
        // Remove any pre-existing placeholder before adding a fresh one
        if (_hasPendingNew)
            DiscardPendingNew();

        // Insert placeholder row and select it
        _modeList.Items.Add(NewModePlaceholder);
        _hasPendingNew = true;
        _modeList.SelectedItem = NewModePlaceholder;

        ResetEditorFields();
        _modeNameBox.Focus();
    }

    private void OnSaveMode(object? sender, EventArgs e)
    {
        var name = _modeNameBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            MessageBox.Show("Mode name is required.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var mode = new ModeConfig
        {
            AudioDevice = _audioDeviceCombo.SelectedItem?.ToString() is "(Don't change)" ? null : _audioDeviceCombo.SelectedItem?.ToString(),
            SoloMonitor = (_soloMonitorCombo.SelectedItem as MonitorDropdownItem)?.MonitorId,
            Volume = _volumeSlider.Value,
            LaunchApp = GetSelectedAppKey(_launchAppCombo),
            KillApp = GetSelectedAppKey(_killAppCombo)
        };

        var selectedItem = _modeList.SelectedItem as string;

        // If renaming an existing mode (not the placeholder), use atomic rename; otherwise just save
        if (selectedItem is not null && selectedItem != NewModePlaceholder && selectedItem != name)
            _configWriter.RenameMode(selectedItem, name, mode);
        else
            _configWriter.SaveMode(name, mode);

        _hasPendingNew = false;
        LoadModes();

        // Re-select the saved mode
        var idx = _modeList.Items.IndexOf(name);
        if (idx >= 0) _modeList.SelectedIndex = idx;
    }

    private void OnDeleteMode(object? sender, EventArgs e)
    {
        if (_modeList.SelectedItem is not string name) return;
        if (name == NewModePlaceholder)
        {
            // Discard the uncommitted placeholder row
            DiscardPendingNew();
            ResetEditorFields();
            return;
        }

        if (MessageBox.Show($"Delete mode '{name}'?", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        _hasPendingNew = false;
        _configWriter.DeleteMode(name);
        LoadModes();
        ResetEditorFields();
    }

    private void ResetEditorFields()
    {
        _modeNameBox.Text = "";
        if (_audioDeviceCombo.Items.Count > 0) _audioDeviceCombo.SelectedIndex = 0;
        if (_soloMonitorCombo.Items.Count > 0) _soloMonitorCombo.SelectedIndex = 0;
        _volumeSlider.Value = 50;
        if (_launchAppCombo.Items.Count > 0) _launchAppCombo.SelectedIndex = 0;
        if (_killAppCombo.Items.Count > 0) _killAppCombo.SelectedIndex = 0;
    }

    private static Label MakeLabel(string text) => new()
    {
        Text = text,
        ForeColor = Color.White,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Padding = new Padding(0, 6, 0, 0)
    };

    private static FlowLayoutPanel WithHelp(Control control, ToolTip toolTip, string helpText)
    {
        var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true };
        panel.Controls.Add(control);
        panel.Controls.Add(MakeHelpIcon(toolTip, helpText));
        return panel;
    }

    private static Label MakeHelpIcon(ToolTip toolTip, string helpText)
    {
        var label = new Label
        {
            Text = "ⓘ",
            ForeColor = Color.FromArgb(120, 180, 255),
            AutoSize = true,
            Cursor = Cursors.Help,
            Padding = new Padding(4, 5, 0, 0),
            Font = new Font("Segoe UI", 9f)
        };
        toolTip.SetToolTip(label, helpText);
        label.Click += (_, _) => toolTip.Show(helpText, label, 3000);
        return label;
    }

    private static Button CreateButton(string text) => new()
    {
        Text = text,
        FlatStyle = FlatStyle.Flat,
        BackColor = Color.FromArgb(50, 50, 50),
        ForeColor = Color.White,
        Size = new Size(75, 28),
        Cursor = Cursors.Hand
    };

    private static void SelectMonitorItem(ComboBox combo, string? monitorId)
    {
        if (string.IsNullOrEmpty(monitorId))
        {
            combo.SelectedIndex = 0;
            return;
        }

        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is MonitorDropdownItem item && item.MonitorId == monitorId)
            {
                combo.SelectedIndex = i;
                return;
            }
        }

        combo.SelectedIndex = 0;
    }

    private sealed class AppDropdownItem(string? key, string displayName)
    {
        public string? Key { get; } = key;
        public override string ToString() => displayName;
    }

    private sealed class MonitorDropdownItem(string? monitorId, string displayName)
    {
        public string? MonitorId { get; } = monitorId;
        public override string ToString() => displayName;
    }
}
