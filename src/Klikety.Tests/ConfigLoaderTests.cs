using System.IO;
using Klikety.Config;

namespace Klikety.Tests;

public class ConfigLoaderTests
{
    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var result = ConfigLoader.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"));
        Assert.Empty(result.Violations);
        Assert.Equal(NavigationMode.Both, result.Config.NavigationMode);
    Assert.Equal(4, result.Config.KeySets.Left.FirstKeys.Length);
    Assert.Equal(4, result.Config.KeySets.Left.SecondKeys.Length);
    Assert.Equal(4, result.Config.KeySets.Right.FirstKeys.Length);
    Assert.Equal(4, result.Config.KeySets.Right.SecondKeys.Length);
  }

    [Fact]
    public void Load_ValidJsonc_ParsesCorrectly()
    {
        var json = """
        {
            // comment
            "hotKey": { "modifiers": "Control", "key": "OemTilde" },
            "navigationMode": "twoKey",
            "level3CellSizeThreshold": 25000
        }
        """;
        var path = WriteTempFile(json);
        try
        {
            var result = ConfigLoader.Load(path);
            Assert.Equal(HotKeyModifiers.Control, result.Config.HotKey.Modifiers);
            Assert.Equal(Input.VKey.OemTilde, result.Config.HotKey.Key);
            Assert.Equal(NavigationMode.TwoKey, result.Config.NavigationMode);
            Assert.Equal(25000, result.Config.Level3CellSizeThreshold);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_MalformedJson_ReturnsDefaults()
    {
        var path = WriteTempFile("{invalid json}}}");
        try
        {
            var result = ConfigLoader.Load(path);
            Assert.Equal(NavigationMode.Both, result.Config.NavigationMode);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_ReservedKeyInFirstKeys_ReportsViolation()
    {
        var json = """
        {
            "keySets": {
                "left": { "firstKeys": ["A", "S", "Escape", "F"], "secondKeys": ["W", "E", "R", "T"] },
                "right": { "firstKeys": ["J", "K", "L", "OemSemicolon"], "secondKeys": ["Y", "U", "I", "O"] }
            }
        }
        """;
        var path = WriteTempFile(json);
        try
        {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("Escape") && v.Contains("firstKeys"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_ReservedKeyInSecondKeys_ReportsViolation()
    {
        var json = """
        {
            "keySets": {
                "left": { "firstKeys": ["A", "S", "D", "F"], "secondKeys": ["W", "Return", "R", "T"] },
                "right": { "firstKeys": ["J", "K", "L", "OemSemicolon"], "secondKeys": ["Y", "U", "I", "O"] }
            }
        }
        """;
        var path = WriteTempFile(json);
        try
        {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("Return") && v.Contains("secondKeys"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_OverlappingFirstAndSecondKeys_ReportsViolation()
    {
        var json = """
        {
            "keySets": {
                "left": { "firstKeys": ["A", "S", "D", "W"], "secondKeys": ["W", "E", "R", "T"] },
                "right": { "firstKeys": ["J", "K", "L", "OemSemicolon"], "secondKeys": ["Y", "U", "I", "O"] }
            }
        }
        """;
        var path = WriteTempFile(json);
        try
        {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("W") && v.Contains("both"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_ActionKeyConflictsWithNavKey_ReportsViolation()
    {
        var json = """
        {
            "keySets": {
                "left": { "firstKeys": ["A", "S", "D", "F"], "secondKeys": ["W", "E", "R", "T"] },
                "right": { "firstKeys": ["J", "K", "L", "OemSemicolon"], "secondKeys": ["Y", "U", "I", "O"] }
            },
            "actionBindings": {
                "A": "RightClick"
            }
        }
        """;
        var path = WriteTempFile(json);
        try
        {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("A") && v.Contains("conflicts"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_DuplicateFirstKeys_ReportsViolation()
    {
        var json = """
        {
            "keySets": {
                "left": { "firstKeys": ["A", "S", "A", "F"], "secondKeys": ["W", "E", "R", "T"] },
                "right": { "firstKeys": ["J", "K", "L", "OemSemicolon"], "secondKeys": ["Y", "U", "I", "O"] }
            }
        }
        """;
        var path = WriteTempFile(json);
        try
        {
            var result = ConfigLoader.Load(path);
            Assert.Contains(result.Violations, v => v.Contains("firstKeys") && v.Contains("duplicate"));
        }
        finally { File.Delete(path); }
    }

    private static string WriteTempFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        File.WriteAllText(path, content);
        return path;
    }
}
