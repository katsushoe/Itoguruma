using Itoguruma.Core;
using Xunit;

namespace Itoguruma.Tests;

public sealed class ProjectWorkspaceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "itoguruma-workspace-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void FindProjectId_WhenInsideRepositorySubdirectory_ReturnsLowercaseRepositoryName()
    {
        var repository = CreateRepository("AI_prompt");
        var nested = Directory.CreateDirectory(Path.Combine(repository, "docs", "notes")).FullName;

        Assert.Equal("ai_prompt", ProjectWorkspace.FindProjectId(nested));
    }

    [Fact]
    public void FindProjectId_WhenInsideWorktree_ReturnsMainRepositoryName()
    {
        var repository = CreateRepository("Itoguruma");
        var worktreeGitDirectory = Directory.CreateDirectory(Path.Combine(repository, ".git", "worktrees", "feature-x")).FullName;
        var worktree = Directory.CreateDirectory(Path.Combine(_directory, "worktrees", "feature-x")).FullName;
        File.WriteAllText(Path.Combine(worktree, ".git"), "gitdir: " + worktreeGitDirectory + "\n");

        Assert.Equal("itoguruma", ProjectWorkspace.FindProjectId(worktree));
    }

    [Fact]
    public void FindProjectId_WhenRepositoryNameIsNotValidProjectId_ReturnsNull()
    {
        var repository = CreateRepository("my-repo");

        Assert.Null(ProjectWorkspace.FindProjectId(repository));
    }

    [Fact]
    public void FindProjectId_WhenOutsideRepository_ReturnsNull()
    {
        var plain = Directory.CreateDirectory(Path.Combine(_directory, "plain", "child")).FullName;

        // テスト用一時ディレクトリの上位にGitリポジトリがない前提です。
        Assert.Null(ProjectWorkspace.FindProjectId(plain));
    }

    private string CreateRepository(string name)
    {
        var repository = Directory.CreateDirectory(Path.Combine(_directory, name)).FullName;
        Directory.CreateDirectory(Path.Combine(repository, ".git"));
        return repository;
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
