using Itoguruma.Core;
using Xunit;

namespace Itoguruma.Tests;

public sealed class AuthenticationTokenServiceTests
{
    [Fact]
    public void InitialIssue_CreatesTokenAndDiagnosticGenerationTogether()
    {
        var store = new MemoryTokenStore();
        var service = new AuthenticationTokenService(store, () => Enumerable.Repeat((byte)7, 32).ToArray(),
            () => "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

        var generation = service.Rotate();

        Assert.Equal("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", generation);
        Assert.Equal(generation, store.Read()!.GenerationId);
        Assert.Equal(43, store.Read()!.Token.Length);
    }

    [Fact]
    public void Rotation_ReplacesTokenAndGeneration()
    {
        var generations = new Queue<string>([
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"]);
        var bytes = new byte[] { 1 };
        var store = new MemoryTokenStore();
        var service = new AuthenticationTokenService(store, () => Enumerable.Repeat(bytes[0]++, 32).ToArray(), generations.Dequeue);

        service.Rotate();
        var first = store.Read()!;
        service.Rotate();
        var second = store.Read()!;

        Assert.NotEqual(first.Token, second.Token);
        Assert.NotEqual(first.GenerationId, second.GenerationId);
        Assert.Equal("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", second.GenerationId);
    }

    [Fact]
    public void InvalidGenerationFactory_DoesNotSaveCredential()
    {
        var store = new MemoryTokenStore();
        var service = new AuthenticationTokenService(store, () => new byte[32], () => "bad\r\nvalue");

        Assert.Throws<InvalidOperationException>(() => service.Rotate());
        Assert.Null(store.Read());
    }

    private sealed class MemoryTokenStore : IUserTokenStore
    {
        private StoredAuthenticationToken? _value;
        public bool IsConfigured => _value is not null;
        public StoredAuthenticationToken? Read() => _value;
        public void Save(StoredAuthenticationToken credential) => _value = credential;
    }
}
