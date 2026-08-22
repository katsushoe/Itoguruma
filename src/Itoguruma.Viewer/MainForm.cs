using System.ComponentModel;
using System.Globalization;
using Itoguruma.Core;

namespace Itoguruma.Viewer;

public sealed class MainForm : Form
{
    private readonly TextBox _databasePath = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _typeFilter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
    private readonly ComboBox _agentFilter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly TextBox _searchText = new() { Width = 220 };
    private readonly NumericUpDown _limit = new() { Minimum = 1, Maximum = 5000, Value = 500, Width = 75 };
    private readonly CheckBox _autoRefresh = new() { Checked = true, AutoSize = true };
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
        _searchText.PlaceholderText = L("Body, thread, or message ID", "本文・thread・message ID");
        _autoRefresh.Text = L("Auto refresh", "自動更新");
        _typeFilter.Items.AddRange([L("All", "すべて"), "message", "notification", "system", "change_request"]);
        _typeFilter.SelectedIndex = 0;
        _agentFilter.Items.Add(L("All", "すべて"));
        _agentFilter.SelectedIndex = 0;
        ConfigureGrid();
        Controls.Add(BuildLayout());

        _timer.Interval = 2000;
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();
        Shown += async (_, _) => await RefreshAsync();
        FormClosed += (_, _) => _timer.Stop();
        _messages.SelectionChanged += (_, _) => ShowSelectedMessage();
        _messages.CellDoubleClick += ShowMessageDetails;
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
        var browse = new Button { Text = L("Browse...", "参照..."), AutoSize = true };
        browse.Click += (_, _) => BrowseDatabase();
        database.Controls.Add(browse, 2, 0);
        var refresh = new Button { Text = L("Refresh", "更新"), AutoSize = true };
        refresh.Click += async (_, _) => await RefreshAsync();
        database.Controls.Add(refresh, 3, 0);

        var filters = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new(8, 2, 8, 6), WrapContents = true };
        filters.Controls.AddRange([
            LabelFor(L("Type", "種別")), _typeFilter, LabelFor("Agent"), _agentFilter,
            LabelFor(L("Search", "検索")), _searchText, LabelFor(L("Limit", "最大件数")), _limit,
            _autoRefresh, LabelFor(L("Interval (sec)", "間隔(秒)")), _interval, _summary, _state
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
        AddColumn("CreatedAtLocal", L("Date", "日時"), 145);
        AddColumn("DeliveryStatus", L("Status", "状態"), 70);
        AddColumn("SenderAgentId", L("From", "送信元"), 110);
        AddColumn("Provider", "Provider", 90);
        AddColumn("RecipientAgentId", L("To", "宛先"), 110);
        AddColumn("ThreadId", "Thread", 150);
        AddColumn("MessageType", L("Type", "種別"), 75);
        AddColumn("BodyPreview", L("Body", "本文"), 420, DataGridViewAutoSizeColumnMode.Fill);
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
        _state.Text = L("Loading...", "読込中...");
        try
        {
            var monitor = new SqliteMessageMonitor(_databasePath.Text.Trim());
            var query = new MessageMonitorQuery(
                "pending", SelectedValue(_agentFilter),
                _searchText.Text, SelectedValue(_typeFilter), decimal.ToInt32(_limit.Value));
            var snapshot = await monitor.LoadAsync(query);
            var currentAgent = _agentFilter.SelectedItem?.ToString();
            _agentFilter.BeginUpdate();
            _agentFilter.Items.Clear();
            _agentFilter.Items.Add(L("All", "すべて"));
            foreach (var agent in snapshot.AgentIds) _agentFilter.Items.Add(agent);
            _agentFilter.SelectedItem = currentAgent is not null && _agentFilter.Items.Contains(currentAgent)
                ? currentAgent
                : L("All", "すべて");
            _agentFilter.EndUpdate();
            _messages.DataSource = new BindingList<MessageRow>(snapshot.Messages.Select(x => new MessageRow(x)).ToList());
            _summary.Text = L($"Undelivered {snapshot.PendingCount}", $"未配信 {snapshot.PendingCount}件");
            _state.Text = L($"{snapshot.Messages.Count} items  {snapshot.LoadedAt.ToLocalTime():HH:mm:ss}", $"{snapshot.Messages.Count}件  {snapshot.LoadedAt.ToLocalTime():HH:mm:ss}");
            ShowSelectedMessage();
        }
        catch (Exception ex)
        {
            _state.Text = L("Load failed", "読込失敗");
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
            Provider: {message.Provider}
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

    private void ShowMessageDetails(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || _messages.Rows[e.RowIndex].DataBoundItem is not MessageRow row) return;

        using var dialog = new MessageDetailsForm(row.Source);
        dialog.ShowDialog(this);
    }

    private static Label LabelFor(string text) => new() { Text = text, AutoSize = true, Padding = new(6, 7, 2, 0) };
    private static string L(string english, string japanese) => AppLocalization.Text(english, japanese);
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
        public string Provider => Source.Provider;
        public string RecipientAgentId => Source.RecipientAgentId;
        public string ThreadId => Source.ThreadId;
        public string MessageType => Source.MessageType;
        public string BodyPreview => Source.Body.ReplaceLineEndings(" ");
    }
}
