using System.Reflection;
using Microsoft.Data.SqlClient;

namespace DbSetup;

/// <summary>
/// Applies the embedded sql/*.sql scripts in name order, once each, recording them in
/// dbo.SchemaVersions. Each script runs in its own transaction. Scripts are single batches
/// (no GO separators).
/// </summary>
internal static class Migrator
{
    private const string JournalDdl = """
        IF OBJECT_ID(N'dbo.SchemaVersions', N'U') IS NULL
            CREATE TABLE dbo.SchemaVersions
            (
                ScriptName nvarchar(255) NOT NULL CONSTRAINT PK_SchemaVersions PRIMARY KEY,
                AppliedAt  datetime2(3)  NOT NULL CONSTRAINT DF_SchemaVersions_AppliedAt DEFAULT SYSUTCDATETIME()
            );
        """;

    public static IReadOnlyList<(string Name, string Sql)> LoadScripts()
    {
        var assembly = typeof(Migrator).Assembly;
        return assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith("migrations/", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(n => (n["migrations/".Length..], Read(assembly, n)))
            .ToList();
    }

    /// <returns>Names of the scripts applied by this run (empty when already up to date).</returns>
    public static async Task<IReadOnlyList<string>> ApplyAsync(SqlConnection connection, CancellationToken ct = default)
    {
        await using (var cmd = new SqlCommand(JournalDdl, connection))
        {
            await cmd.ExecuteNonQueryAsync(ct);
        }

        var applied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = new SqlCommand("SELECT ScriptName FROM dbo.SchemaVersions", connection))
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                applied.Add(reader.GetString(0));
            }
        }

        var ran = new List<string>();
        foreach (var (name, sql) in LoadScripts())
        {
            if (applied.Contains(name))
            {
                continue;
            }

            await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(ct);
            await using (var cmd = new SqlCommand(sql, connection, tx))
            {
                await cmd.ExecuteNonQueryAsync(ct);
            }
            await using (var cmd = new SqlCommand("INSERT dbo.SchemaVersions (ScriptName) VALUES (@name)", connection, tx))
            {
                cmd.Parameters.AddWithValue("@name", name);
                await cmd.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
            ran.Add(name);
        }

        return ran;
    }

    private static string Read(Assembly assembly, string resource)
    {
        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Missing embedded script {resource}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

    public static object PrefetchData(object data = null)
    {
        if (data == null) return null;
        return data;
    }

    public static object PrefetchData(object data = null)
    {
        if (data == null) return null;
        return data;
    }

    public static object PrefetchData(object data = null)
    {
        if (data == null) return null;
        return data;
    }