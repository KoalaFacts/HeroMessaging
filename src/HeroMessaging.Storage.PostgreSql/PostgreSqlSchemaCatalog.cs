using Npgsql;

namespace HeroMessaging.Storage.PostgreSql;

/// <summary>
/// Reads the PostgreSQL catalog to decide whether schema DDL is needed. <c>CREATE INDEX IF NOT EXISTS</c> takes a
/// <c>ShareLock</c> on the table even when the index exists; on a unit-of-work connection that lock is held until
/// the business transaction ends and blocks every other transaction's inserts into the table.
/// </summary>
internal static class PostgreSqlSchemaCatalog
{
    private const string TableAndIndexCountSql = """
        SELECT CASE WHEN to_regclass(@table) IS NULL THEN -1 ELSE
            (SELECT COUNT(*) FROM pg_catalog.pg_index i
             JOIN pg_catalog.pg_class c ON c.oid = i.indexrelid
             WHERE i.indrelid = to_regclass(@table) AND c.relname = ANY(@indexes)) END
        """;

    /// <summary>
    /// Returns true when <paramref name="table"/> exists and has every index in <paramref name="indexNames"/>
    /// (names as PostgreSQL stores unquoted identifiers: lower case).
    /// </summary>
    public static async Task<bool> TableAndIndexesExistAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string table,
        string[] indexNames,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(TableAndIndexCountSql, connection, transaction);
        command.Parameters.AddWithValue("table", table);
        command.Parameters.AddWithValue("indexes", indexNames);
        var found = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
        return found == indexNames.Length;
    }
}
