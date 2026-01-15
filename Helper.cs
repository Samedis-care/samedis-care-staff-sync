using System.Data;
using System.Globalization;
using Newtonsoft.Json;
using CsvHelper;
using CsvHelper.Configuration;
using Newtonsoft.Json.Linq;
using System.Reflection;

namespace SamedisStaffSync
{
  public class Helper
  {
    /// <summary>
    /// LogLevel 0: turned off
    /// LogLevel 1: normal output
    /// LogLevel 2: debug output
    /// </summary>
    public int LogLevel = 1;
    /// <summary>
    /// LogMode 0: no output
    /// LogMode 1: Console Output
    /// LogMode 2: LogFile
    /// LofMode 3: Console and Logfile
    /// </summary>
    public int LogMode = 3;
    public string LogFile = "debug.log";
    private static readonly string CsvDelimiter = ";";

    public void Message(string message, int logLevel = 1, string logType = "INFO")
    {
      if (logLevel > LogLevel) return;
      const string format = "yyyy-MM-dd HH:mm:ss";

      if (LogMode == 1 || LogMode == 3)
      {
        Console.WriteLine(new string('*', 80));
        Console.WriteLine(DateTime.Now.ToString(format) + " " + message);
      }

      if (LogMode < 2) return;
      Directory.CreateDirectory("log");
      var logContent = string.Empty;
      logContent += DateTime.Now.ToString(format) + " ";
      logContent += logType + " ";
      if (!string.IsNullOrEmpty(message))
        logContent += message;
      File.AppendAllText(Path.Combine("log", LogFile), logContent + "\n");
    }

    public static DataTable ReadCsvWithCsvHelper(string filePath, bool hasHeader = true, string delimeter = ";")
    {
      var dt = new DataTable();

      var config = new CsvConfiguration(CultureInfo.InvariantCulture)
      {
        HasHeaderRecord = hasHeader,
        Delimiter = delimeter,
        Encoding = System.Text.Encoding.UTF8,
        DetectColumnCountChanges = true,
        BadDataFound = null // ignore bad data gracefully
      };

      using (var reader = new StreamReader(filePath, System.Text.Encoding.UTF8))
      using (var csv = new CsvReader(reader, config))
      {
        using var dr = new CsvDataReader(csv);
        dt.Load(dr);
      }

      return dt;
    }

    public static bool CheckColumnsExist(DataTable dataTable, string[] requiredColumns)
    {
      foreach (var columnName in requiredColumns)
      {
        if (!dataTable.Columns.Contains(columnName))
          return false;
      }
      return true;
    }

    public static string[] GetAvailableColumns(DataTable dataTable, string[] importColumns)
    {
      return importColumns
          .Where(columnName => dataTable.Columns.Contains(columnName))
          .ToArray();
    }

    public static bool TryParseDate(string stringDate, out DateTime result)
    {
      // Try to parse AD GeneralizedTime format (e.g., 20240531193352.0Z)
      if (DateTime.TryParseExact(stringDate, "yyyyMMddHHmmss.0Z", null, System.Globalization.DateTimeStyles.AssumeUniversal, out result))
        return true;

      // Try general DateTime parsing for other formats
      return DateTime.TryParse(stringDate, out result);
    }

    public void CanDo(RequestData client, string resource)
    {
      var requestResource = resource + "?limit=0";
      var check = client.Get(requestResource);
      if (client.StatusCode >= 400)
      {
        Staffs.Root? record = null;
        if (!string.IsNullOrEmpty(check))
          record = JsonConvert.DeserializeObject<Staffs.Root>(check);
        var errorMsg = record?.Meta?.Msg?.Message ?? "Unknown error";
        MessageAndExit($"Sync stopped. {client.StatusCode} {errorMsg}");
      }
    }

    internal string MessageAndExit(string errorMessage)
    {
      Message(errorMessage, 1);
      Environment.Exit(1);
      return null; // Unreachable Code, just for compiler
    }

    private static readonly string[] StaffCsvColumns = typeof(Staffs.Attributes)
      .GetProperties(BindingFlags.Public | BindingFlags.Instance)
      .OrderBy(property => property.MetadataToken)
      .Select(property =>
      {
        var jsonProperty = property.GetCustomAttribute<JsonPropertyAttribute>();
        return jsonProperty?.PropertyName ?? property.Name;
      })
      .Where(name => !string.Equals(name, "id", StringComparison.OrdinalIgnoreCase))
      .ToArray();

    public void AppendJsonAsCsv(string filePath, string json)
    {
      if (string.IsNullOrWhiteSpace(json))
        return;

      try
      {
        var root = JObject.Parse(json);
        if (root["data"] is not JObject dataObject)
        {
          return;
        }

        if (StaffCsvColumns.Length == 0)
        {
          return;
        }

        var needsHeader = !File.Exists(filePath) || new FileInfo(filePath).Length == 0;
        var delimiter = Helper.CsvDelimiter;

        if (needsHeader)
        {
          var headerLine = string.Join(delimiter, StaffCsvColumns.Select(EscapeCsvValue));
          File.AppendAllText(filePath, headerLine + Environment.NewLine);
        }

        var values = StaffCsvColumns
          .Select(column => dataObject.TryGetValue(column, out var token) ? TokenToString(token) : string.Empty)
          .Select(EscapeCsvValue);

        var dataLine = string.Join(delimiter, values);
        File.AppendAllText(filePath, dataLine + Environment.NewLine);
      }
      catch (Exception)
      {
        File.AppendAllText(filePath, json + Environment.NewLine);
      }
    }

