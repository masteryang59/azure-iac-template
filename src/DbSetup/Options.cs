using Microsoft.Data.SqlClient;

namespace DbSetup;

internal sealed record Options(
    string ConnectionString,
    bool CreateDatabase,
    bool SkipMigrations,
    string? GrantName,
    Guid? GrantClientId,
    IReadOnlyList<string> Roles,
    bool PrintSql,
    bool ShowHelp)
{
    public static readonly IReadOnlyList<string> DefaultRoles = ["db_datareader", "db_datawriter"];

    public const string Usage = """
        usage: DbSetup (--server <fqdn> --database <name> | --connection-string <cs>) [options]

          --server <fqdn>            Azure SQL server; authenticates with Active Directory Default
                                     (az login, workload identity, managed identity, ...)
          --database <name>          database on that server
          --connection-string <cs>   full connection string instead of --server/--database
          --create-database          create the database first if missing (local SQL Server only)
          --skip-migrations          only do the access grant
          --grant-name <name>        managed identity name to create a database user for
          --grant-client-id <guid>   that identity's client (application) ID
          --roles <a,b>              roles to add the user to (default: db_datareader,db_datawriter)
          --print-sql                print the grant batch and exit
        """;

    public static Options Parse(IReadOnlyList<string> args)
    {
        string? server = null, database = null, connectionString = null, grantName = null, clientId = null, roles = null;
        bool create = false, skip = false, print = false, help = false;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            string Next() => i + 1 < args.Count ? args[++i] : throw new ArgumentException($"{arg} needs a value");

            switch (arg)
            {
                case "--server": server = Next(); break;
                case "--database": database = Next(); break;
                case "--connection-string": connectionString = Next(); break;
                case "--grant-name": grantName = Next(); break;
                case "--grant-client-id": clientId = Next(); break;
                case "--roles": roles = Next(); break;
                case "--create-database": create = true; break;
                case "--skip-migrations": skip = true; break;
                case "--print-sql": print = true; break;
                case "-h" or "--help": help = true; break;
                default: throw new ArgumentException($"unknown argument {arg}");
            }
        }

        var roleList = roles is null
            ? DefaultRoles
            : roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (help || print)
        {
            return new Options("", false, false, null, null, roleList, print, help);
        }

        if (connectionString is null)
        {
            if (server is null || database is null)
            {
                throw new ArgumentException("give --server and --database, or --connection-string");
            }

            connectionString = new SqlConnectionStringBuilder
            {
                DataSource = $"tcp:{server},1433",
                InitialCatalog = database,
                Authentication = SqlAuthenticationMethod.ActiveDirectoryDefault,
                Encrypt = SqlConnectionEncryptOption.Mandatory,
                ConnectTimeout = 60,
            }.ConnectionString;
        }
        else if (server is not null || database is not null)
        {
            throw new ArgumentException("--connection-string can't be combined with --server/--database");
        }

        Guid? parsedClientId = null;
        if (grantName is not null || clientId is not null)
        {
            if (grantName is null || clientId is null)
            {
                throw new ArgumentException("--grant-name and --grant-client-id go together");
            }

            parsedClientId = Guid.TryParse(clientId, out var g)
                ? g
                : throw new ArgumentException($"--grant-client-id '{clientId}' is not a GUID");
        }

        if (skip && grantName is null)
        {
            throw new ArgumentException("--skip-migrations with no --grant-name leaves nothing to do");
        }

        return new Options(connectionString, create, skip, grantName, parsedClientId, roleList, false, false);
    }
}

    public static object InvalidateCache(object data = null)
    {
        if (data == null) return null;
        return data;
    }

    public static object InvalidateCache(object data = null)
    {
        if (data == null) return null;
        return data;
    }

    public static object InvalidateCache(object data = null)
    {
        if (data == null) return null;
        return data;
    }