using SamedisCare.Api;

namespace SamedisStaffSync
{
  /// <summary>
  /// Bridges the library's <see cref="ISyncLog"/> onto this tool's existing
  /// <see cref="Helper.Message"/> logging, so log level, log mode and the log file
  /// keep behaving exactly as before the migration to SamedisCare.Api.
  /// </summary>
  public sealed class SyncLogAdapter : ISyncLog
  {
    private readonly Helper _helper;

    public SyncLogAdapter(Helper helper) => _helper = helper;

    /// <summary>
    /// The library reads this to decide whether debug-level work is worth doing.
    /// Maps onto Helper.LogLevel (0=off, 1=info, 2=debug) unchanged.
    /// </summary>
    public int Level => _helper.LogLevel;

    // Helper.Message's second argument is the level the message requires, and it
    // suppresses anything above the configured LogLevel — hence 1 for info/warn and
    // 2 for debug, matching how the rest of this tool logs.
    public void Info(string message) => _helper.Message(message, 1);

    public void Warn(string message) => _helper.Message(message, 1, "WARN");

    public void Error(string message, Exception? ex = null)
      => _helper.Message(ex == null ? message : $"{message}: {ex.Message}", 1, "ERROR");

    public void Debug(string message) => _helper.Message(message, 2, "DEBUG");
  }
}
