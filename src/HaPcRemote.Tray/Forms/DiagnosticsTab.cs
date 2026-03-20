using System.ComponentModel;
using HaPcRemote.Service.Native;
using HaPcRemote.Service.Services;
using static HaPcRemote.Service.Native.DisplayConfigApi;

namespace HaPcRemote.Tray.Forms;

internal sealed class DiagnosticsTab : TabPage, ISettingsTab
{
    private readonly IMonitorService _monitorService;
    private readonly IDisplayConfigApi _displayConfigApi;
    private readonly TextBox _outputBox;

    public DiagnosticsTab(IServiceProvider services)
    {
        Text = "Diagnostics";
        BackColor = Color.FromArgb(30, 30, 30);
        ForeColor = Color.White;

        _monitorService = services.GetRequiredService<IMonitorService>();
        _displayConfigApi = services.GetRequiredService<IDisplayConfigApi>();

        _outputBox = new TextBox
        {
            ReadOnly = true,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Color.FromArgb(20, 20, 20),
            ForeColor = Color.White,
            Font = new Font("Consolas", 9f),
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            WordWrap = false
        };

        Controls.Add(_outputBox);
    }

    public IEnumerable<Button> CreateFooterButtons()
    {
        var refreshButton = TabFooter.MakeButton("Refresh");
        refreshButton.Click += (_, _) => _ = LoadDataAsync();
        return [refreshButton];
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible) _ = LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        _outputBox.Text = "Loading...";

        var sb = new System.Text.StringBuilder();

        // Monitors from IMonitorService
        sb.AppendLine("=== Monitors (IMonitorService.GetMonitorsAsync) ===");
        try
        {
            var monitors = await _monitorService.GetMonitorsAsync();
            if (monitors.Count == 0)
            {
                sb.AppendLine("  (none)");
            }
            else
            {
                foreach (var m in monitors)
                {
                    sb.AppendLine($"  Name:         {m.Name}");
                    sb.AppendLine($"  MonitorId:    {m.MonitorId}");
                    sb.AppendLine($"  MonitorName:  {m.MonitorName}");
                    if (m.SerialNumber is not null)
                        sb.AppendLine($"  SerialNumber: {m.SerialNumber}");
                    sb.AppendLine($"  Resolution:   {m.Width}x{m.Height} @ {m.DisplayFrequency}Hz");
                    sb.AppendLine($"  Active:       {m.IsActive}");
                    sb.AppendLine($"  Primary:      {m.IsPrimary}");
                    sb.AppendLine($"  SavedLayout:  {m.HasSavedLayout}");
                    sb.AppendLine();
                }
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  ERROR: {ex.Message}");
            sb.AppendLine();
        }

        // QDC_ONLY_ACTIVE_PATHS
        sb.AppendLine("=== QDC_ONLY_ACTIVE_PATHS ===");
        AppendQueryResult(sb, QueryDisplayConfigFlags.QDC_ONLY_ACTIVE_PATHS, detailed: true);

        // QDC_ALL_PATHS
        sb.AppendLine("=== QDC_ALL_PATHS ===");
        AppendQueryResult(sb, QueryDisplayConfigFlags.QDC_ALL_PATHS, detailed: false);

        // QDC_DATABASE_CURRENT
        sb.AppendLine("=== QDC_DATABASE_CURRENT ===");
        AppendQueryResult(sb, QueryDisplayConfigFlags.QDC_DATABASE_CURRENT, detailed: false);

        _outputBox.Text = sb.ToString();
    }

    private void AppendQueryResult(System.Text.StringBuilder sb, QueryDisplayConfigFlags flags, bool detailed)
    {
        try
        {
            var (paths, modes) = _displayConfigApi.QueryConfig(flags);
            sb.AppendLine($"  Paths: {paths.Length}  Modes: {modes.Length}");

            if (detailed)
            {
                for (int i = 0; i < paths.Length; i++)
                {
                    var p = paths[i];
                    sb.AppendLine($"  [{i}] Source={p.sourceInfo.id}  Target={p.targetInfo.id}  " +
                                  $"AdapterLuid={p.sourceInfo.adapterId.HighPart:X8}{p.sourceInfo.adapterId.LowPart:X8}  " +
                                  $"Flags={p.flags}");
                }
            }

            sb.AppendLine();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ERROR_INVALID_PARAMETER)
        {
            sb.AppendLine("  unavailable (error 87 — no saved layout in this configuration)");
            sb.AppendLine();
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  ERROR: {ex.Message}");
            sb.AppendLine();
        }
    }
}
