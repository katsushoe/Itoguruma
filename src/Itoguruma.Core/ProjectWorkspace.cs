namespace Itoguruma.Core;

/// <summary>作業ディレクトリが属するGitリポジトリからProject IDを求めます。</summary>
public static class ProjectWorkspace
{
    /// <summary>
    /// 作業ディレクトリから上位へ`.git`を探し、リポジトリのルートディレクトリ名を正規化したProject IDを返します。
    /// `git worktree`の作業ツリーでは、`.git`ファイルの`gitdir`から主リポジトリのルートを求めます。
    /// リポジトリ外、またはディレクトリ名が有効なProject IDにならない場合はnullを返します。
    /// </summary>
    public static string? FindProjectId(string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        for (var directory = new DirectoryInfo(Path.GetFullPath(workingDirectory)); directory is not null; directory = directory.Parent)
        {
            var gitPath = Path.Combine(directory.FullName, ".git");
            if (Directory.Exists(gitPath)) return ToProjectId(directory.Name);
            if (File.Exists(gitPath)) return ToProjectId(ResolveWorktreeRoot(gitPath, directory)?.Name ?? directory.Name);
        }
        return null;
    }

    private static DirectoryInfo? ResolveWorktreeRoot(string gitFilePath, DirectoryInfo worktreeRoot)
    {
        const string prefix = "gitdir:";
        var line = File.ReadLines(gitFilePath).FirstOrDefault(value => value.StartsWith(prefix, StringComparison.Ordinal));
        if (line is null) return null;
        var gitDirectory = new DirectoryInfo(Path.GetFullPath(line[prefix.Length..].Trim(), worktreeRoot.FullName));
        // <main>/.git/worktrees/<name> の形式だけを主リポジトリへ解決します。
        if (gitDirectory.Parent is { Name: "worktrees" } worktrees && worktrees.Parent is { Name: ".git" } mainGit)
            return mainGit.Parent;
        return null;
    }

    private static string? ToProjectId(string directoryName)
    {
        if (string.IsNullOrWhiteSpace(directoryName)) return null;
        var projectId = ProjectIdPolicy.Normalize(directoryName);
        return ProjectIdPolicy.IsValid(projectId) ? projectId : null;
    }
}
