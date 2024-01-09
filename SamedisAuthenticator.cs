using System.Net;
using RestSharp;
using Newtonsoft.Json;

namespace SamedisStaffSync
{
  public class SamedisAuthenticator
  {
    public string BearerToken = "";
    public string RefreshToken = "";
    public string App = "";
    public string CurrentUser = "";
    public int StatusCode = 0;
    public HttpStatusCode Status;
    public bool ValidateCertificate = true;
    public string Proxy = "";
    public string ProxyUsername = "";
    public string ProxyPassword = "";

    readonly string _baseUrl;
    readonly string _clientId;
    readonly string _clientSecret;

    public SamedisAuthenticator(string baseUrl, string clientId, string clientSecret)
    {
      _baseUrl = baseUrl;
      _clientId = clientId;
      _clientSecret = clientSecret;
    }

    public string GetCurrentUser()
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

      var request = new RestRequest("api/v1/samedis.care/oauth/token", Method.Post)
        .AddHeader("accept", "application/json")
        .AddHeader("Content-Type", "application/x-www-form-urlencoded")
        .AddParameter("grant_type", "password")
        .AddParameter("email", _clientId)
        .AddParameter("password", _clientSecret);

      var response = client.ExecutePost(request);
      Status = response.StatusCode;
      StatusCode = (int)Status;

      if (response.Content == null) return "";
      var currentUser = JsonConvert.DeserializeObject<CurrentUser.Root>(response.Content);
      BearerToken = currentUser!.meta.token;
      RefreshToken = currentUser!.meta.refresh_token;
      App = currentUser!.meta.app;
      CurrentUser = currentUser!.data.attributes.email;
      return response.Content;
    }
  }
}