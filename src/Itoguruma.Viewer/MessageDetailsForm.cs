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
        Text = $"メッセージ詳細 - {message.MessageId}";
        detailsTextBox.Text = FormatMessage(message);
    }

    private static string FormatMessage(MonitoredMessage message) => $"""
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

    private static string Local(DateTimeOffset? value) =>
        value?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "";
}
