using System;
using System.IO;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

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
    public ImportSqlConfig ImportSql { get; set; } = new ImportSqlConfig();

    public static AppConfig LoadFromYaml(string filePath)
    {
      using var input = File.OpenText(filePath);
      var deserializerBuilder = new DeserializerBuilder().WithNamingConvention(new UnderscoredNamingConvention());
      var deserializer = deserializerBuilder.Build();
      var result = deserializer.Deserialize<AppConfig>(input);
      return result;
    }
  }

  public class AuthConfig
  {
    public string Uri { get; set; }
    public string ClientId { get; set; }
    public string ClientSecret { get; set; }
  }

  public class SamedisConfig
  {
    public string Uri { get; set; }
    public string ApiVersion { get; set; }
    public string TenantId { get; set; }
  }

  public class LoggingConfig
  {
    public int Level { get; set; }
    public int Mode { get; set; }
  }

  public class HttpConfig
  {
    public bool ValidCertificate { get; set; }
    public string Proxy { get; set; }
    public string ProxyUsername { get; set; }
    public string ProxyPassword { get; set; }
  }

  public class ImportSqlConfig
  {
    public DatabaseType DatabaseType { get; set; }
    public string Server { get; set; }
    public string Port { get; set; }
    public string Database { get; set; }
    public string Username { get; set; }
    public string Password { get; set; }
    public bool AllowPublicKeyRetrieval { get; set; }
    public string StaffQuery { get; set; }
  }

}