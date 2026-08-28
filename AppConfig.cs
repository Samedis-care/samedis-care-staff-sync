using SamedisCare.Helper.Data;

namespace SamedisStaffSync
{

  public class AppConfig
  {
    public AuthConfig Auth { get; set; } = new AuthConfig();
    public SamedisConfig Samedis { get; set; } = new SamedisConfig();
    public LoggingConfig Logging { get; set; } = new LoggingConfig();
    public HttpConfig Http { get; set; } = new HttpConfig();
    public string ImportMode { get; set; } = "excel";
    public string ImportFile { get; set; } = "";
    public string CsvDelimiter { get; set; } = ";";
    public ImportSqlConfig ImportSql { get; set; } = new ImportSqlConfig();
    public LdapConfig ImportLdap { get; set; } = new LdapConfig();
    public TestingConfig Testing { get; set; } = new TestingConfig();
    public OptionsConfig Options { get; set; } = new OptionsConfig();

  }

  public class AuthConfig
  {
    public string Uri { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
  }

  public class SamedisConfig
  {
    public string Uri { get; set; } = "";
    public string ApiVersion { get; set; } = "";
    public string TenantId { get; set; } = "";
  }

  public class LoggingConfig
  {
    public int Level { get; set; }
    public int Mode { get; set; }
  }

  public class HttpConfig
  {
    public bool ValidCertificate { get; set; }
    public string Proxy { get; set; } = "";
    public string ProxyUsername { get; set; } = "";
    public string ProxyPassword { get; set; } = "";

    /// <summary>
    /// Hard timeout per HTTP request, in seconds. Before the migration to
    /// SamedisCare.Api this tool set no timeout at all, so the default here is
    /// deliberately generous — LDAP/SAP-driven runs can produce slow bulk calls.
    /// Lower it only if the endpoint is known to respond quickly.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 300;
  }

  /// <summary>
  /// Inherits the connection fields from SamedisCare.Helper, so config.yml keeps its
  /// database_type/server/port/... keys and nothing has to be copied across.
  /// </summary>
  public class ImportSqlConfig : DbConnectionSettings
  {
    public string StaffQuery { get; set; } = "";
  }

  public class LdapConfig
  {
    public string Server { get; set; } = "";
    public bool Ssl { get; set; }
    public string Path { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string Mapping { get; set; } = "";
    public string Filter { get; set; } = "";
  }

  public class TestingConfig
  {
    public bool Active { get; set; }
  }

  public class OptionsConfig
  {
    public bool CreatePositions { get; set; }
    public bool CreateDepartments { get; set; }
    public bool LoginAllowed { get; set; }
  }
}
