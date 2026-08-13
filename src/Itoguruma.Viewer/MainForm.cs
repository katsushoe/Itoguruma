using System.ComponentModel;
using System.Globalization;
using Itoguruma.Core;

namespace Itoguruma.Viewer;

public sealed class MainForm : Form
{
    private readonly TextBox _databasePath = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _statusFilter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    private readonly ComboBox _agentFilter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly TextBox _searchText = new() { Width = 220, PlaceholderText = "本文・thread・message ID" };
    private readonly NumericUpDown _limit = new() { Minimum = 1, Maximum = 5000, Value = 500, Width = 75 };
    private readonly CheckBox _autoRefresh = new() { Text = "自動更新", Checked = true, AutoSize = true };
    private readonly NumericUpDown _interval = new() { Minimum = 1, Maximum = 60, Value = 2, Width = 55 };
    private readonly Label _summary = new() { AutoSize = true, Padding = new(8, 7, 0, 0) };
    private readonly Label _state = new() { AutoSize = true, ForeColor = Color.DimGray, Padding = new(8, 7, 0, 0) };
    private readonly DataGridView _messages = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoGenerateColumns = false,
        RowHeadersVisible = false
    };
    private readonly TextBox _details = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Both,
        Font = new Font(FontFamily.GenericMonospace, 10),
        WordWrap = false
    };
    private readonly System.Windows.Forms.Timer _timer = new();
    private bool _refreshing;

    public MainForm(string? databasePath)
    {
        Text = $"Itoguruma Message Viewer {ProductInfo.Version}";
        Width = 1280;
        Height = 760;
        MinimumSize = new(900, 560);
        StartPosition = FormStartPosition.CenterScreen;
        _databasePath.Text = databasePath ?? ResolveDefaultDatabasePath();
        _statusFilter.Items.AddRange(["すべて", "pending", "leased", "acked"]);
        _statusFilter.SelectedIndex = 0;
        _agentFilter.Items.Add("すべて");
        _agentFilter.SelectedIndex = 0;
        ConfigureGrid();
        Controls.Add(BuildLayout());

        _timer.Interval = 2000;
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();
        Shown += async (_, _) => await RefreshAsync();
        FormClosed += (_, _) => _timer.Stop();
        _messages.SelectionChanged += (_, _) => ShowSelectedMessage();
        _interval.ValueChanged += (_, _) => _timer.Interval = decimal.ToInt32(_interval.Value) * 1000;
        _autoRefresh.CheckedChanged += (_, _) => _timer.Enabled = _autoRefresh.Checked;
    }

    private Control BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        root.RowStyles.Add(new(SizeType.AutoSize));
        root.RowStyles.Add(new(SizeType.AutoSize));
        root.RowStyles.Add(new(SizeType.Percent, 100));

        var database = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, Padding = new(8, 8, 8, 2) };
        database.ColumnStyles.Add(new(SizeType.AutoSize));
        database.ColumnStyles.Add(new(SizeType.Percent, 100));
        database.ColumnStyles.Add(new(SizeType.AutoSize));
        database.ColumnStyles.Add(new(SizeType.AutoSize));
        database.Controls.Add(new Label { Text = "SQLite DB", AutoSize = true, Padding = new(0, 7, 8, 0) }, 0, 0);
        database.Controls.Add(_databasePath, 1, 0);
        var browse = new Button { Text = "参照...", AutoSize = true };
        browse.Click += (_, _) => BrowseDatabase();
        database.Controls.Add(browse, 2, 0);
        var refresh = new Button { Text = "更新", AutoSize = true };
        refresh.Click += async (_, _) => await RefreshAsync();
        database.Controls.Add(refresh, 3, 0);

        var filters = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new(8, 2, 8, 6), WrapContents = true };
        filters.Controls.AddRange([
            LabelFor("状態"), _statusFilter, LabelFor("Agent"), _agentFilter,
            LabelFor("検索"), _searchText, LabelFor("最大件数"), _limit,
            _autoRefresh, LabelFor("間隔(秒)"), _interval, _summary, _state
        ]);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 430 };
        split.Panel1.Controls.Add(_messages);
        split.Panel2.Controls.Add(_details);
        root.Controls.Add(database, 0, 0);
        root.Controls.Add(filters, 0, 1);
        root.Controls.Add(split, 0, 2);
        return root;
    }

    private void ConfigureGrid()
    {
        AddColumn("CreatedAtLocal", "日時", 145);
        AddColumn("DeliveryStatus", "状態", 70);
        AddColumn("SenderAgentId", "送信元", 110);
        AddColumn("RecipientAgentId", "宛先", 110);
        AddColumn("ThreadId", "Thread", 150);
        AddColumn("MessageType", "種別", 75);
        AddColumn("BodyPreview", "本文", 420, DataGridViewAutoSizeColumnMode.Fill);
        _messages.CellFormatting += (_, e) =>
        {
            if (_messages.Columns[e.ColumnIndex].DataPropertyName != "DeliveryStatus" || e.Value is not string status) return;
            e.CellStyle ??= new DataGridViewCellStyle();
            e.CellStyle.ForeColor = status switch
            {
                "pending" => Color.DarkOrange,
                "leased" => Color.RoyalBlue,
                "acked" => Color.SeaGreen,
                _ => Color.Black
            };
        };
    }

    private void AddColumn(string property, string title, int width,
        DataGridViewAutoSizeColumnMode sizeMode = DataGridViewAutoSizeColumnMode.None) =>
        _messages.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = property,
            HeaderText = title,
            Width = width,
            AutoSizeMode = sizeMode
        });

    private async Task RefreshAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        _state.Text = "読込中...";
        try
        {
            var monitor = new SqliteMessageMonitor(_databasePath.Text.Trim());
            var query = new MessageMonitorQuery(
                SelectedValue(_statusFilter), SelectedValue(_agentFilter),
                _searchText.Text, decimal.ToInt32(_limit.Value));
            var snapshot = await monitor.LoadAsync(query);
            var currentAgent = _agentFilter.SelectedItem?.ToString();
            _agentFilter.BeginUpdate();
            _agentFilter.Items.Clear();
            _agentFilter.Items.Add("すべて");
            foreach (var agent in snapshot.AgentIds) _agentFilter.Items.Add(agent);
            _agentFilter.SelectedItem = currentAgent is not null && _agentFilter.Items.Contains(currentAgent)
                ? currentAgent
                : "すべて";
            _agentFilter.EndUpdate();
            _messages.DataSource = new BindingList<MessageRow>(snapshot.Messages.Select(x => new MessageRow(x)).ToList());
            _summary.Text = $"pending {snapshot.PendingCount} / leased {snapshot.LeasedCount} / acked {snapshot.AcknowledgedCount}";
            _state.Text = $"{snapshot.Messages.Count}件  {snapshot.LoadedAt.ToLocalTime():HH:mm:ss}";
            ShowSelectedMessage();
        }
        catch (Exception ex)
        {
            _state.Text = "読込失敗";
            _details.Text = ex.Message;
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void BrowseDatabase()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "SQLite database (*.db)|*.db|All files (*.*)|*.*",
            FileName = Path.GetFileName(_databasePath.Text),
            InitialDirectory = Path.GetDirectoryName(_databasePath.Text)
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) _databasePath.Text = dialog.FileName;
    }

    private void ShowSelectedMessage()
    {
        if (_messages.CurrentRow?.DataBoundItem is not MessageRow row)
        {
            _details.Clear();
            return;
        }
        var message = row.Source;
        _details.Text = $"""
            Message ID: {message.MessageId}
            Thread ID: {message.ThreadId}
            From: {message.SenderAgentId}
            To: {message.RecipientAgentId}
            Type: {message.MessageType}
            Status: {message.DeliveryStatus}
            Created: {Local(message.CreatedAt)}
            Lease until: {Local(message.LeaseUntil)}
            Delivered: {Local(message.DeliveredAt)}
            Acknowledged: {Local(message.AcknowledgedAt)}
            Reply to: {message.ReplyToMessageId}
            Idempotency key: {message.IdempotencyKey}

            Body:
            {message.Body}

            Payload JSON:
            {message.PayloadJson}
            """;
    }

    private static Label LabelFor(string text) => new() { Text = text, AutoSize = true, Padding = new(6, 7, 2, 0) };
    private static string? SelectedValue(ComboBox comboBox) =>
        comboBox.SelectedIndex <= 0 ? null : comboBox.SelectedItem?.ToString();
    private static string Local(DateTimeOffset? value) =>
        value?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "";
    private static string ResolveDefaultDatabasePath()
    {
        var environment = Environment.GetEnvironmentVariable("ITOGURUMA_DB");
        if (!string.IsNullOrWhiteSpace(environment)) return environment;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "Itoguruma", "data", "messages.db");
    }

    private sealed class MessageRow
    {
        public MessageRow(MonitoredMessage source) => Source = source;

        public MonitoredMessage Source { get; }
        public string CreatedAtLocal => Source.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        public string DeliveryStatus => Source.DeliveryStatus;
        public string SenderAgentId => Source.SenderAgentId;
        public string RecipientAgentId => Source.RecipientAgentId;
        public string ThreadId => Source.ThreadId;
        public string MessageType => Source.MessageType;
        public string BodyPreview => Source.Body.ReplaceLineEndings(" ");
    }
}
