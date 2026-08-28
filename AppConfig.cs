using SamedisCare.Helper.Config;

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

    /// <summary>
    /// Loads config.yml through SamedisCare.Helper, which replaces the LoadFromYaml copy
    /// that six of the sync tools carried.
    /// </summary>
    /// <remarks>
    /// ignoreUnmatchedProperties stays FALSE, which is what this tool did before: an
    /// unknown key in config.yml fails the run rather than being skipped silently, so a
    /// typo cannot quietly disable an option.
    /// </remarks>
    public static AppConfig LoadFromYaml(string filePath)
      => ConfigStore.Load<AppConfig>(filePath, ignoreUnmatchedProperties: false);
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

  public class ImportSqlConfig
  {
    public DatabaseType DatabaseType { get; set; }
    public string Server { get; set; } = "";
    public string Port { get; set; } = "";
    public string Database { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public bool AllowPublicKeyRetrieval { get; set; }
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
