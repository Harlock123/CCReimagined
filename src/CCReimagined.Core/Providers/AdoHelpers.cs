using System.Data.Common;

namespace CCReimagined.Core.Providers;

/// <summary>Small shared plumbing so each provider's catalog queries stay readable.</summary>
internal static class AdoHelpers
{
    internal static DbCommand Command(DbConnection cn, string sql, params (string Name, object? Value)[] parameters)
    {
        var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = 120;

        foreach (var (name, value) in parameters)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            p.Value = value ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }

        return cmd;
    }

    internal static async Task<IReadOnlyList<string>> ReadStringsAsync(
        DbConnection cn,
        string sql,
        CancellationToken ct,
        params (string Name, object? Value)[] parameters)
    {
        await using var cmd = Command(cn, sql, parameters);
        await using var r = await cmd.ExecuteReaderAsync(ct);

        var result = new List<string>();
        while (await r.ReadAsync(ct))
        {
            if (!await r.IsDBNullAsync(0, ct))
                result.Add(r.GetString(0));
        }

        return result;
    }

    /// <summary>Reads a nullable int from a catalog column that may be NULL or a widened type.</summary>
    internal static int? NullableInt(DbDataReader r, int ordinal)
    {
        if (r.IsDBNull(ordinal))
            return null;

        var value = r.GetValue(ordinal);
        return value switch
        {
            int i => i,
            long l => (int)l,
            short s => s,
            byte b => b,
            uint u => (int)u,
            ulong ul => (int)ul,
            decimal d => (int)d,
            _ => int.TryParse(value.ToString(), out var parsed) ? parsed : null,
        };
    }

    internal static bool TruthyFlag(DbDataReader r, int ordinal)
    {
        if (r.IsDBNull(ordinal))
            return false;

        var value = r.GetValue(ordinal);
        return value switch
        {
            bool b => b,
            int i => i != 0,
            long l => l != 0,
            short s => s != 0,
            byte by => by != 0,
            string s => s.Equals("YES", StringComparison.OrdinalIgnoreCase)
                        || s.Equals("ALWAYS", StringComparison.OrdinalIgnoreCase)
                        || s.Equals("BY DEFAULT", StringComparison.OrdinalIgnoreCase)
                        || s == "1",
            _ => false,
        };
    }
}
