using System.Diagnostics;
using HaPcRemote.Tray.Logging;
using Microsoft.Extensions.Logging;

namespace HaPcRemote.Tray.Forms;

internal sealed class LogTab : TabPage
{
    private readonly InMemoryLogProvider _provider;
    private readonly RichTextBox _logBox;

    public LogTab(InMemoryLogProvider provider, int port)
    {
        _provider = provider;

        Text = "Log";
        BackColor = Color.FromArgb(30, 30, 30);
        ForeColor = Color.White;

        _logBox = new RichTextBox
        {
            ReadOnly = true,
            Multiline = true,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.White,
            Font = new Font("Consolas", 9.5f),
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            WordWrap = false
        };

        var clearButton = TabFooter.MakeButton("Clear");
        clearButton.Click += (_, _) => _logBox.Clear();

        var debugButton = TabFooter.MakeButton("API Explorer");
        debugButton.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo($"http://localhost:{port}/api-explorer") { UseShellExecute = true }); }
            catch { /* best effort */ }
        };

        var footer = new TabFooter();
        footer.Add(clearButton);
        footer.Add(debugButton);

        Controls.Add(_logBox);
        Controls.Add(footer);

        _provider.OnLogEntry += OnNewLogEntry;
    }

    private void OnNewLogEntry(LogEntry entry)
    {
        if (IsDisposed || !IsHandleCreated) return;

        try
        {
            BeginInvoke(() => AppendEntry(entry));
        }
        catch (ObjectDisposedException)
        {
            // Form disposed between check and invoke
        }
    }

    private void AppendEntry(LogEntry entry)
    {
        var color = entry.Level switch
        {
            LogLevel.Error => Color.FromArgb(255, 100, 100),
            LogLevel.Critical => Color.FromArgb(255, 100, 100),
            LogLevel.Warning => Color.FromArgb(255, 200, 100),
            _ => Color.White
        };

        var level = entry.Level switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "???"
        };

        var line = $"[{entry.Timestamp:HH:mm:ss}] [{level}] {entry.Category} - {entry.Message}\n";

        _logBox.SelectionStart = _logBox.TextLength;
        _logBox.SelectionLength = 0;
        _logBox.SelectionColor = color;
        _logBox.AppendText(line);
        _logBox.ScrollToCaret();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible) LoadExistingEntries();
    }

    private void LoadExistingEntries()
    {
        _logBox.SuspendLayout();
        _logBox.Clear();
        foreach (var entry in _provider.GetEntries())
            AppendEntry(entry);
        _logBox.ResumeLayout();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _provider.OnLogEntry -= OnNewLogEntry;
        base.Dispose(disposing);
    }
}
