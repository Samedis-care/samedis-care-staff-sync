using System.Data;

namespace SamedisStaffSync
{
  public class SapCsvRecord
  {
    // Mitarbeiterstammdaten (expected CSV headers)
    public string Personalnummer { get; set; } = string.Empty;   // unique employee id
    public string Vorname { get; set; } = string.Empty;
    public string Nachname { get; set; } = string.Empty;
    public string Titel { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;             // optional
    public string Handynummer { get; set; } = string.Empty;       // optional
    public string Bemerkungen { get; set; } = string.Empty;       // optional

    // Beschäftigung
    public string Eintritt { get; set; } = string.Empty;          // dd.MM.yyyy or ISO
    public string Austritt { get; set; } = string.Empty;          // dd.MM.yyyy or ISO or empty

    // Organisation
    public string Abteilung { get; set; } = string.Empty;         // department code
    public string Abteilungstext { get; set; } = string.Empty;    // department name
    public string Kostenstelle { get; set; } = string.Empty;      // cost center

    // Stellen / Position und Dienst
    public string Position { get; set; } = string.Empty;          // position text or code
    public string Dienstart { get; set; } = string.Empty;         // e.g., Tagdienst, Nachtdienst, Rufbereitschaft
    public string DienstartText { get; set; } = string.Empty;     // descriptive text
  }

  public class SapDepartmentInfo
  {
    public string Abteilung { get; set; } = string.Empty;
    public string Abteilungstext { get; set; } = string.Empty;
    public string Kostenstelle { get; set; } = string.Empty;
  }

