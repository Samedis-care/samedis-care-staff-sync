using System.Net;
using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;

namespace SamedisStaffSync
{
  public class HttpSettings
  {
    public string? Proxy { get; set; }
    public string? ProxyUsername { get; set; }
    public string? ProxyPassword { get; set; }
    public bool ValidateCertificate { get; set; }
  }

  public class Authenticate
  {
    public int StatusCode = 0;
    public HttpStatusCode Status;
    public string BearerToken = "";
    public string RefreshToken = "";
    public string User = "";
    public bool Debug = false;
    public int LogLevel = 0; // 0=off, 1=info, 2=debug

    public Authenticate(string baseUrl, string clientId, string clientSecret, HttpSettings proxySettings, Helper helper)
    {
      // logLevel: 0=off, 1=info, 2=debug
      LogLevel = helper.LogLevel;
      Debug = helper.LogLevel >= 2;

      static string Redact(string? value, int keepStart = 3, int keepEnd = 2)
      {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (value.Length <= keepStart + keepEnd) return new string('*', value.Length);
        return string.Concat(value.AsSpan(0, keepStart), new string('*', value.Length - keepStart - keepEnd), value.AsSpan(value.Length - keepEnd));
      }

      static string SafeUri(string baseUrlValue, string resource)
      {
        if (baseUrlValue.EndsWith("/")) return baseUrlValue + resource.TrimStart('/');
        return baseUrlValue + "/" + resource.TrimStart('/');
      }

      static string DumpResponse(RestResponse? r)
      {
        if (r == null) return "<null response>";
        var sb = new StringBuilder();
        sb.AppendLine($"ResponseStatus: {r.ResponseStatus}");
        sb.AppendLine($"HTTP StatusCode: {(int)r.StatusCode} ({r.StatusCode})");
        sb.AppendLine($"ErrorMessage: {r.ErrorMessage}");
        sb.AppendLine($"ContentType: {r.ContentType}");
        sb.AppendLine($"ContentLength: {(r.Content == null ? 0 : r.Content.Length)}");

        if (r.Headers != null && r.Headers.Any())
        {
          sb.AppendLine("Headers:");
          foreach (var h in r.Headers)
          {
            var name = h?.Name ?? "";
            var val = h?.Value?.ToString() ?? "";
            if (name.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
              val = "<redacted>";
            sb.AppendLine($"  {name}: {val}");
          }
        }

        if (r.ErrorException != null)
        {
          sb.AppendLine("Exception:");
          sb.AppendLine(r.ErrorException.ToString());
        }

        if (!string.IsNullOrEmpty(r.Content))
        {
          var preview = r.Content.Length > 800 ? string.Concat(r.Content.AsSpan(0, 800), "...") : r.Content;
          sb.AppendLine("BodyPreview:");
          sb.AppendLine(preview);
        }

        return sb.ToString();
      }

      try
      {
        var resource = "api/v1/samedis.care/oauth/token";
        var fullUrl = SafeUri(baseUrl, resource);

        helper.Message($"BaseUrl={baseUrl}", 2, "DEBUG");
        helper.Message($"FullUrl={fullUrl}", 2, "DEBUG");
        helper.Message($"ValidateCertificate={proxySettings.ValidateCertificate}", 2, "DEBUG");
        helper.Message($"Proxy={proxySettings.Proxy}", 2, "DEBUG");
        helper.Message($"ProxyUser={(string.IsNullOrEmpty(proxySettings.ProxyUsername) ? "<none>" : proxySettings.ProxyUsername)}", 2, "DEBUG");
        helper.Message($"ClientId(email)={clientId}", 2, "DEBUG");
        helper.Message($"ClientSecret(password) length={(clientSecret == null ? 0 : clientSecret.Length)} (value redacted)", 2, "DEBUG");

        try
        {
          var host = new Uri(baseUrl).Host;
          var ips = Dns.GetHostAddresses(host);
          helper.Message($"DNS {host} -> {string.Join(", ", ips.Select(i => i.ToString()))}");
        }
        catch (Exception ex)
        {
          helper.Message($"DNS resolution failed: {ex.GetType().Name}: {ex.Message}", 2, "DEBUG");
        }

        var options = new RestClientOptions(baseUrl);

        if (!proxySettings.ValidateCertificate)
        {
          options.RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;
          helper.Message("WARNING: Certificate validation is DISABLED for auth request.", 2, "DEBUG");
        }

        if (!string.IsNullOrEmpty(proxySettings.Proxy))
        {
          var proxy = new WebProxy(proxySettings.Proxy);
          if (!string.IsNullOrEmpty(proxySettings.ProxyUsername))
          {
            proxy.Credentials = new NetworkCredential(proxySettings.ProxyUsername, proxySettings.ProxyPassword);
            helper.Message("Proxy credentials: provided (password redacted).", 2, "DEBUG");
          }
          else
          {
            helper.Message("Proxy credentials: none.", 2, "DEBUG");
          }
          options.Proxy = proxy;
        }

        options.ConfigureMessageHandler = handler =>
        {
          if (handler is HttpClientHandler http)
          {
            if (!string.IsNullOrEmpty(proxySettings.Proxy))
            {
              if (!Uri.TryCreate(proxySettings.Proxy.Trim(), UriKind.Absolute, out var proxyUri))
              {
                var withScheme = "http://" + proxySettings.Proxy.Trim();
                if (!Uri.TryCreate(withScheme, UriKind.Absolute, out proxyUri))
                  throw new UriFormatException($"Invalid proxy URI: '{proxySettings.Proxy}'. Expected e.g. 'http://host:port' or 'host:port'.");
              }

              var p = new WebProxy(proxyUri)
              {
                BypassProxyOnLocal = false,
                UseDefaultCredentials = false
              };

              if (!string.IsNullOrEmpty(proxySettings.ProxyUsername))
                p.Credentials = new NetworkCredential(proxySettings.ProxyUsername, proxySettings.ProxyPassword);

              http.Proxy = p;
              http.UseProxy = true;
            }

            if (!proxySettings.ValidateCertificate)
              http.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
          }
          return handler;
        };

        using var client = new RestClient(options);

        var request = new RestRequest(resource, Method.Post)
          .AddHeader("accept", "application/json")
          .AddHeader("Content-Type", "application/x-www-form-urlencoded")
          .AddParameter("grant_type", "password")
          .AddParameter("email", clientId)
          .AddParameter("password", clientSecret);

        helper.Message("Sending auth request...");

        var sw = Stopwatch.StartNew();
        var response = client.ExecutePost(request);
        sw.Stop();

        helper.Message($"Auth request completed in {sw.ElapsedMilliseconds} ms", 2, "DEBUG");

        helper.Message(DumpResponse(response), 2, "DEBUG");

        Status = response.StatusCode;
        StatusCode = (int)Status;

        if (!string.IsNullOrEmpty(response.Content))
        {
          var root = JsonConvert.DeserializeObject<JObject>(response.Content);
          if (root != null)
          {
            var meta = root["meta"];
            var data = root["data"];
            BearerToken = meta?["token"]?.ToString() ?? string.Empty;
            RefreshToken = meta?["refresh_token"]?.ToString() ?? string.Empty;
            User = data?["attributes"]?["email"]?.ToString() ?? string.Empty;
          }
        }

        // Info: parsed results (tokens redacted)
        helper.Message($"Parsed: BearerToken={(string.IsNullOrEmpty(BearerToken) ? "<empty>" : Redact(BearerToken))}", 2, "DEBUG");
        helper.Message($"Parsed: RefreshToken={(string.IsNullOrEmpty(RefreshToken) ? "<empty>" : Redact(RefreshToken))}", 2, "DEBUG");
        helper.Message($"Parsed: User={User}", 2, "DEBUG");
      }
      catch (Exception ex)
      {
        Status = 0;
        StatusCode = 0;

        helper.Message("Authenticate threw an exception:");
        helper.Message(ex.ToString());
        throw;
      }
    }
  }

