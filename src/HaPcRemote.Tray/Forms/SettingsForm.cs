using HaPcRemote.Service.Configuration;
using HaPcRemote.Service.Logging;
using HaPcRemote.Service.Services;
using HaPcRemote.Tray.Logging;
using HaPcRemote.Tray.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HaPcRemote.Tray.Forms;

internal sealed class SettingsForm : Form
{
    private readonly TabControl _tabControl;
    private readonly GeneralTab _generalTab;
    private readonly ModesTab _modesTab;
    private readonly GamesTab _gamesTab;
    private readonly PowerTab _powerTab;
    private readonly LogTab _logTab;
    private readonly TabFooter _footer;

    public SettingsForm(
        IServiceProvider services,
        InMemoryLogProvider logProvider)
    {
        var version = UpdateService.GetCurrentVersion();
        var versionStr = version is not null ? $" v{UpdateService.FormatVersion(version)}" : "";
        Text = $"HA PC Remote{versionStr} - Settings";
        MinimumSize = new Size(600, 450);

        var settings = TraySettings.Load();
        var scaleFactor = DeviceDpi / 96f;
        Size = settings.SettingsWidth > 0 && settings.SettingsHeight > 0
            ? new Size((int)(settings.SettingsWidth * scaleFactor), (int)(settings.SettingsHeight * scaleFactor))
            : new Size(750, 550);
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application;
        BackColor = Color.FromArgb(30, 30, 30);
        ForeColor = Color.White;

        _tabControl = new TabControl
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9.5f),
        };

        var port = services.GetRequiredService<IOptionsMonitor<PcRemoteOptions>>().CurrentValue.Port;

        _generalTab = new GeneralTab(services);
        _modesTab = new ModesTab(services);
        _gamesTab = new GamesTab(services);
        _powerTab = new PowerTab(services);
        _logTab = new LogTab(logProvider, port);

        _tabControl.TabPages.Add(_generalTab);
        _tabControl.TabPages.Add(_modesTab);
        _tabControl.TabPages.Add(_gamesTab);
        _tabControl.TabPages.Add(_powerTab);
        _tabControl.TabPages.Add(_logTab);

        // Shared footer — buttons swap when the selected tab changes
        _footer = new TabFooter();
        _tabControl.SelectedIndexChanged += (_, _) => SyncFooter();

        _gamesTab.SetFooter(_footer);
        _powerTab.SetFooter(_footer);

        Controls.Add(_tabControl);
        Controls.Add(_footer);

        SyncFooter();
    }

    private void SyncFooter()
    {
        if (_tabControl.SelectedTab is ISettingsTab tab)
        {
            _footer.SetButtons(tab.CreateFooterButtons());
            _footer.Visible = true;
        }
        else
        {
            _footer.Visible = false;
        }
    }

    /// <summary>Show the form with a specific tab selected.</summary>
    public void ShowTab(int tabIndex)
    {
        if (tabIndex >= 0 && tabIndex < _tabControl.TabCount)
            _tabControl.SelectedIndex = tabIndex;
        Show();
        BringToFront();
        Activate();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;

            var s = TraySettings.Load();
            var scaleFactor = DeviceDpi / 96f;
            s.SettingsWidth = (int)(Width / scaleFactor);
            s.SettingsHeight = (int)(Height / scaleFactor);
            s.Save();

            Hide();
        }
    }
}
