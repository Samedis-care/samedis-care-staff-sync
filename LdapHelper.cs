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
    public static DataSet FillDirectory(string ldapServer, bool Ssl, string ldapPath, string userName, string password, string jsonMapping, string filter, Helper helper, DateTime lastRun)
    {
      var mapping = JsonConvert.DeserializeObject<Dictionary<string, string>>(jsonMapping);
      DataSet dataSet = new();
      DataTable dataTable = new DataTable();

      // Add columns to the DataTable based on the JSON mapping
      foreach (var column in mapping.Keys)
      {
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

          var propertiesToLoad = new List<string>(mapping.Values);
          propertiesToLoad.Add("userAccountControl");
          propertiesToLoad.Add("whenChanged");

          var searchRequest = new SearchRequest(ldapPath, filter, SearchScope.Subtree, propertiesToLoad.ToArray());

          // Send the search request
          var searchResponse = (SearchResponse)ldapConnection.SendRequest(searchRequest);

          // Iterate over results and add rows to DataTable
          foreach (SearchResultEntry entry in searchResponse.Entries)
          {
            if (entry.Attributes["whenChanged"]?.Count > 0)
            {
              string whenChangedString = entry.Attributes["whenChanged"][0].ToString();
              if (DateTime.TryParseExact(whenChangedString, "yyyyMMddHHmmss.0Z", null, System.Globalization.DateTimeStyles.AssumeUniversal, out DateTime whenChanged))
              {
                if (whenChanged <= lastRun)
                {
                  helper.Message($"SKIP: record not changed", 2);
                  continue; // Skip records that have not been changed since the last run
                }
              }
              else
              {
                helper.Message($"Failed to parse whenChanged value: {whenChangedString}");
                continue; // Skip records with invalid whenChanged value
              }
            }

            DataRow row = dataTable.NewRow();
            foreach (var column in mapping)
            {
              var attribute = entry.Attributes[column.Value];
              row[column.Key] = attribute?.Count > 0 ? attribute[0].ToString() : DBNull.Value;
            }

            // Check if the user is deactivated
            if (entry.Attributes["userAccountControl"]?.Count > 0)
            {
              var userAccountControlString = entry.Attributes["userAccountControl"][0].ToString();
              if (int.TryParse(userAccountControlString, out int userAccountControl))
              {
                const int ADS_UF_ACCOUNTDISABLE = 0x0002;
                if ((userAccountControl & ADS_UF_ACCOUNTDISABLE) == ADS_UF_ACCOUNTDISABLE)
                  row["Austrittsdatum"] = DateTime.Now.ToString("dd.MM.yyyy");
              }
              else
                helper.Message($"Failed to parse userAccountControl value: {userAccountControlString}");
            }

            dataTable.Rows.Add(row);
          }
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