    public static void WriteCsv(string filePath, string[] headers, IEnumerable<string[]> rows)
    {
      var delimiter = Helper.CsvDelimiter;
      var lines = new List<string>
      {
        string.Join(delimiter, headers.Select(EscapeCsvValue))
      };

      foreach (var row in rows)
      {
        lines.Add(string.Join(delimiter, row.Select(EscapeCsvValue)));
      }

      File.WriteAllText(filePath, string.Join(Environment.NewLine, lines));
    }

    private static string TokenToString(JToken token)
    {
      return token.Type switch
      {
        JTokenType.Array => string.Join("|", token.Select(TokenToString)),
        JTokenType.Object => token.ToString(Formatting.None),
        JTokenType.Null or JTokenType.Undefined => string.Empty,
        JTokenType.Date => token.Value<DateTime>().ToString("o", CultureInfo.InvariantCulture),
        _ => token.ToString()
      };
    }

    private static string EscapeCsvValue(string value)
    {
      if (string.IsNullOrEmpty(value))
        return string.Empty;

      var needsQuotes = value.Contains('"') || value.Contains(CsvDelimiter) || value.Contains('\r') || value.Contains('\n');
      var sanitized = value.Replace("\"", "\"\"");
      return needsQuotes ? $"\"{sanitized}\"" : sanitized;
    }

    /// <summary>
    /// Ensures JSON can be parsed whether "data" is a single object or array.
    /// </summary>
    public class SingleOrArrayConverter<T> : JsonConverter
    {
      public override bool CanConvert(Type objectType) => objectType == typeof(List<T>);

      public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
      {
        var token = JToken.Load(reader);
        if (token.Type == JTokenType.Array)
          return token.ToObject<List<T>>(serializer) ?? [];

        var obj = token.ToObject<T>(serializer);
        return obj != null ? new List<T> { obj } : [];
      }

      public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
      {
        serializer.Serialize(writer, value);
      }
    }
  }

  public class DepartmentInfo
  {
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string? CostCenter { get; set; }
  }

  public class UniqueOrgData
  {
    public List<string> Positions { get; set; } = new List<string>();
    public Dictionary<string, DepartmentInfo> Departments { get; set; } = new Dictionary<string, DepartmentInfo>(StringComparer.OrdinalIgnoreCase);
  }

  public static class OrgDataHelper
  {
    public static UniqueOrgData CollectUniqueOrgData(DataSet dataSet)
    {
      var positions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      var departments = new Dictionary<string, DepartmentInfo>(StringComparer.OrdinalIgnoreCase);

      foreach (DataTable table in dataSet.Tables)
      {
        var hasPositions = table.Columns.Contains("Positionen");
        var hasDepartments = table.Columns.Contains("Abteilungen");
        var hasDeptText = table.Columns.Contains("Abteilungstext");
        var hasCostCenter = table.Columns.Contains("Kostenstelle");

        if (!hasPositions && !hasDepartments) continue;

        foreach (DataRow row in table.Rows)
        {
          if (hasPositions)
          {
            var posTitle = row["Positionen"]?.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(posTitle))
              positions.Add(posTitle);
          }

          if (hasDepartments)
          {
            var deptKey = row["Abteilungen"]?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(deptKey)) continue;

            var deptTitle = hasDeptText ? row["Abteilungstext"]?.ToString()?.Trim() : null;
            var costCenter = hasCostCenter ? row["Kostenstelle"]?.ToString()?.Trim() : null;

            if (!departments.TryGetValue(deptKey, out var existing))
            {
              departments[deptKey] = new DepartmentInfo
              {
                Key = deptKey,
                Title = !string.IsNullOrWhiteSpace(deptTitle) ? deptTitle! : deptKey,
                Code = hasDeptText ? deptKey : null,
                CostCenter = !string.IsNullOrWhiteSpace(costCenter) ? costCenter : null
              };
            }
            else
            {
              if (!string.IsNullOrWhiteSpace(deptTitle))
                existing.Title = deptTitle!;
              if (hasDeptText && string.IsNullOrWhiteSpace(existing.Code))
                existing.Code = deptKey;
              if (!string.IsNullOrWhiteSpace(costCenter))
                existing.CostCenter = costCenter;
            }
          }
        }
      }

      return new UniqueOrgData
      {
        Positions = positions.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList(),
        Departments = departments
      };
    }
  }

  public enum DatabaseType
  {
    SqlServer,
    MySql,
    SQLite,
    Oracle
  }

}
