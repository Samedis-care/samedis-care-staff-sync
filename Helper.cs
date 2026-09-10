using System.Data;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Reflection;
using SamedisCare.Api.V4.Public;
using SamedisCare.Helper;
using SamedisCare.Helper.Text;

namespace SamedisStaffSync
{
  public static class Helper
  {
    // CSV reading and writing, column checks, date parsing and logging all come from
    // SamedisCare.Helper and are called directly at the call sites. What is left here is
    // specific to this import.

    /// <summary>
    /// The date format the staff import passes between its own parts, and the one the API
    /// receives in <c>Staffs.Attributes.Joined</c> / <c>.Left</c>.
    /// </summary>
    /// <remarks>
    /// Named because it is a contract across four files: HelperSap writes it into the
    /// DataTable, LdapHelper writes a leaving date into the same column, Program reformats
    /// and sends it, and <c>TryParseStaffDate</c> below reads it back. Always format and parse
    /// it with <see cref="CultureInfo.InvariantCulture"/> -- the ambient culture put the
    /// host's calendar into the value (30.04.2569 on th-TH) and made the day-first spelling
    /// unreadable on en-US. See samedis-care-issues#2886.
    /// </remarks>
    public const string StaffDateFormat = "dd.MM.yyyy";

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

    /// <summary>Why a staff row's dates were rejected, or <see cref="StaffDateVerdict.Ok"/>.</summary>
    public enum StaffDateVerdict
    {
      Ok,
      JoinMissingOrUnreadable,
      LeftUnreadable,
      LeftBeforeJoin,
      LeftTooFarInTheFuture,
    }

    /// <summary>
    /// Validates a staff row's join and leaving date and renders both in
    /// <see cref="StaffDateFormat"/> for the API.
    /// </summary>
    /// <remarks>
    /// Extracted from the import loop so it can be tested: it used to format with the ambient
    /// culture and then re-parse its own output with <c>Convert.ToDateTime</c>, which reads a
    /// day-first date only where the culture is day-first. On en-US, or in a container with no
    /// locale, the first row carrying a leaving date threw an uncaught FormatException and
    /// aborted the import; on th-TH the year written to the API was Buddhist. Both are gone
    /// now: formatting is invariant and the comparisons use the parsed values, which were
    /// already in hand. See samedis-care-issues#2886.
    /// </remarks>
    /// <param name="rawJoin">The <c>Beitritt am</c> cell, in whatever the source wrote.</param>
    /// <param name="rawLeft">The <c>Austritt am</c> cell; empty means "still employed".</param>
    /// <param name="join">The join date for the API, or empty when the verdict is not Ok.</param>
    /// <param name="left">The leaving date for the API, empty when the row has none.</param>
    public static StaffDateVerdict PrepareStaffDates(string? rawJoin, string? rawLeft,
                                                     out string join, out string left)
    {
      join = string.Empty;
      left = string.Empty;

      if (string.IsNullOrEmpty(rawJoin) || !Dates.TryParseGeneralizedTime(rawJoin, out var parsedJoin))
        return StaffDateVerdict.JoinMissingOrUnreadable;

      join = parsedJoin.ToString(StaffDateFormat, CultureInfo.InvariantCulture);

      if (string.IsNullOrEmpty(rawLeft))
        return StaffDateVerdict.Ok;

      if (!Dates.TryParseGeneralizedTime(rawLeft, out var parsedLeft))
        return StaffDateVerdict.LeftUnreadable;

      if (parsedLeft < parsedJoin)
        return StaffDateVerdict.LeftBeforeJoin;

      if (parsedLeft > DateTime.Now.AddYears(10))
        return StaffDateVerdict.LeftTooFarInTheFuture;

      left = parsedLeft.ToString(StaffDateFormat, CultureInfo.InvariantCulture);
      return StaffDateVerdict.Ok;
    }

    // Styles are passed explicitly to keep the previous behaviour exactly: the known
    // dd.MM.yyyy form must NOT be normalized to UTC (that would move midnight to the
    // previous day at a positive offset), while the fallback still normalizes.
    private static bool TryParseStaffDate(string s, out DateTime date)
      => Dates.TryParse(s, out date,
                        formats: new[] { StaffDateFormat },
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

    public static void AppendJsonAsCsv(string filePath, string json, string delimiter)
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

        if (needsHeader)
        {
          var headerLine = string.Join(delimiter, StaffCsvColumns.Select(v => Csv.Escape(v)));
          File.AppendAllText(filePath, headerLine + Environment.NewLine);
        }

        var values = StaffCsvColumns
          .Select(column => dataObject.TryGetValue(column, out var token) ? TokenToString(token) : string.Empty)
          .Select(v => Csv.Escape(v));

        var dataLine = string.Join(delimiter, values);
        File.AppendAllText(filePath, dataLine + Environment.NewLine);
      }
      catch (Exception)
      {
        File.AppendAllText(filePath, json + Environment.NewLine);
      }
    }

    private static string TokenToString(JToken token)
      => token.Type is JTokenType.Null or JTokenType.Undefined
           ? string.Empty
           : token is JArray or JObject
             ? token.ToString(Formatting.None)
             : token.ToString();
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


}
