using System;
using System.Data;
using System.DirectoryServices.Protocols;
using System.Net;
using Newtonsoft.Json;
using System.Collections.Generic;

namespace SamedisStaffSync
{
  class LdapHelper
  {
    private static readonly string[] DefaultOutputColumns =
    [
      "Vorname",
      "Nachname",
      "Mitarbeiternr.",
      "Beitritt am",
      "Austritt am",
      "E-Mail",
      "Titel",
      "Bemerkungen",
      "Handynummer",
      "Positionen",
      "Abteilungen",
      "Abteilungstext",
      "Kostenstelle",
      "Id"
    ];

    public static DataSet FillDirectory(string ldapServer, bool Ssl, string ldapPath, string userName, string password, string jsonMapping, string filter, Helper helper, DateTime lastRun)
    {
      var mapping = JsonConvert.DeserializeObject<Dictionary<string, string>>(jsonMapping) ?? throw new ArgumentException("JSON mapping could not be deserialized or is null.", nameof(jsonMapping));
      var configuredMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

      foreach (var column in mapping)
      {
        configuredMapping[column.Key.Trim()] = column.Value;
      }

      DataSet dataSet = new();
      DataTable dataTable = new DataTable();

      // Keep output schema aligned with Program.cs import processing.
      foreach (var column in DefaultOutputColumns)
      {
        dataTable.Columns.Add(column);
      }

      // Keep any custom mapped columns as passthrough values.
      foreach (var column in configuredMapping.Keys)
      {
        if (!dataTable.Columns.Contains(column))
          dataTable.Columns.Add(column);
      }

      try
      {
        // Create LDAP connection
        using (var ldapConnection = new LdapConnection(ldapServer))
        {
          // Set credentials
          var credentials = new NetworkCredential(userName, password);
          ldapConnection.Credential = credentials;
          ldapConnection.AuthType = AuthType.Basic;

          // Set LDAP version to 3
          ldapConnection.SessionOptions.ProtocolVersion = 3;

          // Optional: Use SSL/TLS
          if (Ssl)
          {
            ldapConnection.SessionOptions.SecureSocketLayer = true;
            // ldapConnection.SessionOptions.VerifyServerCertificate += (con, cer) => false;
          }

          // Connect to the LDAP server
          ldapConnection.Bind();

          var propertiesToLoad = new List<string>(configuredMapping.Values);
          propertiesToLoad.Add("userAccountControl");
          propertiesToLoad.Add("whenChanged");

          var searchRequest = new SearchRequest(ldapPath, filter, SearchScope.Subtree, propertiesToLoad.ToArray());
          const int LdapPageSize = 500;
          var pageRequestControl = new PageResultRequestControl(LdapPageSize);
          searchRequest.Controls.Add(pageRequestControl);
          int pageNumber = 0;
          int totalReturnedEntries = 0;

          helper.Message($"LDAP search started. Path='{ldapPath}', Filter='{filter}', Scope='{SearchScope.Subtree}', PageSize={LdapPageSize}", 2);

          while (true)
          {
            pageNumber++;
            var searchResponse = (SearchResponse)ldapConnection.SendRequest(searchRequest);
            int pageReturnedEntries = searchResponse.Entries.Count;
            totalReturnedEntries += pageReturnedEntries;
            helper.Message($"LDAP page {pageNumber}: returned {pageReturnedEntries} entries.", 2);

            // Iterate over results and add rows to DataTable.
            foreach (SearchResultEntry entry in searchResponse.Entries)
            {
              var whenChangedAttr = entry.Attributes["whenChanged"];
              if (whenChangedAttr != null && whenChangedAttr.Count > 0)
              {
                string whenChangedString = whenChangedAttr[0]?.ToString() ?? string.Empty;
                if (DateTime.TryParseExact(whenChangedString, "yyyyMMddHHmmss.0Z", null, System.Globalization.DateTimeStyles.AssumeUniversal, out DateTime whenChanged))
                {
                  if (whenChanged <= lastRun)
                  {
                    helper.Message($"SKIP: record not changed", 2);
                    continue; // Skip records that have not been changed since the last run.
                  }
                }
                else
                {
                  helper.Message($"Failed to parse whenChanged value: {whenChangedString}");
                  continue; // Skip records with invalid whenChanged value.
                }
              }

              DataRow row = dataTable.NewRow();
              foreach (var column in configuredMapping)
              {
                var attribute = entry.Attributes[column.Value];
                row[column.Key] = attribute?.Count > 0 ? attribute[0].ToString() : DBNull.Value;
              }

              // Check if the user is deactivated.
              if (entry.Attributes["userAccountControl"]?.Count > 0)
              {
                var userAccountControlString = entry.Attributes["userAccountControl"][0].ToString();
                if (int.TryParse(userAccountControlString, out int userAccountControl))
                {
                  const int ADS_UF_ACCOUNTDISABLE = 0x0002;
                  if ((userAccountControl & ADS_UF_ACCOUNTDISABLE) == ADS_UF_ACCOUNTDISABLE)
                    row["Austritt am"] = DateTime.Now.ToString("dd.MM.yyyy");
                }
                else
                  helper.Message($"Failed to parse userAccountControl value: {userAccountControlString}");
              }

              dataTable.Rows.Add(row);
            }

            PageResultResponseControl? pageResponseControl = null;
            foreach (DirectoryControl control in searchResponse.Controls)
            {
              if (control is PageResultResponseControl responseControl)
              {
                pageResponseControl = responseControl;
                break;
              }
            }

            if (pageResponseControl == null || pageResponseControl.Cookie == null || pageResponseControl.Cookie.Length == 0)
              break;

            pageRequestControl.Cookie = pageResponseControl.Cookie;
          }

          helper.Message($"LDAP search completed. Path='{ldapPath}', Filter='{filter}', ReturnedEntries={totalReturnedEntries}, ImportedRows={dataTable.Rows.Count}");
        }
      }
      catch (LdapException ex)
      {
        helper.MessageAndExit($"LDAP Exception: {ex.Message}");
        // Handle the exception based on your application's requirements
      }
      catch (Exception ex)
      {
        helper.MessageAndExit($"General Exception: {ex.Message}");
        // Handle the exception based on your application's requirements
      }

      dataSet.Tables.Add(dataTable);
      return dataSet;
    }
  }
}
