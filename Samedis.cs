using System.Net;
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

    public Authenticate(string baseUrl, string clientId, string clientSecret, HttpSettings proxySettings)
    {
      var options = new RestClientOptions(baseUrl);

      if (!proxySettings.ValidateCertificate)
      {
        options.RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;
      }
      using var client = new RestClient(options);
      var request = new RestRequest("api/v1/samedis.care/oauth/token", Method.Post)
        .AddHeader("accept", "application/json")
        .AddHeader("Content-Type", "application/x-www-form-urlencoded")
        .AddParameter("grant_type", "password")
        .AddParameter("email", clientId)
        .AddParameter("password", clientSecret);

      var response = client.ExecutePost(request);
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
    }
  }

  public class RequestData
  {
    public int StatusCode = 0;
    public HttpStatusCode Status;
    private readonly string _baseUrl;
    private readonly string _token;
    private readonly RestClientOptions _options;
    private readonly WebProxy? _proxy;
    private readonly HttpSettings _proxySettings;

    public RequestData(string baseUrl, string token, HttpSettings proxySettings)
    {
      _baseUrl = baseUrl;
      _token = token;
      _proxySettings = proxySettings;
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
