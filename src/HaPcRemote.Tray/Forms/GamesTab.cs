using HaPcRemote.Service.Configuration;
using HaPcRemote.Service.Services;
using Microsoft.Extensions.Options;

namespace HaPcRemote.Tray.Forms;

internal sealed class GamesTab : TabPage, ISettingsTab
{
    private readonly IConfigurationWriter _configWriter;
    private readonly ISteamService _steamService;
    private readonly IModeService _modeService;
    private readonly IOptionsMonitor<PcRemoteOptions> _options;

    private readonly ComboBox _defaultModeCombo;
    private readonly DataGridView _gameGrid;
    private readonly ToolTip _toolTip = new();
    private TabFooter? _footer;

    public GamesTab(IServiceProvider services)
    {
        Text = "Games";
        BackColor = Color.FromArgb(30, 30, 30);
        ForeColor = Color.White;
        Padding = new Padding(10);

        _configWriter = services.GetRequiredService<IConfigurationWriter>();
        _steamService = services.GetRequiredService<ISteamService>();
        _modeService = services.GetRequiredService<IModeService>();
        _options = services.GetRequiredService<IOptionsMonitor<PcRemoteOptions>>();

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 2,
            Padding = new Padding(0)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // Default PC mode
        _defaultModeCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 200,
            BackColor = Color.FromArgb(50, 50, 50),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        layout.Controls.Add(MakeLabel("Default PC Mode:"), 0, 0);
        var defaultModePanel = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true };
        defaultModePanel.Controls.Add(_defaultModeCombo);
        defaultModePanel.Controls.Add(MakeHelpIcon(_toolTip,
            "Mode applied automatically before launching any Steam game.\n" +
            "Set to \"(none)\" to disable automatic mode switching on game launch."));
        layout.Controls.Add(defaultModePanel, 1, 0);

        // Game grid
        _gameGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            BackgroundColor = Color.FromArgb(35, 35, 35),
            ForeColor = Color.White,
            GridColor = Color.FromArgb(60, 60, 60),
            DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
                SelectionBackColor = Color.FromArgb(70, 70, 70),
                SelectionForeColor = Color.White
            },
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(50, 50, 50),
                ForeColor = Color.White
            },
            EnableHeadersVisualStyles = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            EditMode = DataGridViewEditMode.EditOnEnter
        };

        var nameCol = new DataGridViewTextBoxColumn
        {
            Name = "Game",
            HeaderText = "Game",
            ReadOnly = true,
            FillWeight = 60
        };
        var appIdCol = new DataGridViewTextBoxColumn
        {
            Name = "AppId",
            HeaderText = "App ID",
            ReadOnly = true,
            FillWeight = 15
        };
        var modeCol = new DataGridViewComboBoxColumn
        {
            Name = "PcMode",
            HeaderText = "PC Mode",
            FillWeight = 25,
            FlatStyle = FlatStyle.Flat,
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton
        };

        _toolTip.SetToolTip(_gameGrid,
            "Per-game PC mode override.\n" +
            "(default) = use the Default PC Mode above\n" +
            "(none) = launch without switching modes\n" +
            "Any named mode = switch to that mode before launching this game");

        _gameGrid.Columns.AddRange(nameCol, appIdCol, modeCol);
        layout.Controls.Add(_gameGrid, 0, 1);
        layout.SetColumnSpan(_gameGrid, 2);

        Controls.Add(layout);
    }

    public IEnumerable<Button> CreateFooterButtons()
    {
        var applyButton = TabFooter.MakeSaveButton("Apply");
        var discardButton = TabFooter.MakeCancelButton("Discard");
        applyButton.Click += OnSave;
        discardButton.Click += async (_, _) => await RefreshAsync();
        return [applyButton, discardButton];
    }

    internal void SetFooter(TabFooter footer) => _footer = footer;

    protected override async void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible) await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        try
        {
            var modeNames = _modeService.GetModeNames();
            var modeOptions = new List<string> { "(default)", "(none)" };
            modeOptions.AddRange(modeNames);

            // Default mode combo
            _defaultModeCombo.Items.Clear();
            _defaultModeCombo.Items.Add("(none)");
            foreach (var m in modeNames)
                _defaultModeCombo.Items.Add(m);

            var steam = _options.CurrentValue.Steam;
            var defaultMode = steam.DefaultPcMode;
            if (string.IsNullOrEmpty(defaultMode) || defaultMode.Equals("none", StringComparison.OrdinalIgnoreCase))
                _defaultModeCombo.SelectedIndex = 0;
            else
                _defaultModeCombo.SelectedItem = defaultMode;

            // Game list
            var games = await _steamService.GetGamesAsync();
            var bindings = steam.GamePcModeBindings;

            _gameGrid.Rows.Clear();

            // Update mode column items
            var modeColumn = (DataGridViewComboBoxColumn)_gameGrid.Columns["PcMode"]!;
            modeColumn.Items.Clear();
            foreach (var opt in modeOptions)
                modeColumn.Items.Add(opt);

            foreach (var game in games)
            {
                var rowIdx = _gameGrid.Rows.Add(game.Name, game.AppId.ToString());
                var row = _gameGrid.Rows[rowIdx];

                if (bindings.TryGetValue(game.AppId.ToString(), out var bound) && !string.IsNullOrEmpty(bound))
                {
                    if (bound.Equals("none", StringComparison.OrdinalIgnoreCase))
                        row.Cells["PcMode"].Value = "(none)";
                    else
                        row.Cells["PcMode"].Value = bound;
                }
                else
                {
                    row.Cells["PcMode"].Value = "(default)";
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to refresh games tab: {ex.Message}");
        }
    }

    private void OnSave(object? sender, EventArgs e)
    {
        var defaultMode = _defaultModeCombo.SelectedItem?.ToString();
        if (defaultMode == "(none)")
            defaultMode = "";

        var bindings = new Dictionary<string, string>();
        foreach (DataGridViewRow row in _gameGrid.Rows)
        {
            var appId = row.Cells["AppId"].Value?.ToString();
            var mode = row.Cells["PcMode"].Value?.ToString();
            if (string.IsNullOrEmpty(appId))
                continue;

            if (mode == "(default)" || string.IsNullOrEmpty(mode))
                continue; // No binding = use default

            if (mode == "(none)")
                bindings[appId] = "none";
            else
                bindings[appId] = mode;
        }

        var steamConfig = new SteamConfig
        {
            DefaultPcMode = defaultMode ?? "",
            GamePcModeBindings = bindings
        };
        _configWriter.SaveSteamBindings(steamConfig);

        _footer?.ShowStatus("Saved");
    }

    private static Label MakeLabel(string text) => TabHelpers.MakeLabel(text);

    private static Label MakeHelpIcon(ToolTip toolTip, string helpText) => TabHelpers.MakeHelpIcon(toolTip, helpText);
}
