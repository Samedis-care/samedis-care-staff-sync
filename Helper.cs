using System;
using System.Data;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;

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

    public static bool CheckColumnsExist(DataTable dataTable, string[] requiredColumns)
    {
      foreach (var columnName in requiredColumns)
      {
        if (!dataTable.Columns.Contains(columnName))
          return false;
      }
      return true;
    }

    public bool TryParseDate(string stringDate, out DateTime result)
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
        var record = JsonConvert.DeserializeObject<Staffs.Root>(check);
        MessageAndExit($"Sync stopped. {client.StatusCode} {record.Meta.Msg.Message}");
      }
    }

    internal string MessageAndExit(string errorMessage)
    {
      Message(errorMessage, 1);
      Environment.Exit(1);
      return null; // Unreachable Code, just for compiler
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