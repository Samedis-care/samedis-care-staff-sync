using System.Data;
using System.Data.Common;

namespace SamedisStaffSync
{
  class DbHelper
  {
    public static string GetConnectionString(ImportSqlConfig config)
    {
      return config.DatabaseType switch
      {
        DatabaseType.SqlServer => !string.IsNullOrEmpty(config.Port)
                                ? $"Data Source={config.Server},{config.Port};Initial Catalog={config.Database};User Id={config.Username};Password={config.Password};"
                                : $"Data Source={config.Server};Initial Catalog={config.Database};User Id={config.Username};Password={config.Password};",
        DatabaseType.MySql => $"Server={config.Server};Port={config.Port};Database={config.Database};User Id={config.Username};Password={config.Password};AllowPublicKeyRetrieval={config.AllowPublicKeyRetrieval};",
        DatabaseType.SQLite => $"Data Source={config.Server};",
        DatabaseType.Oracle => $"User Id={config.Username};Password={config.Password};Data Source={config.Server};",
        _ => throw new NotSupportedException("Unsupported database type"),
      };
    }

    public static DataSet ExecuteQuery(DatabaseType provider, string connectionString, string query)
    {
      DataSet dataSet = new();
      DbProviderFactory factory = provider switch
      {
        DatabaseType.SqlServer => System.Data.SqlClient.SqlClientFactory.Instance,
        DatabaseType.MySql => MySql.Data.MySqlClient.MySqlClientFactory.Instance,
        DatabaseType.SQLite => Microsoft.Data.Sqlite.SqliteFactory.Instance,
        DatabaseType.Oracle => Oracle.ManagedDataAccess.Client.OracleClientFactory.Instance,
        _ => throw new NotSupportedException("Unsupported database type"),
      };
      using (DbConnection connection = factory.CreateConnection() ?? throw new InvalidOperationException("Failed to create a database connection."))
      {
        connection.ConnectionString = connectionString;
        connection.Open();

        using DbCommand command = factory.CreateCommand() ?? throw new InvalidOperationException("Failed to create a database command.");
        command.Connection = connection;
        command.CommandText = query;

        using DbDataReader reader = command.ExecuteReader();
        DataTable dataTable = new();
        dataTable.Load(reader);
        dataSet.Tables.Add(dataTable);
      }
      return dataSet;
    }
  }
}