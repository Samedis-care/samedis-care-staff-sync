using System.Data;
using System.Globalization;
using Newtonsoft.Json;
using CsvHelper;
using CsvHelper.Configuration;
using Newtonsoft.Json.Linq;
using System.Reflection;
using SamedisCare.Api.Http;
using SamedisCare.Api.Logging;
using SamedisCare.Api.V4.Public;
using SamedisCare.Api.Common;

namespace SamedisStaffSync
{
  public static class Helper
  {
    public static string CsvDelimiter = ";";

    // Logging lives in SamedisCare.Api.Logging.FileSyncLog. This class no longer
    // carries a copy of it, and no longer needs to be instantiated at all.

    public static DataTable ReadCsvWithCsvHelper(string filePath, bool hasHeader = true, string? delimeter = null)
    {
      var dt = new DataTable();

      var config = new CsvConfiguration(CultureInfo.InvariantCulture)
      {
        HasHeaderRecord = hasHeader,
        Delimiter = delimeter ?? CsvDelimiter,
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

    /// <summary>
    /// Parses a date coming from Active Directory. Renamed from TryParseDate because the
    /// GeneralizedTime form (e.g. 20240531193352.0Z) is an LDAP special case and does not
    /// belong in the shared library - only the generic fallback does.
    /// </summary>
    public static bool TryParseLdapDate(string stringDate, out DateTime result)
      => Dates.TryParseGeneralizedTime(stringDate, out result);

    /// <summary>
    /// Returns true when every field in the outgoing payload already matches the remote attributes.
    /// Lists are compared as unordered sets (e.g. department_ids, position_ids). Date fields are
    /// compared as parsed DateTime values so dd.MM.yyyy vs ISO formats round-trip cleanly.
    /// Only fields present in the outgoing payload are checked — fields we do not set are ignored.
    /// <paramref name="mismatchReason"/> is populated with the first differing field for logging.
    /// </summary>
    public static bool StaffPayloadMatchesRemote(JObject outgoing, Staffs.Attributes? remote, out string? mismatchReason)
    {
      mismatchReason = null;
      if (remote == null) { mismatchReason = "remote record missing"; return false; }
      var remoteJson = JObject.FromObject(remote);
      var dateFields = new HashSet<string>(StringComparer.Ordinal) { "joined", "left" };

      foreach (var prop in outgoing.Properties())
      {
        var remoteToken = remoteJson[prop.Name];
        var outgoingToken = prop.Value;

        if (dateFields.Contains(prop.Name))
        {
          var outStr = outgoingToken.Type == JTokenType.Null ? string.Empty : outgoingToken.ToString();
          var remStr = remoteToken == null || remoteToken.Type == JTokenType.Null ? string.Empty : remoteToken.ToString();
          if (string.IsNullOrEmpty(outStr) && string.IsNullOrEmpty(remStr)) continue;
          if (TryParseStaffDate(outStr, out var outDate) && TryParseStaffDate(remStr, out var remDate))
          {
            if (outDate.Date != remDate.Date)
            {
              mismatchReason = $"{prop.Name}: '{outStr}' vs '{remStr}'";
              return false;
            }
            continue;
          }
          if (!string.Equals(outStr, remStr, StringComparison.Ordinal))
          {
            mismatchReason = $"{prop.Name}: '{outStr}' vs '{remStr}'";
            return false;
          }
          continue;
        }

        if (outgoingToken is JArray outArr)
        {
          var outList = outArr.Select(t => t.ToString()).OrderBy(s => s, StringComparer.Ordinal).ToList();
          var remList = (remoteToken as JArray)?.Select(t => t.ToString()).OrderBy(s => s, StringComparer.Ordinal).ToList() ?? new List<string>();
          if (!outList.SequenceEqual(remList))
          {
            mismatchReason = $"{prop.Name}: [{string.Join(",", outList)}] vs [{string.Join(",", remList)}]";
            return false;
          }
        }
        else if (!JToken.DeepEquals(outgoingToken, remoteToken ?? JValue.CreateNull()))
        {
          mismatchReason = $"{prop.Name}: '{outgoingToken}' vs '{remoteToken?.ToString() ?? "<missing>"}'";
          return false;
        }
      }
      return true;
    }

    // Styles are passed explicitly to keep the previous behaviour exactly: the known
    // dd.MM.yyyy form must NOT be normalized to UTC (that would move midnight to the
    // previous day at a positive offset), while the fallback still normalizes.
    private static bool TryParseStaffDate(string s, out DateTime date)
      => Dates.TryParse(s, out date,
                        formats: new[] { "dd.MM.yyyy" },
                        culture: CultureInfo.InvariantCulture,
                        styles: DateTimeStyles.None,
                        fallbackStyles: DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    // CanDo and MessageAndExit moved to Program: the probe itself is Capability.Probe
    // in the library, and terminating the process is the host's decision, not a
    // helper's - see RequireAccess and Abort there.

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

    public static void AppendJsonAsCsv(string filePath, string json)
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
    // SingleOrArrayConverter was dead here once the resource models moved to the
    // library - the models use SamedisCare.Api.Common.Helper's converter.
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
