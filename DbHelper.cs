using System.Data;
using System.Data.Common;
using SamedisCare.Helper.Data;

namespace SamedisStaffSync
{
  /// <summary>
  /// Maps this tool's configured database type onto a provider factory and delegates the
  /// actual work to SamedisCare.Helper.Data.Database.
  /// <para>
  /// The factory mapping stays here on purpose: it is the only place that names a driver,
  /// so the shared package needs no driver reference and no consumer pulls in providers it
  /// does not use.
  /// </para>
  /// </summary>
  static class DbHelper
  {
    private static DbProviderFactory Factory(DatabaseType provider) => provider switch
    {
      DatabaseType.SqlServer => System.Data.SqlClient.SqlClientFactory.Instance,
      DatabaseType.MySql => MySql.Data.MySqlClient.MySqlClientFactory.Instance,
      DatabaseType.SQLite => Microsoft.Data.Sqlite.SqliteFactory.Instance,
      _ => throw new NotSupportedException($"Unsupported database type: {provider}"),
    };

    private static DbKind Kind(DatabaseType provider) => provider switch
    {
      DatabaseType.SqlServer => DbKind.SqlServer,
      DatabaseType.MySql => DbKind.MySql,
      DatabaseType.SQLite => DbKind.Sqlite,
      _ => throw new NotSupportedException($"Unsupported database type: {provider}"),
    };

    public static string GetConnectionString(ImportSqlConfig config)
      => Database.BuildConnectionString(Kind(config.DatabaseType), new DbConnectionSettings
      {
        Server = config.Server,
        Port = config.Port,
        Database = config.Database,
        Username = config.Username,
        Password = config.Password,
        AllowPublicKeyRetrieval = config.AllowPublicKeyRetrieval,
      });

    public static DataSet ExecuteQuery(DatabaseType provider, string connectionString, string query)
      => Database.QueryAsDataSet(Factory(provider), connectionString, query);
  }
}