  public class SapImportResult
  {
    public DataSet PersonnelDataSet { get; set; } = new DataSet();
    public List<string> UniquePositions { get; set; } = new List<string>();
    public Dictionary<string, SapDepartmentInfo> UniqueDepartments { get; set; } = new Dictionary<string, SapDepartmentInfo>(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> UniqueDienstarten { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
  }

  public static class SapImporter
  {
    /// <summary>
    /// Imports a SAP-style CSV and builds (a) unique Lists/Dicts and (b) a consolidated personnel DataSet.
    /// Expected columns (case sensitive as headers):
    /// Personalnummer, Vorname, Nachname, Titel, Email, Handynummer, Bemerkungen, Status, Eintritt, Austritt,
    /// Abteilung, Abteilungstext, Kostenstelle, Position, Dienstart, DienstartText
    /// </summary>
    public static SapImportResult Import(string csvPath, Helper helper)
    {
      if (!File.Exists(csvPath))
      {
        throw new FileNotFoundException($"CSV not found: {csvPath}");
      }

      // Read the CSV as DataTable using existing helper (keeps project consistency)
      DataTable raw = Helper.ReadCsvWithCsvHelper(csvPath, hasHeader: true, delimeter: ",");
      var columnNames = raw.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToList();

      static string GetCell(DataRow row, params string[] names)
      {
        foreach (var n in names)
        {
          if (!row.Table.Columns.Contains(n)) continue;
          var v = row[n]?.ToString()?.Trim();
          if (!string.IsNullOrWhiteSpace(v)) return v!;
        }
        return string.Empty;
      }

      // Map raw rows to typed records (tolerant to missing columns)
      var records = new List<SapCsvRecord>(raw.Rows.Count);
      foreach (DataRow r in raw.Rows)
      {
        var rec = new SapCsvRecord
        {
          Personalnummer = GetCell(r, "Personalnummer", "Mitarbeiternr.", "EmployeeID"),
          Vorname = GetCell(r, "Vorname", "FirstName"),
          Nachname = GetCell(r, "Nachname", "LastName"),
          Titel = GetCell(r, "Titel", "Anrede", "Title"),
          Email = GetCell(r, "E-Mail", "Email", "Mail", "E Mail"),
          Handynummer = GetCell(r, "Handynummer", "Mobil", "Mobile", "Telefon mobil", "Handy"),
          Bemerkungen = GetCell(r, "Bemerkungen", "Kommentar", "Notiz", "Notes"),
          Eintritt = GetCell(r, "Eintritt", "Beitritt am", "Eintrittsdatum", "Eintritt am"),
          Austritt = GetCell(r, "Austritt", "Austritt am", "Austrittsdatum"),
          Abteilung = GetCell(r, "Abteilung", "Abt.", "Department", "OrgUnit"),
          Abteilungstext = GetCell(r, "Abteilungstext", "Abteilung (Text)", "Department Name", "OrgUnit Name"),
          Kostenstelle = GetCell(r, "Kostenstelle", "KST", "CostCenter"),
          Position = GetCell(r, "Position", "Stelle", "Funktion", "Job Title"),
          Dienstart = GetCell(r, "Dienstart", "Dienst-Art", "Dienstart"),
          DienstartText = GetCell(r, "Dienstart Text", "DienstartText", "Dienst-Art Text", "Dienstart Text")
        };
        if (!string.IsNullOrWhiteSpace(rec.Personalnummer))
          records.Add(rec);
      }

      // Build unique collections
      var uniquePositions = records
        .Select(r => r.Position)
        .Where(s => !string.IsNullOrWhiteSpace(s))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
        .ToList();

      var uniqueDepartments = new Dictionary<string, SapDepartmentInfo>(StringComparer.OrdinalIgnoreCase);
      foreach (var r in records)
      {
        if (string.IsNullOrWhiteSpace(r.Abteilung)) continue;
        if (!uniqueDepartments.ContainsKey(r.Abteilung))
        {
          uniqueDepartments[r.Abteilung] = new SapDepartmentInfo
          {
            Abteilung = r.Abteilung,
            Abteilungstext = r.Abteilungstext,
            Kostenstelle = r.Kostenstelle
          };
        }
        else
        {
          if ( !string.IsNullOrEmpty(r.Kostenstelle) && uniqueDepartments[r.Abteilung].Kostenstelle != r.Kostenstelle )
            uniqueDepartments[r.Abteilung].Kostenstelle = r.Kostenstelle; // last one wins
        }
      }

      var uniqueDienstarten = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
      foreach (var r in records)
      {
        if (string.IsNullOrWhiteSpace(r.Dienstart)) continue;
        if (!uniqueDienstarten.ContainsKey(r.Dienstart))
        {
          uniqueDienstarten[r.Dienstart] = r.DienstartText;
        }
      }

      // Consolidate personnel by dates only (no status):
      // - Group by Personalnummer
      // - lastJoin := MAX(Eintritt)
      // - lastLeft := MAX(Austritt)
      // - Use left date only if lastLeft > lastJoin
      // - Representative record = newest by Eintritt; if ties/empty, then by Nachname/Vorname
      // - If representative.Abteilung empty, fallback to first non-empty Abteilung in group
      var consolidated = records
        .GroupBy(r => r.Personalnummer)
        .OrderBy(g => g.Key)
        .Select(g =>
        {
          var joins = g
            .Select(r => TryParseDate(r.Eintritt, out var d) ? d : (DateTime?)null)
            .Where(d => d.HasValue)
            .ToList();
          DateTime? lastJoin = joins.Count > 0 ? joins.Max() : (DateTime?)null;

          var lefts = g
            .Select(r => TryParseDate(r.Austritt, out var d) ? d : (DateTime?)null)
            .Where(d => d.HasValue)
            .ToList();
          DateTime? lastLeft = lefts.Count > 0 ? lefts.Max() : (DateTime?)null;

          // pick the most recent by Eintritt, then by name
          var representative = g
            .OrderByDescending(r => TryParseDate(r.Eintritt, out var d) ? d : DateTime.MinValue)
            .ThenBy(r => r.Nachname)
            .ThenBy(r => r.Vorname)
            .First();

          // department fallback
          if (string.IsNullOrWhiteSpace(representative.Abteilung))
          {
            var withDept = g.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.Abteilung));
            if (withDept != null)
            {
              representative.Abteilung = withDept.Abteilung;
              representative.Abteilungstext = withDept.Abteilungstext;
            }
          }

          // decide if "Left" applies
          bool useLeft = false;
          if (lastLeft.HasValue)
          {
            if (!lastJoin.HasValue) useLeft = true; // no join known but left exists
            else useLeft = lastLeft.Value > lastJoin.Value; // only count if after last join
          }

          return new
          {
            Key = g.Key,
            LatestJoin = lastJoin,
            LatestLeft = useLeft ? lastLeft : null,
            First = representative
          };
        })
        .ToList();

      // Build DataTable aligned with existing API mapping in Program.cs (Staffs.Attributes)
      var table = new DataTable("Personnel");
      table.Columns.Add("Mitarbeiternr.", typeof(string));
      table.Columns.Add("Vorname", typeof(string));
      table.Columns.Add("Nachname", typeof(string));
      table.Columns.Add("Titel", typeof(string));
      table.Columns.Add("E-Mail", typeof(string));
      table.Columns.Add("Handynummer", typeof(string));
      table.Columns.Add("Bemerkungen", typeof(string));
      table.Columns.Add("Beitritt am", typeof(string)); // dd.MM.yyyy
      table.Columns.Add("Austritt am", typeof(string)); // dd.MM.yyyy or empty
      table.Columns.Add("Positionen", typeof(string));
      table.Columns.Add("Abteilungen", typeof(string));

      foreach (var x in consolidated)
      {
        var f = x.First;
        string joinStr = x.LatestJoin.HasValue ? x.LatestJoin.Value.ToString("dd.MM.yyyy") : string.Empty;
        string leftStr = x.LatestLeft.HasValue ? x.LatestLeft.Value.ToString("dd.MM.yyyy") : string.Empty;

        table.Rows.Add(
          x.Key,
          f.Vorname,
          f.Nachname,
          f.Titel,
          Prefer(f.Email),
          f.Handynummer,
          f.Bemerkungen,
          joinStr,
          leftStr,
          Prefer(f.Position),
          Prefer(f.Abteilung)
        );
      }

      var ds = new DataSet();
      ds.Tables.Add(table);

      helper?.Message($"SAP Import: raw rows: {records.Count}, unique employees: {consolidated.Count}, unique positions: {uniquePositions.Count}, departments: {uniqueDepartments.Count}, dienst-arten: {uniqueDienstarten.Count}", 1);

      return new SapImportResult
      {
        PersonnelDataSet = ds,
        UniquePositions = uniquePositions,
        UniqueDepartments = uniqueDepartments,
        UniqueDienstarten = uniqueDienstarten
      };
    }

    private static string Prefer(string s)
      => string.IsNullOrWhiteSpace(s) ? string.Empty : s.Trim();

    private static bool TryParseDate(string input, out DateTime date)
    {
      date = default;
      if (string.IsNullOrWhiteSpace(input)) return false;

      var styles = System.Globalization.DateTimeStyles.AssumeLocal;
      // Try common formats: dd.MM.yyyy, yyyy-MM-dd, dd.MM.yy
      string[] fmts = ["dd.MM.yyyy", "yyyy-MM-dd", "dd.MM.yy", "d.M.yyyy", "d.M.yy"];
      if (DateTime.TryParseExact(input.Trim(), fmts, System.Globalization.CultureInfo.GetCultureInfo("de-DE"), styles, out var exact))
      {
        date = exact;
        return true;
      }
      // Fallback to general parse
      if (DateTime.TryParse(input, System.Globalization.CultureInfo.GetCultureInfo("de-DE"), styles, out var parsed))
      {
        date = parsed;
        return true;
      }
      return false;
    }
  }
}