using Itoguruma.Core;
using Xunit;

namespace Itoguruma.Tests;

public sealed class ServerSingleInstanceTests
{
    [Fact]
    public void ForDatabase_WhenPathsResolveToSameFile_ReturnsSameName()
    {
        var root = Path.Combine(Path.GetTempPath(), "itoguruma-tests");
        var direct = Path.Combine(root, "messages.db");
        var equivalent = Path.Combine(root, "child", "..", "messages.db").ToUpperInvariant();

        Assert.Equal(
            ServerSingleInstance.ForDatabase(direct),
            ServerSingleInstance.ForDatabase(equivalent));
    }

    [Fact]
    public void ForDatabase_WhenDatabasesDiffer_ReturnsDifferentNames()
    {
        var root = Path.Combine(Path.GetTempPath(), "itoguruma-tests");

        Assert.NotEqual(
            ServerSingleInstance.ForDatabase(Path.Combine(root, "first.db")),
            ServerSingleInstance.ForDatabase(Path.Combine(root, "second.db")));
    }

    [Fact]
    public void ForDatabase_WhenSameDatabaseIsLocked_SecondMutexIsNotNew()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), "itoguruma-tests", Guid.NewGuid().ToString("N"), "messages.db");
        var mutexName = ServerSingleInstance.ForDatabase(databasePath);

        using var first = new Mutex(initiallyOwned: true, mutexName, out var firstCreatedNew);
        using var second = new Mutex(initiallyOwned: true, mutexName, out var secondCreatedNew);

        Assert.True(firstCreatedNew);
        Assert.False(secondCreatedNew);
    }

    [Fact]
    public void ForEndpoint_WhenUrlsResolveToSameListener_ReturnsSameName()
    {
        Assert.Equal(
            ServerSingleInstance.ForEndpoint("http://127.0.0.1:47631"),
            ServerSingleInstance.ForEndpoint("HTTP://127.0.0.1:47631/"));
    }

    [Fact]
    public void ForEndpoint_WhenPortsDiffer_ReturnsDifferentNames()
    {
        Assert.NotEqual(
            ServerSingleInstance.ForEndpoint("http://127.0.0.1:47631"),
            ServerSingleInstance.ForEndpoint("http://127.0.0.1:47632"));
    }
}
