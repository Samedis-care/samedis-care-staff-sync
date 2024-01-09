using Newtonsoft.Json;
using ExcelDataReader;
using System.Data;
using Newtonsoft.Json.Linq;

namespace SamedisStaffSync;

internal class Program
{
  static void Main(string[] args)
  {

    // set log
    var helper = new Helper
    {
      LogFile = "Logfile_" + DateTime.Now.ToShortDateString() + ".log",
    };

    // read config
    var ymlFilePath = "config.yml";
    if ( !File.Exists(ymlFilePath) )
      helper.MessageAndExit($"The file {ymlFilePath} does not exists. Stopping Import.");

    var settings = new ConfigProviderYaml(ymlFilePath);

    var authenticationUrl = settings.Get<string>("auth.uri");
    var clientId = settings.Get<string>("auth.client_id");
    var clientSecret = settings.Get<string>("auth.client_secret");
    var samedisUrl = settings.Get<string>("samedis.uri");
    var apiVersion = settings.Get<string>("samedis.api_version");
    var tenantId = settings.Get<string>("samedis.tenant_id");
    var proxy = settings.Get<string>("http.proxy");
    var proxyUser = settings.Get<string>("http.proxy_username");
    var proxyPass = settings.Get<string>("http.proxy_password");
    var validateCertificate = settings.Get<bool>("http.valid_certificate");
    helper.LogLevel = settings.Get<int>("logging.level");
    helper.LogMode = settings.Get<int>("logging.mode");

    helper.Message("Sync started.", 1);

    // import file exists?
    var importFile = settings.Get<string>("import_file");
    if ( !File.Exists(importFile) ) {
      helper.Message($"The file {importFile} does not exists. Stopping Import.", 1, "ERROR");
      return;
    }

    // init authentication
    if ( authenticationUrl.Length == 0 || clientId.Length == 0 || clientSecret.Length == 0) {
      helper.Message($"Authentication configuration invalid, please check config.yml.", 1, "ERROR");
      return;
    }
    var samedisAuth = new SamedisAuthenticator(authenticationUrl, clientId, clientSecret) {
      Proxy = proxy,
      ProxyUsername = proxyUser,
      ProxyPassword = proxyPass,
      ValidateCertificate = validateCertificate,
    };
    samedisAuth.GetCurrentUser();
    helper.Message($"Credential checkup Status: {samedisAuth.StatusCode} {samedisAuth.Status} User: {samedisAuth.CurrentUser}", 1);
    var bearerToken = samedisAuth.BearerToken;

    //define resource
    var staffResource = $"/api/{apiVersion}/tenants/{tenantId}/staffs";
    var samedisClient = new RequestData(samedisUrl, bearerToken)
    {
      Proxy = proxy,
      ProxyUsername = proxyUser,
      ProxyPassword = proxyPass,
      ValidateCertificate = validateCertificate,
    };

    //check permissions
    var requestResource = staffResource + "?limit=0";
    var client = samedisClient.Get(requestResource);
    if ( samedisClient.StatusCode >= 400 ) {
      var record = JsonConvert.DeserializeObject<Staffs.Root>(client);
      helper.Message($"Sync stopped. {samedisClient.StatusCode} {record.Meta.Msg.Message}", 1, "ERROR");
      return;
    }

    // read excel file
    System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
    using var stream = File.Open(importFile, FileMode.Open, FileAccess.Read, FileShare.Read);

    // Auto-detect format, supports:
    //  - Binary Excel files (2.0-2003 format; *.xls)
    //  - OpenXml Excel files (2007 format; *.xlsx, *.xlsb)
    using var reader = ExcelReaderFactory.CreateReader(stream);

    var result = reader.AsDataSet(new ExcelDataSetConfiguration()
    {
      UseColumnDataType = true,
      FilterSheet = (tableReader, sheetIndex) => true,
      ConfigureDataTable = (tableReader) => new ExcelDataTableConfiguration()
      {
        EmptyColumnNamePrefix = "Column",
        UseHeaderRow = true,
      }
    });

    // validate excel * excel format equal to export
    string[] excelColumns = {"Nachname","Vorname","Personalnummer","Eintrittsdatum","Austrittsdatum","E-Mail","Titel","Bemerkungen","Handynummer"}; //,"Id"};
    // import excel format
    //string[] excelColumns = {"Titel","Nachname","Mitarbeiternr.","E-Mail","Titel","Notizen","Handynummer","Beitritt am","Austritt am"};
    if (!Helper.CheckColumnsExist(result.Tables[0], excelColumns))
      helper.MessageAndExit("Invalid Excel file, stopping import.");

    var filter = "?gridfilter={\"employee_no\": {\"filterType\": \"text\", \"type\": \"equals\", \"filter\": \"_EMPLOYEENO_\"}}";

    foreach (DataTable table in result.Tables)
    {

      helper.Message($"Number of records: {table.Rows.Count}.", 1);

      foreach (DataRow row in table.Rows)
      {
        //validate employee no
        var tmpEmpl = !string.IsNullOrEmpty(row["Personalnummer"].ToString())
                    ? row["Personalnummer"].ToString()
                    : helper.MessageAndExit($"Missing EmployeeNo for \"{row["Nachname"]}\".");

        //validate date fields
        var tmpJoin = row["Eintrittsdatum"].ToString();
        if (DateTime.TryParse(row["Eintrittsdatum"].ToString(), out DateTime parsedJoin))
          tmpJoin = parsedJoin.ToString("dd.MM.yyyy");
        else
          helper.MessageAndExit($"Invalid left date found in Excel: {row["Eintrittsdatum"]}");
        var tmpLeft = row["Austrittsdatum"].ToString();
        if ( tmpLeft?.ToString().Length > 0 ) {
          if (DateTime.TryParse(row["Austrittsdatum"].ToString(), out DateTime parsedLeft))
            tmpLeft = parsedLeft.ToString("dd.MM.yyyy");
          else
            helper.MessageAndExit($"Invalid left date found in Excel: {row["Austrittsdatum"]}");
          if (Convert.ToDateTime(tmpLeft) < Convert.ToDateTime(tmpJoin))
            helper.MessageAndExit($"Left date {row["Austrittsdatum"]} is before join date {row["Eintrittsdatum"]} for \"{row["Nachname"]}\".");
        }

        var attributes = new Staffs.Attributes
        {
          Title = row["Titel"]?.ToString()?.Trim(),
          LastName = row["Nachname"]?.ToString()?.Trim(),
          FirstName = row["Vorname"]?.ToString()?.Trim(),
          Email = row["E-Mail"]?.ToString()?.Trim(),
          MobileNumber = row["Handynummer"]?.ToString()?.Trim(),
          Left = tmpLeft,
          Joined = tmpJoin,
          EmployeeNo = tmpEmpl,
          Notes = row["Bemerkungen"]?.ToString()?.Trim()
        };

        // check for Id, if exists, include
        if (Helper.CheckColumnsExist(result.Tables[0], new string[] {"Id"}))
          attributes.Id = row["Id"].ToString();

        // Convert Staff object to JObject
        var dataObject = new JObject();
        // build object
        foreach (var property in typeof(Staffs.Attributes).GetProperties())
        {
          var jsonPropertyAttribute = (JsonPropertyAttribute)Attribute.GetCustomAttribute(property, typeof(JsonPropertyAttribute));
          var propertyName = jsonPropertyAttribute?.PropertyName ?? property.Name.ToLower();
          if (propertyName == "Id") continue;
          var value = property.GetValue(attributes);
          if (value != null && !value.Equals(default))
            dataObject[propertyName] = JToken.FromObject(value);
        }
        var staffObject = new JObject { ["data"] = dataObject };

        var staffBody = JsonConvert.SerializeObject(staffObject, Formatting.Indented);
        helper.Message(staffBody, 2);

        // check if exists
        requestResource = staffResource;
        requestResource += attributes.Id != null
                        ? $"/{attributes.Id}" 
                        : filter.Replace("_EMPLOYEENO_", row["Personalnummer"].ToString(), StringComparison.OrdinalIgnoreCase);

        client = samedisClient.Get(requestResource);
        var record = JsonConvert.DeserializeObject<Staffs.Root>(client);
        var totalRecords = record != null ? record.Meta.Total : 0;

        // put or post
        if ( totalRecords > 0 ) {
          var recordId = record.Data[0].Attributes.Id;
          helper.Message($"Staff EmployeeNo {row["Personalnummer"]} exists with record {recordId}", 2);
          client = samedisClient.Put(staffResource, recordId, staffBody);
          helper.Message($"Status Code: {samedisClient.StatusCode} {samedisClient.Status}", 2);
        } else {
          helper.Message($"Staff EmployeeNo {row["Personalnummer"]} does not exists", 2);
          client = samedisClient.Post(staffResource, staffBody);
          helper.Message($"Status Code: {samedisClient.StatusCode} {samedisClient.Status}", 2);
        }

      }
    }

    helper.Message("Sync finised.", 1);
  }
}