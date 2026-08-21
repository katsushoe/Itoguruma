using System.Globalization;
using Itoguruma.Core;

namespace Itoguruma.Viewer;

/// <summary>
/// メッセージの全項目を表示するダイアログです。
/// </summary>
public sealed partial class MessageDetailsForm : Form
{
    /// <summary>
    /// 表示対象のメッセージを指定して初期化します。
    /// </summary>
    /// <param name="message">表示対象のメッセージ。</param>
    public MessageDetailsForm(MonitoredMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        InitializeComponent();
        Text = $"{AppLocalization.Text("Message details", "メッセージ詳細")} - {message.MessageId}";
        itemColumn.HeaderText = AppLocalization.Text("Item", "項目");
        valueColumn.HeaderText = AppLocalization.Text("Value", "値");
        closeButton.Text = AppLocalization.Text("Close", "閉じる");
        AddRow("Message ID", message.MessageId);
        AddRow("Thread ID", message.ThreadId);
        AddRow("From", message.SenderAgentId);
        AddRow("To", message.RecipientAgentId);
        AddRow("Type", message.MessageType);
        AddRow("Status", message.DeliveryStatus);
        AddRow("Created", Local(message.CreatedAt));
        AddRow("Lease until", Local(message.LeaseUntil));
        AddRow("Delivered", Local(message.DeliveredAt));
        AddRow("Acknowledged", Local(message.AcknowledgedAt));
        AddRow("Reply to", message.ReplyToMessageId);
        AddRow("Idempotency key", message.IdempotencyKey);
        AddRow("Body", message.Body);
        AddRow("Payload JSON", message.PayloadJson);
    }

    private void AddRow(string item, string? value) => detailsGrid.Rows.Add(item, value ?? "");

    private static string Local(DateTimeOffset? value) =>
        value?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "";
}
