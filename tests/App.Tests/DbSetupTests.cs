extern alias dbsetup;

using dbsetup::DbSetup;
using Microsoft.Data.SqlClient;

namespace App.Tests;

public sealed class OptionsTests
{
    [Fact]
    public void Server_mode_uses_active_directory_default_over_encrypted_tcp()
    {
        var o = Options.Parse(["--server", "sql-x.database.windows.net", "--database", "sqldb-x"]);
        var cs = new SqlConnectionStringBuilder(o.ConnectionString);

        Assert.Equal("tcp:sql-x.database.windows.net,1433", cs.DataSource);
        Assert.Equal("sqldb-x", cs.InitialCatalog);
        Assert.Equal(SqlAuthenticationMethod.ActiveDirectoryDefault, cs.Authentication);
        Assert.Equal(SqlConnectionEncryptOption.Mandatory, cs.Encrypt);
        Assert.False(o.CreateDatabase);
        Assert.Null(o.GrantName);
        Assert.Equal(["db_datareader", "db_datawriter"], o.Roles);
    }

    [Fact]
    public void Grant_options_parse_client_id_and_roles()
    {
        var id = Guid.NewGuid();
        var o = Options.Parse([
            "--server", "s", "--database", "d",
            "--grant-name", "id-iacdemo-dev", "--grant-client-id", id.ToString(),
            "--roles", "db_datareader, db_ddladmin",
        ]);

        Assert.Equal("id-iacdemo-dev", o.GrantName);
        Assert.Equal(id, o.GrantClientId);
        Assert.Equal(["db_datareader", "db_ddladmin"], o.Roles);
    }

    [Theory]
    [InlineData("--server", "s")]
    [InlineData("--database", "d")]
    [InlineData("--connection-string", "Server=x", "--server", "s")]
    [InlineData("--server", "s", "--database", "d", "--grant-name", "n")]
    [InlineData("--server", "s", "--database", "d", "--grant-name", "n", "--grant-client-id", "not-a-guid")]
    [InlineData("--server", "s", "--database", "d", "--skip-migrations")]
    [InlineData("--server", "s", "--database", "d", "--bogus")]
    [InlineData("--server")]
    public void Invalid_combinations_are_rejected(params string[] args)
    {
        Assert.Throws<ArgumentException>(() => Options.Parse(args));
    }
}

public sealed class AccessGrantTests
{
    [Theory]
    [InlineData("db_owner")]
    [InlineData("db_securityadmin")]
    [InlineData("db_datareader'; DROP TABLE x; --")]
    public void Roles_outside_the_allowlist_are_rejected(string role)
    {
        var ex = Assert.Throws<ArgumentException>(() => AccessGrant.BuildSql(["db_datareader", role]));
        Assert.Contains("not allowed", ex.Message);
    }

    [Fact]
    public void Empty_role_list_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => AccessGrant.BuildSql([]));
    }

    [Fact]
    public void Batch_creates_user_from_client_id_sid_without_graph_lookup()
    {
        var sql = AccessGrant.BuildSql(["db_datareader", "db_datawriter", "db_datareader"]);

        Assert.Contains("CONVERT(varbinary(16), CONVERT(uniqueidentifier, @clientId))", sql);
        Assert.Contains("WITH SID = ", sql);
        Assert.Contains("TYPE = E", sql);
        Assert.DoesNotContain("FROM EXTERNAL PROVIDER", sql);
        // Stale user (same name, different SID) is dropped and recreated.
        Assert.Contains("@existing <> @sid", sql);
        Assert.Contains("DROP USER", sql);
        // Duplicates collapse to one ALTER ROLE each.
        Assert.Equal(1, Count(sql, "QUOTENAME(N'db_datareader') + N' ADD MEMBER"));
        Assert.Equal(1, Count(sql, "QUOTENAME(N'db_datawriter') + N' ADD MEMBER"));
    }

    [Fact]
    public void Principal_name_only_reaches_dynamic_sql_through_quotename()
    {
        var sql = AccessGrant.BuildSql(["db_datareader"]);
        var concatenations = sql.Split('\n').Where(l => l.Contains("SET @stmt") && l.Contains("@name"));
        Assert.All(concatenations, line => Assert.Contains("QUOTENAME(@name)", line));
        Assert.DoesNotContain("+ @name", sql);
    }

    private static int Count(string s, string needle)
    {
        var n = 0;
        for (var i = s.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = s.IndexOf(needle, i + 1, StringComparison.Ordinal))
        {
            n++;
        }
        return n;
    }
}

public sealed class MigratorScriptTests
{
    [Fact]
    public void Scripts_are_embedded_in_name_order()
    {
        var scripts = Migrator.LoadScripts();

        Assert.NotEmpty(scripts);
        Assert.Equal(scripts.Select(s => s.Name).Order(StringComparer.Ordinal), scripts.Select(s => s.Name));
        Assert.All(scripts, s => Assert.Matches(@"^\d{3}_[a-z0-9_]+\.sql$", s.Name));
        // The migrator runs each script as one batch.
        Assert.All(scripts, s => Assert.DoesNotMatch(@"(?im)^\s*GO\s*$", s.Sql));
    }
}

[Collection("sql")]
public sealed class DbSetupSqlTests(SqlFixture sql)
{
    [Fact]
    public async Task Migrations_run_once_and_are_journaled()
    {
        sql.SkipIfUnavailable();

        await using var connection = new SqlConnection(sql.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        // The fixture already migrated; a second run is a no-op.
        var ran = await Migrator.ApplyAsync(connection, TestContext.Current.CancellationToken);
        Assert.Empty(ran);

        var journaled = await sql.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.SchemaVersions");
        Assert.Equal(Migrator.LoadScripts().Count, journaled);
        Assert.NotNull(await sql.ScalarAsync<object>("SELECT OBJECT_ID(N'dbo.Visits', N'U')"));
    }

    [Fact]
    public async Task Grant_batch_compiles_on_sql_server()
    {
        sql.SkipIfUnavailable();

        // NOEXEC compiles the outer batch without running it, so this checks T-SQL syntax and
        // parameter use without needing Entra (TYPE = E only exists on Azure SQL).
        await using var connection = new SqlConnection(sql.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var cmd = new SqlCommand(
            "SET NOEXEC ON;\n" + AccessGrant.BuildSql(AccessGrant.AllowedRoles) + "\nSET NOEXEC OFF;", connection);
        cmd.Parameters.AddWithValue("@name", "id-test");
        cmd.Parameters.AddWithValue("@clientId", Guid.NewGuid().ToString());

        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Client_id_converts_to_the_sid_bytes_entra_uses()
    {
        sql.SkipIfUnavailable();

        // Entra app SIDs are the client ID in .NET Guid.ToByteArray() order (mixed-endian).
        var clientId = Guid.Parse("0e2a3b4c-5d6e-4f70-8192-a3b4c5d6e7f8");
        await using var connection = new SqlConnection(sql.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var cmd = new SqlCommand("SELECT CONVERT(varbinary(16), CONVERT(uniqueidentifier, @id))", connection);
        cmd.Parameters.AddWithValue("@id", clientId.ToString());

        var bytes = (byte[])(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
        Assert.Equal(clientId.ToByteArray(), bytes);
    }
}

    public static object DequeueBatch(object data = null)
    {
        if (data == null) return null;
        return data;
    }

    public static object DequeueBatch(object data = null)
    {
        if (data == null) return null;
        return data;
    }