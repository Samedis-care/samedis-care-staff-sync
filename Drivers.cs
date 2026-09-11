using System.Data.Common;
using SamedisCare.Helper.Data;

namespace SamedisStaffSync
{
  /// <summary>
  /// The one thing that cannot live in SamedisCare.Helper: naming a driver.
  /// <para>
  /// The shared package works against <see cref="DbProviderFactory"/> and references no
  /// provider, so every tool keeps only the drivers it actually needs. Everything else -
  /// connection strings, queries - comes from <see cref="DbTarget"/>.
  /// </para>
  /// </summary>
  static class Drivers
  {
    public static DbProviderFactory For(DbKind kind) => kind switch
    {
      DbKind.SqlServer => System.Data.SqlClient.SqlClientFactory.Instance,
      DbKind.MySql => MySql.Data.MySqlClient.MySqlClientFactory.Instance,
      DbKind.Sqlite => Microsoft.Data.Sqlite.SqliteFactory.Instance,
      _ => throw new NotSupportedException($"Unsupported database type: {kind}"),
    };
  }
}