  public class RequestData
  {
    public int StatusCode = 0;
    public HttpStatusCode Status;
    public bool Debug { get; set; }
    public int LogLevel { get; set; }
    public bool TestMode { get; set; }
    private readonly string _baseUrl;
    private readonly string _token;
    private readonly RestClientOptions _options;
    private readonly WebProxy? _proxy;
    private readonly HttpSettings _proxySettings;

    public RequestData(string baseUrl, string token, HttpSettings proxySettings, int logLevel = 0, bool testMode = false)
    {
      _baseUrl = baseUrl;
      _token = token;
      _proxySettings = proxySettings;
      LogLevel = logLevel;
      Debug = logLevel >= 2;
      TestMode = testMode;
      _options = new RestClientOptions(_baseUrl);

      if (!_proxySettings.ValidateCertificate)
      {
        _options.RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;
      }

      if (!string.IsNullOrEmpty(_proxySettings.Proxy))
      {
        _proxy = new WebProxy(_proxySettings.Proxy);
        if (!string.IsNullOrEmpty(_proxySettings.ProxyUsername))
        {
          _proxy.Credentials = new NetworkCredential(_proxySettings.ProxyUsername, _proxySettings.ProxyPassword);
        }
        _options.Proxy = _proxy;
      }
    }

    public string? Get(string resource)
    {
      using var client = new RestClient(_options);
      var request = new RestRequest(resource, Method.Get)
          .AddHeader("accept", "application/json")
          .AddHeader("Content-Type", "application/json")
          .AddHeader("Authorization", $"Bearer {_token}");

      var response = client.ExecuteGet(request);
      HandleRetry(response, request, client.ExecuteGet);

      Status = response.StatusCode;
      StatusCode = (int)Status;
      if (Debug && TestMode)
        WriteDebugGetCsv(resource, response);
      return response.Content ?? string.Empty;
    }

