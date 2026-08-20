using System.Text.Json;
using Itoguruma.Core;
using Xunit;

namespace Itoguruma.Tests;

public sealed class ChangeRequestValidationTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "itoguruma-cr-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SendMessage_WhenChangeRequestIsValid_DeliversAndCanFilter()
    {
        var path = CreateCr();
        var store = new SqliteMessageStore(Path.Combine(_directory, "messages.db"));
        var service = new MessagingService(store, new ChangeRequestValidator(_directory));
        await service.InitializeAsync();
        await service.RegisterAgentAsync("buckettie", "test");
        await service.RegisterAgentAsync("codex", "test");

        var id = await service.SendMessageAsync(new(
            "buckettie", ["codex"], "CR path notification", "cr-thread",
            MessageType: "change_request", PayloadJson: Payload(path), IdempotencyKey: "cr-1"));
        await service.SendMessageAsync(new("buckettie", ["codex"], "normal", "normal-thread"));

        var messages = await service.GetMessagesAsync("codex", messageType: "change_request");
        var message = Assert.Single(messages);
        Assert.Equal(id, message.MessageId);
        Assert.Equal("change_request", message.MessageType);
    }

    [Fact]
    public async Task SendMessage_WhenChangeRequestIsRepeated_IsIdempotent()
    {
        var path = CreateCr();
        var store = new SqliteMessageStore(Path.Combine(_directory, "messages.db"));
        var service = new MessagingService(store, new ChangeRequestValidator(_directory));
        await service.InitializeAsync();
        await service.RegisterAgentAsync("buckettie", "test");
        await service.RegisterAgentAsync("codex", "test");
        var request = new SendMessageRequest(
            "buckettie", ["codex"], "CR", "cr-thread", MessageType: "change_request",
            PayloadJson: Payload(path), IdempotencyKey: "same-cr");

        var first = await service.SendMessageAsync(request);
        var second = await service.SendMessageAsync(request);

        Assert.Equal(first, second);
        Assert.Single(await service.GetMessagesAsync("codex"));
    }

    [Fact]
    public async Task SendMessage_WhenPathIsOutsideRoot_IsRejected()
    {
        Directory.CreateDirectory(_directory);
        var outside = Path.Combine(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}.md");
        await File.WriteAllTextAsync(outside, ValidCr);
        try
        {
            var service = await CreateServiceAsync();
            await Assert.ThrowsAsync<ArgumentException>(() => service.SendMessageAsync(new(
                "buckettie", ["codex"], "CR", "cr-thread", MessageType: "change_request",
                PayloadJson: Payload(outside))));
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Theory]
    [InlineData("source_project", "Other")]
    [InlineData("target_project", "Other")]
    [InlineData("priority", "高")]
    [InlineData("status", "完了")]
    public async Task SendMessage_WhenPayloadDoesNotMatchFile_IsRejected(string property, string value)
    {
        var path = CreateCr();
        var payload = new Dictionary<string, object>
        {
            ["schema_version"] = 1,
            ["cr_path"] = path,
            ["source_project"] = "Buckettie",
            ["target_project"] = "Itoguruma",
            ["priority"] = "中",
            ["status"] = "未着手"
        };
        payload[property] = value;
        var service = await CreateServiceAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => service.SendMessageAsync(new(
            "buckettie", ["codex"], "CR", "cr-thread", MessageType: "change_request",
            PayloadJson: JsonSerializer.Serialize(payload))));
    }

    [Fact]
    public async Task InspectChangeRequest_WhenFileStatusChanged_ReportsMismatch()
    {
        var path = CreateCr();
        var validator = new ChangeRequestValidator(_directory);
        await File.WriteAllTextAsync(path, ValidCr.Replace("- 状態: 未着手", "- 状態: 対応中", StringComparison.Ordinal));

        var result = await validator.InspectAsync(Payload(path), requireStatusMatch: false);

        Assert.False(result.StatusMatches);
        Assert.Equal("対応中", result.CurrentStatus);
    }

    private async Task<MessagingService> CreateServiceAsync()
    {
        var store = new SqliteMessageStore(Path.Combine(_directory, "messages.db"));
        var service = new MessagingService(store, new ChangeRequestValidator(_directory));
        await service.InitializeAsync();
        await service.RegisterAgentAsync("buckettie", "test");
        await service.RegisterAgentAsync("codex", "test");
        return service;
    }

    private string CreateCr()
    {
        var directory = Path.Combine(_directory, "inbox", "Itoguruma");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "CR-test.md");
        File.WriteAllText(path, ValidCr);
        return path;
    }

    private static string Payload(string path) => JsonSerializer.Serialize(new
    {
        schema_version = 1,
        cr_path = path,
        source_project = "Buckettie",
        target_project = "Itoguruma",
        priority = "中",
        status = "未着手"
    });

    private const string ValidCr = """
        # Test CR

        - 依頼元: Buckettie
        - 依頼先: Itoguruma
        - 優先度: 中
        - 状態: 未着手

        ## 背景
        test

        ## 依頼内容
        test

        ## 完了条件
        test

        ## 受け取り結果
        test
        """;

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
