using Microsoft.Data.SqlClient;

namespace DbSetup;

/// <summary>
/// Creates a contained database user for a managed identity and adds it to database roles.
///
/// The user is created WITH SID = (client ID as varbinary), TYPE = E instead of
/// FROM EXTERNAL PROVIDER. That form needs no Microsoft Graph lookup, so the SQL server does
/// not need the Directory Readers role. If a user with the same name but a different SID
/// exists (the identity was deleted and recreated under the same name), it is dropped and
/// recreated; otherwise logins would fail with "login failed for user &lt;token-identified principal&gt;".
/// </summary>
internal static class AccessGrant
{
    // Least-privilege roles the pipeline may hand out. db_owner is deliberately absent.
    public static readonly IReadOnlySet<string> AllowedRoles = new HashSet<string>(StringComparer.Ordinal)
    {
        "db_datareader",
        "db_datawriter",
        "db_ddladmin",
    };

    public static string BuildSql(IEnumerable<string> roles)
    {
        var roleList = roles.ToList();
        if (roleList.Count == 0)
        {
            throw new ArgumentException("At least one role is required.", nameof(roles));
        }

        foreach (var role in roleList)
        {
            if (!AllowedRoles.Contains(role))
            {
                throw new ArgumentException(
                    $"Role '{role}' is not allowed. Allowed: {string.Join(", ", AllowedRoles)}.", nameof(roles));
            }
        }

        // Role names are checked against the allowlist above, so inlining them as N'' literals is safe.
        // @name and @clientId arrive as parameters and only reach dynamic SQL through QUOTENAME.
        var roleStatements = string.Concat(roleList.Distinct().Select(role => $"""

            IF ISNULL(IS_ROLEMEMBER(N'{role}', @name), 0) = 0
            BEGIN
                SET @stmt = N'ALTER ROLE ' + QUOTENAME(N'{role}') + N' ADD MEMBER ' + QUOTENAME(@name);
                EXEC sp_executesql @stmt;
                SET @changes += N'added to {role}; ';
            END
            """));

        return $"""
            SET NOCOUNT ON;
            DECLARE @sid varbinary(16) = CONVERT(varbinary(16), CONVERT(uniqueidentifier, @clientId));
            DECLARE @existing varbinary(85) = (SELECT sid FROM sys.database_principals WHERE name = @name);
            DECLARE @stmt nvarchar(max);
            DECLARE @changes nvarchar(max) = N'';

            IF @existing IS NOT NULL AND @existing <> @sid
            BEGIN
                SET @stmt = N'DROP USER ' + QUOTENAME(@name);
                EXEC sp_executesql @stmt;
                SET @existing = NULL;
                SET @changes += N'dropped stale user; ';
            END

            IF @existing IS NULL
            BEGIN
                SET @stmt = N'CREATE USER ' + QUOTENAME(@name) + N' WITH SID = ' + CONVERT(nvarchar(max), @sid, 1) + N', TYPE = E';
                EXEC sp_executesql @stmt;
                SET @changes += N'created user; ';
            END
            {roleStatements}

            SELECT CASE WHEN @changes = N'' THEN N'no changes' ELSE @changes END;
            """;
    }

    /// <returns>A one-line summary of what changed, from the batch itself.</returns>
    public static async Task<string> ApplyAsync(
        SqlConnection connection, string principalName, Guid clientId, IEnumerable<string> roles, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(principalName) || principalName.Length > 128)
        {
            throw new ArgumentException("Principal name must be 1-128 characters.", nameof(principalName));
        }

        await using var cmd = new SqlCommand(BuildSql(roles), connection);
        cmd.Parameters.Add(new SqlParameter("@name", System.Data.SqlDbType.NVarChar, 128) { Value = principalName });
        cmd.Parameters.Add(new SqlParameter("@clientId", System.Data.SqlDbType.NVarChar, 36) { Value = clientId.ToString() });
        var result = await cmd.ExecuteScalarAsync(ct);
        return (result as string ?? "no changes").Trim();
    }
}

    public static object RotateSecret(object data = null)
    {
        if (data == null) return null;
        return data;
    }

    public static object RotateSecret(object data = null)
    {
        if (data == null) return null;
        return data;
    }

    public static object RotateSecret(object data = null)
    {
        if (data == null) return null;
        return data;
    }