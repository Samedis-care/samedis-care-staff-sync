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
    if (!File.Exists(ymlFilePath))
      helper.MessageAndExit($"The file {ymlFilePath} does not exists. Stopping Import.");

    AppConfig config = AppConfig.LoadFromYaml(ymlFilePath);
    if ( config == null) Environment.Exit(1);

    helper.LogLevel = config.Logging.Level;
    helper.LogMode = config.Logging.Mode;

    helper.Message("Sync started.", 1);

    // init authentication
    if (config.Auth.Uri.Length == 0 || config.Auth.ClientId.Length == 0 || config.Auth.ClientSecret.Length == 0)
    {
      helper.Message($"Authentication configuration invalid, please check config.yml.", 1, "ERROR");
      return;
    }

    var httpSettings = new HttpSettings()
    {
      Proxy = config.Http.Proxy,
      ProxyUsername = config.Http.ProxyUsername,
      ProxyPassword = config.Http.ProxyPassword,
      ValidateCertificate = config.Http.ValidCertificate,
    };

    var samedisAuth = new Authenticate(config.Auth.Uri, config.Auth.ClientId, config.Auth.ClientSecret, httpSettings);
    helper.Message($"Credential checkup Status: {samedisAuth.StatusCode} {samedisAuth.Status} User: {samedisAuth.User}", 1);
    var bearerToken = samedisAuth.BearerToken;

    //define resource
    var samedisClient = new RequestData(config.Samedis.Uri, bearerToken, httpSettings);
    var staffResource = $"/api/{config.Samedis.ApiVersion}/tenants/{config.Samedis.TenantId}/staffs";

    //check permissions
    helper.CanDo(samedisClient, staffResource);

    DataSet result = new();

    switch (config.ImportMode)
    {
      case "excel":
        // import file exists?
        var importFile = config.ImportFile;
        if (!File.Exists(importFile))
        {
          helper.Message($"The file {importFile} does not exists. Stopping Import.", 1, "ERROR");
          return;
        }

        // read excel file
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        using (var stream = File.Open(importFile, FileMode.Open, FileAccess.Read, FileShare.Read))
        {

          // Auto-detect format, supports:
          //  - Binary Excel files (2.0-2003 format; *.xls)
          //  - OpenXml Excel files (2007 format; *.xlsx, *.xlsb)
          using (var reader = ExcelReaderFactory.CreateReader(stream))
          {
            result = reader.AsDataSet(new ExcelDataSetConfiguration()
            {
              UseColumnDataType = true,
              FilterSheet = (tableReader, sheetIndex) => true,
              ConfigureDataTable = (tableReader) => new ExcelDataTableConfiguration()
              {
                EmptyColumnNamePrefix = "Column",
                UseHeaderRow = true,
              }
            });
          }
        }
        break;
      case "sql":
        string connectionString = DbHelper.GetConnectionString(config.ImportSql);
        string sqlQuery = File.ReadAllText(config.ImportSql.StaffQuery);
        result = DbHelper.ExecuteQuery(config.ImportSql.DatabaseType, connectionString, sqlQuery);
        break;
      case "ldap":
        result = LdapHelper.FillDirectory(config.ImportLdap.Server, config.ImportLdap.Ssl, config.ImportLdap.Path, config.ImportLdap.Username, config.ImportLdap.Password, config.ImportLdap.Mapping, config.ImportLdap.Filter, helper);
        break;
    }

    // column definition and check
    string[] importColumns = { "Nachname", "Vorname", "Personalnummer", "Eintrittsdatum", "Austrittsdatum", "E-Mail", "Titel", "Bemerkungen", "Handynummer" }; //,"Id"};
    if (!Helper.CheckColumnsExist(result.Tables[0], importColumns))
      helper.MessageAndExit("Invalid Column mapping, stopping import.");

    var filter = "?gridfilter={\"employee_no\": {\"filterType\": \"text\", \"type\": \"equals\", \"filter\": \"_EMPLOYEENO_\"}}";
    var idfilter = "?gridfilter={\"id\": {\"filterType\": \"text\", \"type\": \"equals\", \"filter\": \"_ID_\"}}";

    foreach (DataTable table in result.Tables)
    {

      helper.Message($"Number of records: {table.Rows.Count}.", 1);

      foreach (DataRow row in table.Rows)
      {
        //validate employee no
        var tmpEmpl = row["Personalnummer"].ToString();
        if (string.IsNullOrEmpty(tmpEmpl))
        {
          helper.Message($"SKIP: Missing EmployeeNo for \"{row["Nachname"]}\".");
          continue;
        }

        //validate date fields
        var tmpJoin = row["Eintrittsdatum"].ToString();
        if (helper.TryParseDate(tmpJoin, out DateTime parsedJoin))
          tmpJoin = parsedJoin.ToString("dd.MM.yyyy");
        else
        {
          helper.Message($"SKIP: No or invalid join date for \"{row["Nachname"]}\": {row["Eintrittsdatum"]}");
          continue;
        }
        var tmpLeft = row["Austrittsdatum"].ToString();
        if (tmpLeft?.ToString().Length > 0)
        {
          if (helper.TryParseDate(tmpLeft, out DateTime parsedLeft))
            tmpLeft = parsedLeft.ToString("dd.MM.yyyy");
          else
          {
            helper.Message($"SKIP: Invalid left date for \"{row["Nachname"]}\": {row["Austrittsdatum"]}");
            continue;
          }
          if (Convert.ToDateTime(tmpLeft) < Convert.ToDateTime(tmpJoin))
          {
            helper.Message($"SKIP: Left date {row["Austrittsdatum"]} is before join date {row["Eintrittsdatum"]} for \"{row["Nachname"]}\".");
            continue;
          }
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
        if (Helper.CheckColumnsExist(result.Tables[0], new string[] { "Id" }))
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
        var requestResource = staffResource;
        requestResource += attributes.Id != null
                        ? idfilter.Replace("_ID_", attributes.Id, StringComparison.OrdinalIgnoreCase)
                        : filter.Replace("_EMPLOYEENO_", row["Personalnummer"].ToString(), StringComparison.OrdinalIgnoreCase);

        var client = samedisClient.Get(requestResource);
        var record = JsonConvert.DeserializeObject<Staffs.Root>(client);
        var totalRecords = record != null ? record.Meta.Total : 0;

        // put or post
        if (totalRecords > 0)
        {
          var recordId = record.Data[0].Attributes.Id;
          helper.Message($"Staff EmployeeNo {row["Personalnummer"]} exists with record {recordId}", 2);
          client = samedisClient.Put(staffResource, recordId, staffBody);
          helper.Message($"Status Code: {samedisClient.StatusCode} {samedisClient.Status}", 2);
        }
        else
        {
          helper.Message($"Staff EmployeeNo {row["Personalnummer"]} does not exists", 2);
          client = samedisClient.Post(staffResource, staffBody);
          helper.Message($"Status Code: {samedisClient.StatusCode} {samedisClient.Status}", 2);
        }

      }
    }

    helper.Message("Sync finised.", 1);
  }
}