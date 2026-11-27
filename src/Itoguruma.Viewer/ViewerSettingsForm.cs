using Itoguruma.Core;

namespace Itoguruma.Viewer;

internal sealed class ViewerSettingsForm : Form
{
    private readonly CheckBox _autoUpdate = new()
    {
        Text = L("Check for and install updates at startup", "起動時に更新を確認してインストールする"),
        Checked = false,
        AutoSize = true
    };
    private readonly CheckBox _startWithWindows = new()
    {
        Text = L("Start Itoguruma in the notification area when Windows starts", "Windows起動時にItogurumaをタスクトレイで起動する"),
        Checked = false,
        AutoSize = true
    };

    public ViewerSettings Result { get; private set; } = new();

    public ViewerSettingsForm(ViewerSettings settings)
    {
        _autoUpdate.Checked = settings.AutoUpdate;
        _startWithWindows.Checked = settings.StartWithWindows;
        Result = settings;
        Text = L("Itoguruma Settings", "Itoguruma 設定");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new(520, 155);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(14), RowCount = 3, ColumnCount = 1 };
        layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.RowStyles.Add(new(SizeType.Percent, 100));
        layout.Controls.Add(_autoUpdate, 0, 0);
        layout.Controls.Add(_startWithWindows, 0, 1);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var save = new Button { Text = L("Save", "保存"), AutoSize = true, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = L("Cancel", "キャンセル"), AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons, 0, 2);
        Controls.Add(layout);
        AcceptButton = save;
        CancelButton = cancel;
        FormClosing += (_, e) =>
        {
            if (DialogResult != DialogResult.OK) return;
            Result = new(_autoUpdate.Checked, _startWithWindows.Checked);
            var startupUpdated = false;
            try
            {
                StartupRegistration.SetEnabled(Result.StartWithWindows, Application.ExecutablePath);
                startupUpdated = true;
                Result.Save();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                if (startupUpdated)
                {
                    try { StartupRegistration.SetEnabled(settings.StartWithWindows, Application.ExecutablePath); }
                    catch (Exception rollbackException) when (rollbackException is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
                }
                e.Cancel = true;
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };
    }

    private static string L(string english, string japanese) => AppLocalization.Text(english, japanese);
}
