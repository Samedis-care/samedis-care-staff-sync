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
    if (config == null) Environment.Exit(1);

    helper.LogLevel = config.Logging.Level;
    helper.LogMode = config.Logging.Mode;

    helper.Message("Sync started.", 1);

    //test mode
    var testMode = false;
    var testFile = "test_output.csv";
    if (config.Testing.Active)
    {
      helper.Message("Testing mode active, no changes will be applied.", 1, "INFO");
      testMode = true;
      if (File.Exists(testFile)) File.Delete(testFile);
    }

    // last run
    string filePath = "lastrun.txt";
    DateTime currentTimestamp = DateTime.Now;
    DateTime lastRun = File.Exists(filePath) ? Convert.ToDateTime(File.ReadAllText(filePath)) : currentTimestamp;
    File.WriteAllText(filePath, currentTimestamp.ToString());

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

    if (string.IsNullOrWhiteSpace(bearerToken))
      helper.MessageAndExit("Authentication failed: Bearer token is null or empty. Stopping Import.");
    if (string.IsNullOrWhiteSpace(config.Samedis.Uri))
      helper.MessageAndExit("Samedis.Uri is not configured. Stopping Import.");

    //define resource
    var samedisClient = new RequestData(config.Samedis.Uri, bearerToken, httpSettings);
    var staffResource = $"/api/{config.Samedis.ApiVersion}/tenants/{config.Samedis.TenantId}/staffs";

    //check permissions
    helper.CanDo(samedisClient, staffResource);

    DataSet result = new();
    var importFile = config.ImportFile;

    // build lookups for later staff updates (make available in all modes)
    var positionIdsByTitle = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var departmentIdsByTitle = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    switch (config.ImportMode)
    {
      case "csv":
        if (!File.Exists(importFile))
        {
          helper.Message($"The file {importFile} does not exist. Stopping Import.", 1, "ERROR");
          return;
        }

        result = new DataSet();
        var table = Helper.ReadCsvWithCsvHelper(importFile, hasHeader: true);
        result.Tables.Add(table);
        break;

      case "excel":
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
        result = LdapHelper.FillDirectory(config.ImportLdap.Server, config.ImportLdap.Ssl, config.ImportLdap.Path, config.ImportLdap.Username, config.ImportLdap.Password, config.ImportLdap.Mapping, config.ImportLdap.Filter, helper, lastRun);
        break;
      case "sap":
        if (!File.Exists(importFile))
        {
          helper.Message($"The file {importFile} does not exists. Stopping Import.", 1, "ERROR");
          return;
        }
        var sap = SapImporter.Import(importFile, helper);
        result = sap.PersonnelDataSet;

        helper.Message($"Unique Positions: {sap.UniquePositions.Count}", 2);
        helper.Message($"Unique Departments: {sap.UniqueDepartments.Count}", 2);
        helper.Message($"Unique Dienstarten: {sap.UniqueDienstarten.Count}", 2);

        var positionsResource = $"/api/{config.Samedis.ApiVersion}/tenants/{config.Samedis.TenantId}/positions";
        var departmentsResource = $"/api/{config.Samedis.ApiVersion}/tenants/{config.Samedis.TenantId}/departments";

        foreach (var posTitle in sap.UniquePositions)
        {
          if (string.IsNullOrWhiteSpace(posTitle)) continue;
          var pid = Positions.FindOrCreatePosition(samedisClient, positionsResource, posTitle);
          if (!string.IsNullOrEmpty(pid)) positionIdsByTitle[posTitle] = pid!;
        }

        foreach (var dep in sap.UniqueDepartments.Values)
        {
          if (string.IsNullOrWhiteSpace(dep.Abteilungstext)) continue;
          var did = Departments.FindOrCreateDepartment(samedisClient, departmentsResource, dep.Abteilungstext, dep.Abteilung, dep.Kostenstelle);
          if (!string.IsNullOrEmpty(did)) departmentIdsByTitle[dep.Abteilung] = did!;
        }
        break;
    }

    // column definition and check
    string[] importColumns = ["Vorname", "Nachname", "Mitarbeiternr.", "Beitritt am", "Austritt am", "E-Mail", "Titel", "Bemerkungen", "Handynummer", "Positionen", "Abteilungen", "Id"];
    string[] importPresentColumns = Helper.GetAvailableColumns(result.Tables[0], importColumns);
    string[] importMandatoryColumns = ["Vorname", "Nachname", "Mitarbeiternr.", "Beitritt am", "Austritt am"];
    if (!Helper.CheckColumnsExist(result.Tables[0], importMandatoryColumns))
      helper.MessageAndExit("Invalid Column mapping, stopping import.");

    var filter = "?gridfilter={\"employee_no\": {\"filterType\": \"text\", \"type\": \"equals\", \"filter\": \"_EMPLOYEENO_\"}}";
    var idfilter = "?gridfilter={\"id\": {\"filterType\": \"text\", \"type\": \"equals\", \"filter\": \"_ID_\"}}";

    foreach (DataTable table in result.Tables)
    {

      helper.Message($"Number of records: {table.Rows.Count}.", 1);

      foreach (DataRow row in table.Rows)
      {
        //validate employee no
        var tmpEmpl = row["Mitarbeiternr."].ToString();
        if (string.IsNullOrEmpty(tmpEmpl))
        {
          helper.Message($"SKIP: Missing EmployeeNo for \"{row["Nachname"]}\".");
          continue;
        }

        //validate date fields
        var tmpJoin = row["Beitritt am"]?.ToString();
        if (!string.IsNullOrEmpty(tmpJoin) && Helper.TryParseDate(tmpJoin, out DateTime parsedJoin))
          tmpJoin = parsedJoin.ToString("dd.MM.yyyy");
        else
        {
          helper.Message($"SKIP: No or invalid join date for \"{row["Nachname"]}\": {row["Beitritt am"]}");
          continue;
        }
        var tmpLeft = row["Austritt am"].ToString();
        if (tmpLeft?.ToString().Length > 0)
        {
          if (Helper.TryParseDate(tmpLeft, out DateTime parsedLeft))
            tmpLeft = parsedLeft.ToString("dd.MM.yyyy");
          else
          {
            helper.Message($"SKIP: Invalid left date for \"{row["Nachname"]}\": {row["Austritt am"]}");
            continue;
          }
          if (Convert.ToDateTime(tmpLeft) < Convert.ToDateTime(tmpJoin))
          {
            helper.Message($"SKIP: Left date {row["Austritt am"]} is before join date {row["Beitritt am"]} for \"{row["Nachname"]}\".");
            continue;
          }
          if (Convert.ToDateTime(tmpLeft) > DateTime.Now.AddYears(10))
          {
            helper.Message($"SKIP: Left date {row["Austritt am"]}, please leave this field blank instead of using dates far in the future for \"{row["Nachname"]}\".");
            continue;
          }
        }

        var attributes = new Staffs.Attributes
        {
          LastName = row["Nachname"]?.ToString()?.Trim(),
          FirstName = row["Vorname"]?.ToString()?.Trim(),
          EmployeeNo = tmpEmpl,
          Left = tmpLeft,
          Joined = tmpJoin
        };

        // only set property if column exists
        if (row.Table.Columns.Contains("Titel"))
          attributes.Title = row["Titel"]?.ToString()?.Trim();

        if (row.Table.Columns.Contains("E-Mail"))
          attributes.Email = row["E-Mail"]?.ToString()?.Trim();

        if (row.Table.Columns.Contains("Handynummer"))
          attributes.MobileNumber = row["Handynummer"]?.ToString()?.Trim();

        if (row.Table.Columns.Contains("Bemerkungen"))
          attributes.Notes = row["Bemerkungen"]?.ToString()?.Trim();

        // check for Id, if exists, include
        if (Helper.CheckColumnsExist(result.Tables[0], ["Id"]))
          attributes.Id = row["Id"].ToString();

        var deptTitle = row.Table.Columns.Contains("Abteilungen") ? row["Abteilungen"]?.ToString() : null;
        if (!string.IsNullOrWhiteSpace(deptTitle) && departmentIdsByTitle.TryGetValue(deptTitle, out var deptId))
        {
          attributes.DepartmentIds = [deptId];
        }

        var posTitle = row.Table.Columns.Contains("Positionen") ? row["Positionen"]?.ToString() : null;
        if (!string.IsNullOrWhiteSpace(posTitle) && positionIdsByTitle.TryGetValue(posTitle, out var posId))
          attributes.PositionIds = [posId];

        // Convert Staff object to JObject
        var dataObject = new JObject();
        // build object
        foreach (var property in typeof(Staffs.Attributes).GetProperties())
        {
          var attr = Attribute.GetCustomAttribute(property, typeof(JsonPropertyAttribute));
          var jsonPropertyAttribute = attr as JsonPropertyAttribute;
          var propertyName = jsonPropertyAttribute?.PropertyName ?? property.Name.ToLower();
          if (propertyName == "Id") continue;
          var value = property.GetValue(attributes);
          if (value != null)
          {
            if (value is string strValue && string.IsNullOrWhiteSpace(strValue))
              continue;
            dataObject[propertyName] = JToken.FromObject(value);
          }
        }
        var staffObject = new JObject { ["data"] = dataObject };

        var staffBody = JsonConvert.SerializeObject(staffObject, Formatting.Indented);
        helper.Message(staffBody, 2);

        if (!testMode)
        {
          // check if exists
          var requestResource = staffResource;
          requestResource += attributes.Id != null
                          ? idfilter.Replace("_ID_", attributes.Id, StringComparison.OrdinalIgnoreCase)
                          : filter.Replace("_EMPLOYEENO_", row["Mitarbeiternr."].ToString(), StringComparison.OrdinalIgnoreCase);

          var client = samedisClient.Get(requestResource);
          Staffs.Root? record = null;
          if (!string.IsNullOrEmpty(client))
            record = JsonConvert.DeserializeObject<Staffs.Root>(client);
          var totalRecords = record?.Meta?.Total ?? 0;

          // put or post
          if (totalRecords > 0)
          {
            var recordId = record?.Data?[0].Attributes?.Id ?? "";
            helper.Message($"Staff EmployeeNo {row["Mitarbeiternr."]} exists with record {recordId}", 2);
            if (!string.IsNullOrEmpty(recordId)) client = samedisClient.Put(staffResource, recordId, staffBody);
          }
          else
          {
            helper.Message($"Staff EmployeeNo {row["Mitarbeiternr."]} does not exists", 2);
            client = samedisClient.Post(staffResource, staffBody);
          }
          helper.Message($"Status Code: {samedisClient.StatusCode} {samedisClient.Status}", 2);
        }
        else
        {
          helper.AppendJsonAsCsv(testFile, staffBody);
        }
      }
    }
    helper.Message("Sync finised.", 1);
  }
}