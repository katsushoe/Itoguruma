using Itoguruma.Cli;
using Itoguruma.Core;
using Xunit;

namespace Itoguruma.Tests;

public sealed class HumanConfirmationTests
{
    [Fact]
    public void Require_WithMatchingFiveDigits_Succeeds()
    {
        var console = new FakeConsole("12345");
        new HumanConfirmation(console, codeGenerator: () => 12345).Require();
    }

    [Fact]
    public void Require_WithRedirectedInput_ReturnsFixedErrorCode()
    {
        var exception = Assert.Throws<ProjectOperationException>(() =>
            new HumanConfirmation(new FakeConsole(string.Empty) { IsInputRedirected = true }).Require());
        Assert.Equal(ProjectErrorCodes.ConsoleRedirected, exception.ErrorCode);
    }

    [Fact]
    public void Require_WithRedirectedOutput_ReturnsFixedErrorCode()
    {
        var exception = Assert.Throws<ProjectOperationException>(() =>
            new HumanConfirmation(new FakeConsole(string.Empty) { IsOutputRedirected = true }).Require());
        Assert.Equal(ProjectErrorCodes.ConsoleRedirected, exception.ErrorCode);
    }

    [Fact]
    public void Require_AfterThreeFailures_ReturnsFixedErrorCode()
    {
        var exception = Assert.Throws<ProjectOperationException>(() =>
            new HumanConfirmation(new FakeConsole("000000000000000"), codeGenerator: () => 12345).Require());
        Assert.Equal(ProjectErrorCodes.ConfirmationFailed, exception.ErrorCode);
    }

    private sealed class FakeConsole(string keys) : IHumanConfirmationConsole
    {
        private readonly Queue<char> _keys = new(keys);
        public bool IsInputRedirected { get; init; }
        public bool IsOutputRedirected { get; init; }
        public void WriteLine(string value) { }
        public ConsoleKeyInfo ReadKey(bool intercept) => new(_keys.Dequeue(), ConsoleKey.D0, false, false, false);
    }
}
