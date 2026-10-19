using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

/// <summary>One entry in the server list. Strings = the server's rsc0000 resource file (each official server has its own).</summary>
public class ServerProfile
{
    public string Name { get; set; } = "";
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 5959;
    public string Strings { get; set; } = "";       // path relative to the project folder, or absolute
    public string User { get; set; } = "";
    public bool Builtin { get; set; }
}

public class ServerList
{
    public List<ServerProfile> Servers { get; set; } = new List<ServerProfile>();
    public int Selected { get; set; }

    static readonly JsonSerializerOptions Opts = new JsonSerializerOptions { WriteIndented = true };

    public static ServerList Load(string path)
    {
        ServerList list = null;
        try { if (File.Exists(path)) list = JsonSerializer.Deserialize<ServerList>(File.ReadAllText(path)); } catch { }
        list ??= new ServerList();
        // make sure the built-in entries exist (user edits to them are kept)
        foreach (var d in Defaults())
            if (!list.Servers.Exists(s => s.Name == d.Name)) list.Servers.Add(d);
        if (list.Selected < 0 || list.Selected >= list.Servers.Count) list.Selected = 0;
        return list;
    }

    public void Save(string path)
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, JsonSerializer.Serialize(this, Opts)); } catch { }
    }

    static IEnumerable<ServerProfile> Defaults()
    {
        yield return new ServerProfile { Name = "Local server", Host = "127.0.0.1", Port = 5959, Strings = "server/rsc0000.rsb", Builtin = true };
        yield return new ServerProfile { Name = "Official 101", Host = "meridian101.meridian59.com", Port = 5901, Strings = "client_files/resource/rsc0000-101.rsb", Builtin = true };
        yield return new ServerProfile { Name = "Official 102", Host = "meridian102.meridian59.com", Port = 5902, Strings = "client_files/resource/rsc0000-101.rsb", Builtin = true };
        yield return new ServerProfile { Name = "OpenMeridian 103", Host = "meridian103.openmeridian.org", Port = 5903, Strings = "client_files/resource/rsc0000-103.rsb", Builtin = true };
        yield return new ServerProfile { Name = "Meridian Next 105", Host = "meridian105.meridiannext.com", Port = 5905, Strings = "client_files/resource/rsc0000-105.rsb", Builtin = true };
    }
}
