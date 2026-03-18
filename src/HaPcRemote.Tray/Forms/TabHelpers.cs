using Microsoft.Extensions.Logging;

namespace HaPcRemote.Tray.Forms;

internal static class TabHelpers
{
    public static Label MakeLabel(string text, Padding? padding = null) => new()
    {
        Text = text,
        ForeColor = Color.White,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Padding = padding ?? new Padding(0, 6, 0, 0)
    };

    public static Label MakeHelpIcon(ToolTip toolTip, string helpText)
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

    public static LogLevel ParseLogLevel(string level) => level switch
    {
        "Error"   => LogLevel.Error,
        "Info"    => LogLevel.Information,
        "Verbose" => LogLevel.Debug,
        _         => LogLevel.Warning
    };
}
