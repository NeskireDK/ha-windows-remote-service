namespace HaPcRemote.Tray.Forms;

/// <summary>
/// Shared bottom footer panel used by settings tabs.
/// Buttons are right-aligned; add primary action first (it appears rightmost).
/// </summary>
internal sealed class TabFooter : Panel
{
    private readonly FlowLayoutPanel _buttons;

    public TabFooter()
    {
        Dock = DockStyle.Bottom;
        Height = 40;
        BackColor = Color.FromArgb(30, 30, 30);

        var separator = new Panel
        {
            Dock = DockStyle.Top,
            Height = 1,
            BackColor = Color.FromArgb(60, 60, 60)
        };

        _buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 5, 8, 0),
            WrapContents = false
        };

        Controls.Add(_buttons);
        Controls.Add(separator);
    }

    /// <summary>Add a button. First call = rightmost position (primary action).</summary>
    public void Add(Button button) => _buttons.Controls.Add(button);

    /// <summary>Remove all buttons and show a new set.</summary>
    public void SetButtons(IEnumerable<Button> buttons)
    {
        _buttons.Controls.Clear();
        foreach (var b in buttons)
            _buttons.Controls.Add(b);
    }

    public static Button MakeButton(string text, int width = 90, Color? accentColor = null) => new()
    {
        Text = text,
        FlatStyle = FlatStyle.Flat,
        BackColor = accentColor ?? Color.FromArgb(50, 50, 50),
        ForeColor = Color.White,
        Size = new Size(width, 28),
        Cursor = Cursors.Hand
    };

    private static readonly Color AccentGreen = Color.FromArgb(50, 130, 50);

    public static Button MakeSaveButton(string text = "Save", int width = 90) =>
        MakeButton(text, width, AccentGreen);

    public static Button MakeCancelButton(string text = "Cancel") =>
        MakeButton(text);
}

/// <summary>
/// Implemented by tabs that delegate their footer buttons to the shared form-level footer.
/// </summary>
internal interface ISettingsTab
{
    /// <summary>
    /// Return footer buttons for this tab. First button = rightmost (primary action).
    /// </summary>
    IEnumerable<Button> CreateFooterButtons();
}
