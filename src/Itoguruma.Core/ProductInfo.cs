using System.Reflection;

namespace Itoguruma.Core;

/// <summary>Itoguruma製品の共通情報を提供します。</summary>
public static class ProductInfo
{
    /// <summary>ビルド時に設定された製品バージョンを取得します。</summary>
    public static string Version { get; } =
        typeof(ProductInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+', 2)[0]
        ?? throw new InvalidOperationException("Product version was not found.");
}