    public string? Post(string resource, string content)
    {
      using var client = new RestClient(_options);
      var request = new RestRequest(resource, Method.Post)
          .AddHeader("accept", "application/json")
          .AddHeader("Content-Type", "application/json")
          .AddHeader("Authorization", $"Bearer {_token}")
          .AddJsonBody(content);

      var response = client.ExecutePost(request);
      HandleRetry(response, request, client.ExecutePost);

      Status = response.StatusCode;
      StatusCode = (int)Status;
      return response.Content ?? string.Empty;
    }

    public string? PostFileUload(string resource, string filePath, string name, bool primary)
    {
      using var client = new RestClient(_options);
      var request = new RestRequest(resource, Method.Post)
          .AddHeader("accept", "application/json")
          .AddHeader("Content-Type", "multipart/form-data")
          .AddHeader("Authorization", $"Bearer {_token}")
          .AddParameter("data[name]", name)
          .AddParameter("data[primary]", primary.ToString().ToLower())
          .AddFile("data[image]", filePath);

      var response = client.ExecutePost(request);
      HandleRetry(response, request, client.ExecutePost);

      Status = response.StatusCode;
      StatusCode = (int)Status;
      return response.Content ?? string.Empty;
    }

    public string? Put(string resource, string id, string content)
    {
      using var client = new RestClient(_options);
      var request = new RestRequest(resource + "/" + id, Method.Put)
          .AddHeader("accept", "application/json")
          .AddHeader("Content-Type", "application/json")
          .AddHeader("Authorization", $"Bearer {_token}")
          .AddJsonBody(content);

      var response = client.ExecutePut(request);
      HandleRetry(response, request, client.ExecutePut);

      Status = response.StatusCode;
      StatusCode = (int)Status;
      return response.Content ?? string.Empty;
    }

    private static void HandleRetry(RestResponse response, RestRequest request, Func<RestRequest, RestResponse> execute)
    {
      if (response.StatusCode == HttpStatusCode.TooManyRequests)
      {
        var retryAfterHeader = response.Headers?.FirstOrDefault(h => h.Name?.Equals("Retry-After", StringComparison.OrdinalIgnoreCase) == true);
        if (retryAfterHeader != null && retryAfterHeader.Value != null)
        {
          if (int.TryParse(retryAfterHeader.Value.ToString(), out var retryAfterSeconds))
          {
            Thread.Sleep(retryAfterSeconds * 1000);
            response = execute(request);
          }
        }
      }
    }

    private static readonly object DebugCsvLock = new object();
    private const string DebugGetCsvFile = "debug_get_requests.csv";

    private static void WriteDebugGetCsv(string resource, RestResponse response)
    {
      var headers = new[] { "Timestamp", "Method", "Resource", "StatusCode", "ResponseBody" };
      var body = response.Content ?? string.Empty;
      var preview = body.Length > 2000 ? body.Substring(0, 2000) : body;
      var row = new[]
      {
        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
        "GET",
        resource ?? string.Empty,
        ((int)response.StatusCode).ToString(),
        preview.Replace("\r", " ").Replace("\n", " ")
      };

      lock (DebugCsvLock)
      {
        var needsHeader = !File.Exists(DebugGetCsvFile) || new FileInfo(DebugGetCsvFile).Length == 0;
        using var sw = new StreamWriter(DebugGetCsvFile, append: true, Encoding.UTF8);
        if (needsHeader)
        {
          sw.WriteLine(string.Join(";", headers.Select(EscapeCsv)));
        }
        sw.WriteLine(string.Join(";", row.Select(EscapeCsv)));
      }
    }

    private static string EscapeCsv(string value)
    {
      if (string.IsNullOrEmpty(value))
        return string.Empty;
      var needsQuotes = value.Contains('"') || value.Contains(';') || value.Contains('\r') || value.Contains('\n');
      var sanitized = value.Replace("\"", "\"\"");
      return needsQuotes ? $"\"{sanitized}\"" : sanitized;
    }

    public async Task DownloadAsync(string url, string outputPath)
    {
      var handler = new HttpClientHandler();

      if (_proxySettings != null)
      {
        if (!string.IsNullOrEmpty(_proxySettings.Proxy))
        {
          var proxy = new WebProxy
          {
            Address = new Uri(_proxySettings.Proxy),
            BypassProxyOnLocal = false,
            UseDefaultCredentials = false
          };

          if (!string.IsNullOrEmpty(_proxySettings.ProxyUsername))
          {
            proxy.Credentials = new NetworkCredential(
                _proxySettings.ProxyUsername,
                _proxySettings.ProxyPassword
            );
          }

          handler.Proxy = proxy;
          handler.UseProxy = true;
        }

        if (!_proxySettings.ValidateCertificate)
        {
          handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }
      }

      using (HttpClient client = new HttpClient(handler))
      {
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();

        using (var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
        {
          await response.Content.CopyToAsync(fileStream);
        }
      }
    }
  }
}
