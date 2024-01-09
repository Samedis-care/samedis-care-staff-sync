using System.Net;
using RestSharp;

namespace SamedisStaffSync
{
  public class RequestData
  {
    public int StatusCode = 0;
    public HttpStatusCode Status;
    public bool ValidateCertificate = true;
    public string Proxy = "";
    public string ProxyUsername = "";
    public string ProxyPassword = "";
    readonly string _baseUrl;
    readonly string _token;

    public RequestData(string baseUrl, string token)
    {
      _baseUrl = baseUrl;
      _token = token;
    }

    public string Get(string resource)
    {
      var options = new RestClientOptions(_baseUrl);
      // turn off SSL check for local development or self signed certificates
      if (!ValidateCertificate)
        options.RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;

      if ( Proxy.Length > 0) {
        var proxy = new WebProxy(Proxy);
        if ( ProxyUsername.Length > 0)
          proxy.Credentials = new NetworkCredential(ProxyUsername, ProxyPassword);
        options.Proxy = proxy;
      }

      using var client = new RestClient(options);

      var request = new RestRequest(resource, Method.Get)
        .AddHeader("accept", "application/json")
        .AddHeader("Content-Type", "application/json")
        .AddHeader("Authorization", $"Bearer {_token}");

      var response = client.ExecuteGet(request);

      if (response.StatusCode == HttpStatusCode.TooManyRequests)
      {
        var retryAfterHeader = response.Headers?.FirstOrDefault(h => h.Name?.Equals("Retry-After", StringComparison.OrdinalIgnoreCase) == true);
        if (retryAfterHeader != null && retryAfterHeader.Value != null)
        {
          if (int.TryParse(retryAfterHeader.Value.ToString(), out var retryAfterSeconds))
          {
            Thread.Sleep(retryAfterSeconds * 1000);
            response = client.ExecuteGet(request);
          }
        }
      }

      Status = response.StatusCode;
      StatusCode = (int)Status;

      if (response.Content == null) return "";
      return response.Content;
    }

    public string Post(string resource, string content)
    {
      var options = new RestClientOptions(_baseUrl);
      // turn off SSL check for local development or self signed certificates
      if (!ValidateCertificate)
        options.RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;

      if ( Proxy.Length > 0) {
        var proxy = new WebProxy(Proxy);
        if ( ProxyUsername.Length > 0)
          proxy.Credentials = new NetworkCredential(ProxyUsername, ProxyPassword);
        options.Proxy = proxy;
      }

      using var client = new RestClient(options);

      var request = new RestRequest(resource, Method.Post)
        .AddHeader("accept", "application/json")
        .AddHeader("Content-Type", "application/json")
        .AddHeader("Authorization", $"Bearer {_token}")
        .AddJsonBody(content);

      var response = client.ExecutePost(request);

      if (response.StatusCode == HttpStatusCode.TooManyRequests)
      {
        var retryAfterHeader = response.Headers?.FirstOrDefault(h => h.Name?.Equals("Retry-After", StringComparison.OrdinalIgnoreCase) == true);
        if (retryAfterHeader != null && retryAfterHeader.Value != null)
        {
          if (int.TryParse(retryAfterHeader.Value.ToString(), out var retryAfterSeconds))
          {
            Thread.Sleep(retryAfterSeconds * 1000);
            response = client.ExecutePost(request);
          }
        }
      }

      Status = response.StatusCode;
      StatusCode = (int)Status;

      if (response.Content == null) return "";
      return response.Content;
    }

    public string Put(string resource, string id, string content)
    {
      var options = new RestClientOptions(_baseUrl);
      // turn off SSL check for local development or self signed certificates
      if (!ValidateCertificate)
        options.RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;

      if ( Proxy.Length > 0) {
        var proxy = new WebProxy(Proxy);
        if ( ProxyUsername.Length > 0)
          proxy.Credentials = new NetworkCredential(ProxyUsername, ProxyPassword);
        options.Proxy = proxy;
      }

      using var client = new RestClient(options);

      var request = new RestRequest(resource + "/" + id, Method.Put)
        .AddHeader("accept", "application/json")
        .AddHeader("Content-Type", "application/json")
        .AddHeader("Authorization", $"Bearer {_token}")
        .AddJsonBody(content);

      var response = client.ExecutePut(request);

      if (response.StatusCode == HttpStatusCode.TooManyRequests)
      {
        var retryAfterHeader = response.Headers?.FirstOrDefault(h => h.Name?.Equals("Retry-After", StringComparison.OrdinalIgnoreCase) == true);
        if (retryAfterHeader != null && retryAfterHeader.Value != null)
        {
          if (int.TryParse(retryAfterHeader.Value.ToString(), out var retryAfterSeconds))
          {
            Thread.Sleep(retryAfterSeconds * 1000);
            response = client.ExecutePut(request);
          }
        }
      }

      Status = response.StatusCode;
      StatusCode = (int)Status;

      if (response.Content == null) return "";
      return response.Content;
    }
  }
  
}