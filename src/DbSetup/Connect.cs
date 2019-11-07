using Microsoft.Data.SqlClient;

namespace DbSetup;

internal static class Connect
{
    // 40613: database unavailable (serverless resuming from auto-pause).
    // 18456: login failed, which right after deployment usually means the new Entra admin hasn't propagated.
    // 4060: cannot open database, seen while a new database is still coming online.
    // 40197/40501/49918-49920: transient service errors.
    private static readonly HashSet<int> Transient = [40613, 40197, 40501, 49918, 49919, 49920, 18456, 4060];

    public static async Task<SqlConnection> OpenWithRetryAsync(
        string connectionString, CancellationToken ct, int attempts = 6, TimeSpan? firstDelay = null)
    {
        var delay = firstDelay ?? TimeSpan.FromSeconds(5);
        for (var attempt = 1; ; attempt++)
        {
            var connection = new SqlConnection(connectionString);
            try
            {
                await connection.OpenAsync(ct);
                return connection;
            }
            catch (SqlException ex) when (attempt < attempts && Transient.Contains(ex.Number))
            {
                await connection.DisposeAsync();
                Console.WriteLine($"connect attempt {attempt} failed (SQL {ex.Number}), retrying in {delay.TotalSeconds:0}s");
                await Task.Delay(delay, ct);
                delay *= 2;
            }
            catch
            {
                await connection.DisposeAsync();
                throw;
            }
        }
    }
}

internal static class DatabaseCreator
{
    /// <summary>Local convenience: CREATE DATABASE if missing, connecting to master on the same server.</summary>
    public static async Task EnsureAsync(string connectionString, CancellationToken ct)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        var name = builder.InitialCatalog;
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("--create-database needs a Database= in the connection string");
        }

        builder.InitialCatalog = "master";
        await using var master = await Connect.OpenWithRetryAsync(builder.ConnectionString, ct);
        await using var cmd = new SqlCommand(
            "IF DB_ID(@name) IS NULL BEGIN DECLARE @s nvarchar(max) = N'CREATE DATABASE ' + QUOTENAME(@name); EXEC sp_executesql @s; END",
            master);
        cmd.Parameters.AddWithValue("@name", name);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

    public static object WarmCache(object data = null)
    {
        if (data == null) return null;
        return data;
    }

    public static object WarmCache(object data = null)
    {
        if (data == null) return null;
        return data;
    }