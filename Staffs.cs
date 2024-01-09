using Newtonsoft.Json;

namespace SamedisStaffSync
{
  public class Staffs {

    // Root myDeserializedClass = JsonConvert.DeserializeObject<Root>(myJsonResponse);
    public class Attributes
    {
      [JsonProperty("id")]
      public string? Id { get; set; }

      [JsonProperty("account")]
      public string? Account { get; set; }

      [JsonProperty("administered_briefings_count")]
      public int? AdministeredBriefingsCount { get; set; }

      [JsonProperty("avatar")]
      public string? Avatar { get; set; }

      [JsonProperty("catalog_ids")]
      public List<string>? CatalogIds { get; set; }

      [JsonProperty("email")]
      public string? Email { get; set; }

      [JsonProperty("employee_no")]
      public string? EmployeeNo { get; set; }

      [JsonProperty("first_name")]
      public string? FirstName { get; set; }

      [JsonProperty("ident_user_id")]
      public string? IdentUserId { get; set; }

      [JsonProperty("joined")]
      public string? Joined { get; set; }

      [JsonProperty("last_name")]
      public string? LastName { get; set; }

      [JsonProperty("left")]
      public string? Left { get; set; }

      [JsonProperty("login_allowed")]
      public bool? LoginAllowed { get; set; }

      [JsonProperty("manufacturer_catalog_ids")]
      public List<string>? ManufacturerCatalogIds { get; set; }

      [JsonProperty("mobile_number")]
      public string? MobileNumber { get; set; }

      [JsonProperty("notes")]
      public string? Notes { get; set; }

      [JsonProperty("title")]
      public string? Title { get; set; }
    }

    public class Data
    {
      [JsonProperty("id")]
      public string? Id { get; set; }

      [JsonProperty("type")]
      public string? Type { get; set; }

      [JsonProperty("attributes")]
      public Attributes? Attributes { get; set; }
    }

    public class Fields
    {
    }

    public class JsonApiOptions
    {
      [JsonProperty("limit")]
      public int Limit { get; set; }

      [JsonProperty("page")]
      public int Page { get; set; }

      [JsonProperty("fields")]
      public Fields? Fields { get; set; }
    }

    public class Meta
    {
      [JsonProperty("total")]
      public int Total { get; set; }

      [JsonProperty("json_api_options")]
      public JsonApiOptions? JsonApiOptions { get; set; }

      [JsonProperty("locale")]
      public string? Locale { get; set; }

      [JsonProperty("msg")]
      public Msg? Msg { get; set; }
    }

    public class Msg
    {
      [JsonProperty("success")]
      public bool Success { get; set; }

      [JsonProperty("error")]
      public string? Error { get; set; }
      [JsonProperty("message")]
      public string? Message { get; set; }
    }

    public class Root
    {
      [JsonProperty("data")]
      public List<Data>? Data { get; set; }

      [JsonProperty("meta")]
      public Meta? Meta { get; set; }
    }
  }
}