namespace SamedisStaffSync
{
  public abstract class ConfigProvider
  {
    #region CONSTANTS
    private readonly NullReferenceException NOT_FOUND_EXCEPTION = new("Couldn't find given config entry");
    #endregion

    #region SETTINGS
    /// <summary>
    /// Throw an exception if the given config entry is not found and no default value has been given
    /// </summary>
    public bool ThrowIfEntryNotFound { get; set; } = true;
    /// <summary>
    /// Write defaults to file automatically?
    /// </summary>
    public bool WriteDefaults { get; set; } = false;

    #endregion

    #region CUSTOM_IMPL
    /// <summary>
    /// Gets string at path
    /// </summary>
    /// <param name="path">The path in format this.is.a.path</param>
    /// <returns>The given config entry as string or null</returns>
    protected abstract string GetString(string path);

    /// <summary>
    /// Gets string array at path
    /// </summary>
    /// <param name="path">The path in format this.is.a.path</param>
    /// <returns>The given config entry as string array or null</returns>
    protected abstract string[] GetStringArray(string path);
    protected abstract void SetString(string path, string val);
    protected abstract void SetStringArray(string path, string[] vals);
    /// <summary>
    /// Gets the keys present at given path
    /// </summary>
    /// <param name="path">May be empty for root node</param>
    /// <returns>a array of keys usable in path</returns>
    public abstract string[] GetKeys(string path);
    public abstract void Save();

    #endregion

    #region GETTER

    public T Get<T>(string path)
    {
      var val = GetString(path);
      if (val == null)
      {
        if (ThrowIfEntryNotFound)
          throw NOT_FOUND_EXCEPTION;
        else
          return default;
      }

      return (T)Convert.ChangeType(val, typeof(T));
    }

    public T Get<T>(string path, T def)
    {
      var val = GetString(path);
      if (val == null)
      {
        if (WriteDefaults)
        {
          Set(path, def);
        }
        return def;
      }

      return (T)Convert.ChangeType(val, typeof(T));
    }

    public T[] GetArray<T>(string path)
    {
      var val = GetStringArray(path);
      if (val == null)
      {
        if (ThrowIfEntryNotFound)
          throw NOT_FOUND_EXCEPTION;
        else
          return null;
      }

      return ToArray<T>(val);
    }

    public T[] GetArray<T>(string path, T[] def)
    {
      var val = GetStringArray(path);
      if (val == null)
      {
        if (WriteDefaults)
        {
          SetArray(path, def);
        }
        return def;
      }

      return ToArray<T>(val);
    }

    #endregion

    #region SETTER

    public void Set<T>(string path, T val)
    {
      SetString(path, val.ToString());
    }

    public void SetArray<T>(string path, T[] vals)
    {
      var strs = new List<string>();
      foreach (var val in vals)
      {
        strs.Add(val.ToString());
      }
      SetStringArray(path, strs.ToArray());
    }

    #endregion

    #region HELPER

    private static T[] ToArray<T>(string[] vals)
    {
      var list = new List<T>();
      foreach (var val in vals)
      {
        list.Add((T)Convert.ChangeType(val, typeof(T)));
      }
      return list.ToArray();
    }

    #endregion

  }
}
