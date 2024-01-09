using YamlDotNet.RepresentationModel;

namespace SamedisStaffSync
{
  public class ConfigProviderYaml : ConfigProvider
  {
    private readonly string _file;
    private readonly YamlDocument _yml;
    public ConfigProviderYaml(string file)
    {
      _file = file;

      var ymlStream = new YamlStream();
      try
      {
        using (var stream = File.OpenRead(file))
        {
          ymlStream.Load(new StreamReader(stream));
          _yml = ymlStream.Documents.Count == 0 ? new YamlDocument(new YamlMappingNode()) : ymlStream.Documents[0];
        }
      }
      catch (IOException e)
      {
        Console.WriteLine(e);

        _yml = new YamlDocument(new YamlMappingNode());
      }
    }
    protected override string GetString(string path)
    {
      var node = GetYaml(path);
      if (node == null) return null;
      if (node.NodeType != YamlNodeType.Scalar)
      {
        throw new ArgumentException("Node is not Value Type");
      }
      return (node as YamlScalarNode).Value;
    }

    protected override string[] GetStringArray(string path)
    {
      var node = GetYaml(path);
      if (node == null) return null;
      if (node.NodeType != YamlNodeType.Sequence)
      {
        throw new ArgumentException("Node is not Value Array Type");
      }

      var arr = new List<string>();
      var yamlNodes = (node as YamlSequenceNode).Children;
      foreach (var child in yamlNodes)
      {
        if (child.NodeType != YamlNodeType.Scalar)
        {
          throw new ArgumentException("Child is not Value Type");
        }

        arr.Add((child as YamlScalarNode).Value);
      }

      return arr.ToArray();
    }

    protected override void SetString(string path, string val)
    {
      SetYaml(path, new YamlScalarNode(val));
    }

    protected override void SetStringArray(string path, string[] vals)
    {
      var childs = new List<YamlNode>();
      foreach (var val in vals)
      {
        childs.Add(new YamlScalarNode(val));
      }

      SetYaml(path, new YamlSequenceNode(childs.ToArray()));
    }

    public override string[] GetKeys(string path)
    {
      var yaml = GetYaml(path);
      if (yaml == null) return new string[0];
      if (yaml.NodeType != YamlNodeType.Mapping)
      {
        throw new ArgumentException("Given path is not a mapping");
      }

      var list = new List<string>();
      foreach (var child in (yaml as YamlMappingNode).Children.Keys)
      {
        list.Add((child as YamlScalarNode).Value);
      }

      return list.ToArray();
    }

    public override void Save()
    {
      var ymlStream = new YamlStream(_yml);

      var dir = Path.GetDirectoryName(_file);
      if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
      {
        Directory.CreateDirectory(dir);
      }

      using (var file = File.CreateText(_file))
      {
        ymlStream.Save(file, false);
      }
    }

    private YamlNode GetYaml(string path)
    {
      var pathParts = string.IsNullOrWhiteSpace(path) ? new string[0] : path.Split('.');
      var searchIndex = 0;

      var node = _yml.RootNode;

      // try to look up path
      while (searchIndex != pathParts.Length)
      {
        if (node.NodeType != YamlNodeType.Mapping)
        {
          throw new ArgumentException("Expected mapping, got " + node.NodeType + ". Invalid Path?");
        }

        // find child matching path
        var found = false;
        var yamlNodes = (node as YamlMappingNode).Children;
        
        foreach (var child in yamlNodes)
        {
          // if child matches path
          if ((child.Key as YamlScalarNode).Value != pathParts[searchIndex]) continue;
          // mark as found and move along
          node = child.Value;
          searchIndex++;
          found = true;

          // check type
          if (searchIndex != pathParts.Length && node.NodeType != YamlNodeType.Mapping)
          {
            // if wrong type
            return null;
          }

          break;
        }

        // if path not found
        if (!found)
        {
          return null;
        }
      }

      // we scanned the tree and found our wanted node
      return node;
    }

    private void SetYaml(string path, YamlNode value)
    {
      if (string.IsNullOrEmpty(path) || path.Contains(' '))
      {
        throw new ArgumentException("Invalid path");
      }

      var pathParts = path.Split('.');
      var searchIndex = 0;

      var node = _yml.RootNode;

      // try to look up path
      while (searchIndex != pathParts.Length)
      {
        if (node.NodeType != YamlNodeType.Mapping)
        {
          throw new ArgumentException("Expected mapping, got " + node.NodeType + ". Invalid Path?");
        }

        // find child matching path
        var found = false;
        var yamlNodes = (node as YamlMappingNode).Children;
        foreach (var child in yamlNodes)
        {
          var parent = node;
          // if child matches path
          if ((child.Key as YamlScalarNode).Value != pathParts[searchIndex]) continue;
          // mark as found and move along
          node = child.Value;
          searchIndex++;
          found = true;

          // check type
          if (searchIndex != pathParts.Length && node.NodeType != YamlNodeType.Mapping)
          {
            // if wrong type
            throw new ArgumentException("Wrong node type encountered");
          }

          if (searchIndex == pathParts.Length)
          {
            // we scanned the tree and can set our wanted node
            if (node.NodeType == YamlNodeType.Mapping)
            {
              var childs = (node as YamlMappingNode).Children;
              if (childs != null)
              {
                childs.Remove(child.Key);
                childs.Add(
                  new KeyValuePair<YamlNode, YamlNode>(new YamlScalarNode(pathParts[searchIndex - 1]), value));
              }
            }
            else
            {
              var childs = (parent as YamlMappingNode).Children;
              childs.Remove(child.Key);
              childs.Add(child.Key, value);
            }
          }

          break;
        }

        // if path not found
        if (found) continue;
        YamlNode newNode = new YamlMappingNode();

        if (searchIndex + 1 == pathParts.Length)
        {
          newNode = value;
        }

        (node as YamlMappingNode).Children.Add(
          new KeyValuePair<YamlNode, YamlNode>(new YamlScalarNode(pathParts[searchIndex]), newNode));
        node = newNode;
        searchIndex++;
      }
    }
  }
}
