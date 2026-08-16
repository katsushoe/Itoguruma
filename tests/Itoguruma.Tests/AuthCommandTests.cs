using Itoguruma.Cli;
using Xunit;

namespace Itoguruma.Tests;

public sealed class AuthCommandTests
{
    [Fact]
    public void Status_WhenConfigured_DoesNotDisplayToken()
    {
        var store = new FakeTokenStore { Token = "secret-value" };
        var output = new StringWriter();

        int result = new AuthCommand(store, new StringReader(""), output, new StringWriter()).Run(["status"]);

        Assert.Equal(0, result);
        Assert.Contains("configured", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(store.Token, output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Status_WhenNotConfigured_ReportsMissing()
    {
        var output = new StringWriter();

        int result = new AuthCommand(new FakeTokenStore(), new StringReader(""), output, new StringWriter()).Run(["status"]);

        Assert.Equal(0, result);
        Assert.Contains("not configured", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Rotate_WhenConfirmed_SavesTokenWithoutDisplayingIt()
    {
        var store = new FakeTokenStore();
        var output = new StringWriter();
        byte[] generated = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();

        int result = new AuthCommand(store, new StringReader("ROTATE\n"), output, new StringWriter(), () => generated)
            .Run(["rotate"]);

        Assert.Equal(0, result);
        Assert.NotNull(store.Token);
        Assert.DoesNotContain(store.Token, output.ToString(), StringComparison.Ordinal);
        Assert.All(generated, value => Assert.Equal(0, value));
    }

    [Fact]
    public void Rotate_WhenConfirmationRejected_DoesNotSave()
    {
        var store = new FakeTokenStore();

        int result = new AuthCommand(store, new StringReader("no\n"), new StringWriter(), new StringWriter()).Run(["rotate"]);

        Assert.Equal(1, result);
        Assert.Null(store.Token);
    }

    [Fact]
    public void Rotate_WhenSaveFails_ReturnsErrorWithoutDisplayingToken()
    {
        var store = new FakeTokenStore { SaveException = new IOException("access denied") };
        var error = new StringWriter();
        byte[] generated = Enumerable.Repeat((byte)42, 32).ToArray();
        string encodedToken = Convert.ToBase64String(generated).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        int result = new AuthCommand(store, new StringReader("ROTATE\n"), new StringWriter(), error, () => generated)
            .Run(["rotate"]);

        Assert.Equal(2, result);
        Assert.Contains("access denied", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(encodedToken, error.ToString(), StringComparison.Ordinal);
    }

    private sealed class FakeTokenStore : IUserTokenStore
    {
        public string? Token { get; set; }

        public Exception? SaveException { get; init; }

        public bool IsConfigured => !string.IsNullOrWhiteSpace(Token);

        public void Save(string token)
        {
            if (SaveException is not null)
            {
                throw SaveException;
            }

            Token = token;
        }
    }
}
