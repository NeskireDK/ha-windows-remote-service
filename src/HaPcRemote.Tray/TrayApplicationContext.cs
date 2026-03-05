using System.Diagnostics;
using HaPcRemote.Service.Logging;
using HaPcRemote.Service.Services;
using HaPcRemote.Service.Configuration;
using HaPcRemote.Tray.Forms;
using HaPcRemote.Tray.Logging;
using HaPcRemote.Tray.Models;
using HaPcRemote.Tray.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HaPcRemote.Tray;

/// <summary>
/// WinForms application context. Hosts the system tray icon and log viewer.
/// Kestrel runs alongside via TrayWebHost.
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private static readonly string VersionString = GetVersionString();

    private readonly NotifyIcon _notifyIcon;
    private readonly CancellationTokenSource _cts = new();
    private readonly CancellationTokenSource _webCts;
    private readonly ILogger _logger;
    private readonly InMemoryLogProvider _logProvider;
    private readonly UpdateChecker _updateChecker;
    private readonly System.Windows.Forms.Timer _updateTimer;
    private readonly string _profilesPath;
    private readonly int _port;
    private readonly Func<IServiceProvider> _serviceAccessor;
    private readonly System.Windows.Forms.Timer _steamPollTimer;
    private readonly Icon _defaultIcon;
    private Icon? _playingIcon;
    private bool _isGamePlaying;

    private readonly SemaphoreSlim _updateLock = new(1, 1);

    private SettingsForm? _settingsForm;
    private ToolStripMenuItem? _updateMenuItem;
    private ToolStripMenuItem? _autoUpdateMenuItem;
    private UpdateChecker.ReleaseInfo? _pendingRelease;

    public TrayApplicationContext(Func<IServiceProvider> serviceAccessor, CancellationTokenSource webCts, InMemoryLogProvider logProvider)
    {
        _webCts = webCts;
        _logProvider = logProvider;
        _serviceAccessor = serviceAccessor;

        var webServices = serviceAccessor();
        var loggerFactory = webServices.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger<TrayApplicationContext>();

        var options = webServices.GetRequiredService<IOptions<PcRemoteOptions>>().Value;
        _profilesPath = options.ProfilesPath;
        _port = options.Port;

        _updateChecker = new UpdateChecker(loggerFactory.CreateLogger<UpdateChecker>());

        var settings = TraySettings.Load();
        var logLevel = ParseLogLevel(settings.LogLevel);
        InMemoryLogProvider.MinimumLevel = logLevel;
        FileLoggerProvider.MinimumLevel = logLevel;

        var appIcon = LoadAppIcon();
        _defaultIcon = appIcon;

        _notifyIcon = new NotifyIcon
        {
            Icon = appIcon,
            Text = $"HA PC Remote {VersionString}",
            Visible = true,
            ContextMenuStrip = BuildContextMenu(settings)
        };
        _notifyIcon.DoubleClick += OnShowSettings;

        _steamPollTimer = new System.Windows.Forms.Timer { Interval = 10_000 };
        _steamPollTimer.Tick += OnSteamPollTick;
        _steamPollTimer.Start();

        // Check for updates after 30s, then on timer
        _updateTimer = new System.Windows.Forms.Timer { Interval = GetUpdateTimerInterval(settings.AutoUpdate) };
        _updateTimer.Tick += async (_, _) => await SafeCheckForUpdateAsync(showProgress: false);
        _updateTimer.Start();
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), _cts.Token);
            _notifyIcon.ContextMenuStrip!.BeginInvoke(async () => await SafeCheckForUpdateAsync());
        });

        _logger.LogInformation("HA PC Remote Tray {Version} started", VersionString);
        _logger.LogInformation("Profiles path: {ProfilesPath}", _profilesPath);
        _logger.LogInformation("Tools path: {ToolsPath}", options.ToolsPath);
    }

    private ContextMenuStrip BuildContextMenu(TraySettings settings)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add($"HA PC Remote {VersionString}", null, null!).Enabled = false;
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings", null, OnShowSettings);
        menu.Items.Add("Show Log", null, OnShowLog);
        menu.Items.Add("Show API Key", null, OnShowApiKey);
        menu.Items.Add("Open Profiles Folder", null, OnOpenProfilesFolder);
        menu.Items.Add("API Explorer", null, OnOpenApiExplorer);

        menu.Items.Add(new ToolStripSeparator());

        _updateMenuItem = new ToolStripMenuItem("Check for updates");
        _updateMenuItem.Click += OnCheckForUpdatesClick;
        menu.Items.Add(_updateMenuItem);

        _autoUpdateMenuItem = new ToolStripMenuItem("Auto Update") { CheckOnClick = true, Checked = settings.AutoUpdate };
        _autoUpdateMenuItem.CheckedChanged += OnAutoUpdateToggled;
        menu.Items.Add(_autoUpdateMenuItem);

        menu.Items.Add("Exit", null, OnExit);
        return menu;
    }

    private void OnShowSettings(object? sender, EventArgs e)
    {
        EnsureSettingsForm();
        _settingsForm?.ShowTab(0); // General tab
    }

    private void OnShowLog(object? sender, EventArgs e)
    {
        EnsureSettingsForm();
        _settingsForm?.ShowTab(4); // Log tab
    }

    private void EnsureSettingsForm()
    {
        if (_settingsForm is { IsDisposed: false })
            return;

        _settingsForm?.Dispose();

        try
        {
            _settingsForm = new SettingsForm(_serviceAccessor(), _logProvider);
        }
        catch (ObjectDisposedException)
        {
            _settingsForm = null;
            MessageBox.Show(
                "The service is restarting. Please try again in a moment.",
                "HA PC Remote",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }

    private void OnShowApiKey(object? sender, EventArgs e)
    {
        using var dialog = new ApiKeyDialog();
        dialog.ShowDialog();
    }

    private void OnOpenProfilesFolder(object? sender, EventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_profilesPath);
            Process.Start(new ProcessStartInfo("explorer.exe", _profilesPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to open profiles folder: {Path}", _profilesPath);
        }
    }

    private void OnOpenApiExplorer(object? sender, EventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo($"http://localhost:{_port}/api-explorer") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to open API Explorer");
        }
    }

    private async void OnSteamPollTick(object? sender, EventArgs e)
    {
        try
        {
            var steamService = _serviceAccessor().GetRequiredService<ISteamService>();
            var running = await steamService.GetRunningGameAsync();
            var isPlaying = running != null;
            if (isPlaying == _isGamePlaying) return;
            _isGamePlaying = isPlaying;
            _notifyIcon.Icon = isPlaying ? GetPlayingIcon() : _defaultIcon;
            _notifyIcon.Text = isPlaying
                ? $"HA PC Remote {VersionString} — {running!.Name}"
                : $"HA PC Remote {VersionString}";
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Steam poll error");
        }
    }

    private Icon GetPlayingIcon()
    {
        if (_playingIcon != null) return _playingIcon;
        var bmp = _defaultIcon.ToBitmap();
        using var g = Graphics.FromImage(bmp);
        using var brush = new SolidBrush(Color.Lime);
        g.FillEllipse(brush, bmp.Width - 7, bmp.Height - 7, 6, 6);
        _playingIcon = Icon.FromHandle(bmp.GetHicon());
        bmp.Dispose();
        return _playingIcon;
    }

    private void OnAutoUpdateToggled(object? sender, EventArgs e)
    {
        var s = TraySettings.Load();
        s.AutoUpdate = _autoUpdateMenuItem!.Checked;
        s.Save();
        _updateTimer.Interval = GetUpdateTimerInterval(s.AutoUpdate);
        _logger.LogInformation("Auto update {State}", s.AutoUpdate ? "enabled" : "disabled");
    }

    private static LogLevel ParseLogLevel(string level) => level switch
    {
        "Error"   => LogLevel.Error,
        "Info"    => LogLevel.Information,
        "Verbose" => LogLevel.Debug,
        _         => LogLevel.Warning
    };

    private static int GetUpdateTimerInterval(bool autoUpdate)
        => autoUpdate ? 5 * 60 * 1000 : 4 * 60 * 60 * 1000;

    private async void OnCheckForUpdatesClick(object? sender, EventArgs e)
    {
        if (_pendingRelease is not null)
            await HandleDownloadAsync(_pendingRelease);
        else
            await SafeCheckForUpdateAsync(showProgress: true);
    }

    private async Task SafeCheckForUpdateAsync(bool showProgress = false)
    {
        try
        {
            await CheckForUpdateAsync(showProgress);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update check failed unexpectedly");
            if (showProgress && _updateMenuItem is not null)
            {
                _updateMenuItem.Text = "Check for updates";
                _updateMenuItem.Enabled = true;
            }
        }
    }

    private async Task CheckForUpdateAsync(bool showProgress = false)
    {
        if (_updateMenuItem is null) return;

        if (showProgress)
        {
            _updateMenuItem.Enabled = false;
            _updateMenuItem.Text = "Checking...";
        }

        var release = await _updateChecker.CheckAsync(_cts.Token);

        if (release is null)
        {
            if (showProgress)
            {
                _updateMenuItem.Text = "Up to date";
                var resetTimer = new System.Windows.Forms.Timer { Interval = 3000 };
                resetTimer.Tick += (_, _) =>
                {
                    resetTimer.Stop();
                    resetTimer.Dispose();
                    if (_updateMenuItem is not null && _pendingRelease is null)
                    {
                        _updateMenuItem.Text = "Check for updates";
                        _updateMenuItem.Enabled = true;
                    }
                };
                resetTimer.Start();
            }
            return;
        }

        _pendingRelease = release;
        _updateMenuItem.Text = $"Update to {release.TagName}";
        _updateMenuItem.Enabled = true;

        _notifyIcon.ShowBalloonTip(
            5000,
            "Update Available",
            $"HA PC Remote {release.TagName} is available. Right-click the tray icon to update.",
            ToolTipIcon.Info);

        if (_autoUpdateMenuItem?.Checked == true)
        {
            var installTimer = new System.Windows.Forms.Timer { Interval = 5000 };
            installTimer.Tick += async (_, _) =>
            {
                installTimer.Stop();
                installTimer.Dispose();
                if (_pendingRelease is not null)
                    await HandleDownloadAsync(_pendingRelease);
            };
            installTimer.Start();
        }
    }

    private async Task HandleDownloadAsync(UpdateChecker.ReleaseInfo release)
    {
        if (_updateMenuItem is null) return;

        if (!_updateLock.Wait(0))
        {
            _logger.LogInformation("Update already in progress, skipping");
            return;
        }

        try
        {
            _updateMenuItem.Enabled = false;
            _updateMenuItem.Text = "Updating…";

            if (await _updateChecker.DownloadAndInstallAsync(release, _cts.Token))
            {
                Application.Exit();
            }
            else
            {
                _updateMenuItem.Text = $"Update to {release.TagName}";
                _updateMenuItem.Enabled = true;
            }
        }
        finally
        {
            _updateLock.Release();
        }
    }

    private void OnExit(object? sender, EventArgs e)
    {
        _logger.LogInformation("Shutting down...");
        _cts.Cancel();
        _webCts.Cancel();
        _notifyIcon.Visible = false;
        Application.Exit();
    }

    private static string GetVersionString()
    {
        var version = UpdateChecker.GetCurrentVersion();
        return version is null ? "" : $"v{version.ToString(3)}";
    }

    private static Icon LoadAppIcon()
    {
        return Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cts.Cancel();
            _cts.Dispose();
            _updateLock.Dispose();
            _updateTimer.Dispose();
            _steamPollTimer.Dispose();
            _playingIcon?.Dispose();
            _settingsForm?.Dispose();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}
