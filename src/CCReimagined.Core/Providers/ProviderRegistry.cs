namespace CCReimagined.Core.Providers;

/// <summary>The set of engines this build knows how to talk to.</summary>
public static class ProviderRegistry
{
    public static IReadOnlyList<IDatabaseProvider> All { get; } =
    [
        new SqlServerProvider(),
        new PostgreSqlProvider(),
        new MySqlProvider(),
        new SqliteProvider(),
        new OracleProvider(),
    ];

    public static IDatabaseProvider ById(string id) =>
        All.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"No database provider registered with id '{id}'.", nameof(id));

    public static IDatabaseProvider? TryById(string? id) =>
        id is null ? null : All.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
}
