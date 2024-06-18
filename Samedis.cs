using System.Net;
using RestSharp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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

      if (response.Content != null)
      {
        var root = JsonConvert.DeserializeObject<JObject>(response.Content);
        var meta = root["meta"];
        var data = root["data"];
        BearerToken = meta["token"]?.ToString();
        RefreshToken = meta["refresh_token"]?.ToString();
        User = data["attributes"]["email"]?.ToString();
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

    public RequestData(string baseUrl, string token, HttpSettings proxySettings)
    {
      _baseUrl = baseUrl;
      _token = token;
      _options = new RestClientOptions(_baseUrl);

      if (!proxySettings.ValidateCertificate)
      {
        _options.RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;
      }

      if (!string.IsNullOrEmpty(proxySettings.Proxy))
      {
        _proxy = new WebProxy(proxySettings.Proxy);
        if (!string.IsNullOrEmpty(proxySettings.ProxyUsername))
        {
          _proxy.Credentials = new NetworkCredential(proxySettings.ProxyUsername, proxySettings.ProxyPassword);
        }
        _options.Proxy = _proxy;
      }
    }

    public string Get(string resource)
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

    public string Post(string resource, string content)
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

    public string Put(string resource, string id, string content)
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
  }
}
