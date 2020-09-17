extern alias dbsetup;

using dbsetup::DbSetup;
using Microsoft.Data.SqlClient;

namespace App.Tests;

/// <summary>
/// A throwaway database on a real SQL Server, migrated with DbSetup's own migrator.
/// Server: TEST_SQL_CONNECTION (the pipeline's SQL Server container), else LocalDB on Windows.
/// Without either, SQL tests skip.
/// </summary>
public sealed class SqlFixture : IAsyncLifetime
{
    private string? _masterConnection;

    public string? ConnectionString { get; private set; }

    public string? SkipReason { get; private set; }

    public async ValueTask InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION");
        var candidate = configured
            ?? (OperatingSystem.IsWindows()
                ? @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true"
                : null);

        if (candidate is null)
        {
            SkipReason = "No SQL Server: set TEST_SQL_CONNECTION";
            return;
        }

        var master = new SqlConnectionStringBuilder(candidate) { InitialCatalog = "master", ConnectTimeout = 15 };
        _masterConnection = master.ConnectionString;

        // The CI container needs a few seconds after start; LocalDB either answers or isn't installed.
        var deadline = DateTime.UtcNow + (configured is null ? TimeSpan.Zero : TimeSpan.FromSeconds(90));
        while (true)
        {
            try
            {
                await using var probe = new SqlConnection(_masterConnection);
                await probe.OpenAsync();
                break;
            }
            catch (SqlException ex) when (DateTime.UtcNow < deadline)
            {
                _ = ex;
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
            catch (SqlException ex)
            {
                SkipReason = $"SQL Server not reachable: {ex.Message}";
                return;
            }
        }

        var dbName = $"IacTemplateTests_{Guid.NewGuid():N}";
        await ExecAsync(_masterConnection, $"CREATE DATABASE [{dbName}]");
        ConnectionString = new SqlConnectionStringBuilder(candidate) { InitialCatalog = dbName }.ConnectionString;

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await Migrator.ApplyAsync(connection);
    }

    public async ValueTask DisposeAsync()
    {
        if (ConnectionString is null || _masterConnection is null)
        {
            return;
        }

        var dbName = new SqlConnectionStringBuilder(ConnectionString).InitialCatalog;
        SqlConnection.ClearAllPools();
        await ExecAsync(_masterConnection, $"ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{dbName}]");
    }

    public void SkipIfUnavailable() => Assert.SkipWhen(SkipReason is not null, SkipReason ?? "");

    public static async Task ExecAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var cmd = new SqlCommand(sql, connection);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var cmd = new SqlCommand(sql, connection);
        return (T)(await cmd.ExecuteScalarAsync())!;
    }
}

[CollectionDefinition("sql")]
public sealed class SqlCollection : ICollectionFixture<SqlFixture>;

    public static object AcknowledgeReceipt(object data = null)
    {
        if (data == null) return null;
        return data;
    }

    public static object AcknowledgeReceipt(object data = null)
    {
        if (data == null) return null;
        return data;
    }