using System.Diagnostics;
using HezbClips.Core.Hotkeys;

namespace HezbClips.Linux;

/// <summary>Installs/removes GNOME custom keybindings that launch `hezbclips --region` / `--full`.
/// Used where the GlobalShortcuts portal does not exist (GNOME &lt; 48).</summary>
public static class GnomeShortcuts
{
    const string Schema = "org.gnome.settings-daemon.plugins.media-keys";
    const string Key = "custom-keybindings";
    const string Base = "/org/gnome/settings-daemon/plugins/media-keys/custom-keybindings/";

    static string PathFor(string id) => $"{Base}hezbclips-{id}/";

    public static bool IsGnome =>
        (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "").Contains("GNOME", StringComparison.OrdinalIgnoreCase);

    static string Run(params string[] args)
    {
        var psi = new ProcessStartInfo("gsettings") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("gsettings failed to start");
        var o = p.StandardOutput.ReadToEnd();
        var e = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException($"gsettings {string.Join(' ', args)}: {e.Trim()}");
        return o;
    }

    /// <summary>The command a shortcut should run: this executable, or `dotnet app.dll` when run via dotnet.</summary>
    public static string LauncherCommand()
    {
        var exe = Environment.ProcessPath ?? "hezbclips";
        if (Path.GetFileNameWithoutExtension(exe) == "dotnet")
        {
            var dll = Path.Combine(AppContext.BaseDirectory, "hezbclips.dll");
            return $"\"{exe}\" \"{dll}\"";
        }
        return $"\"{exe}\"";
    }

    /// <summary>id → (display name, combo, cli flag)</summary>
    public static void Install(IReadOnlyDictionary<string, (string Name, string Combo, string Flag)> shortcuts)
    {
        var paths = GsettingsList.Parse(Run("get", Schema, Key)).ToList();
        foreach (var (id, s) in shortcuts)
        {
            var path = PathFor(id);
            var kb = $"{Schema}.custom-keybinding:{path}";
            Run("set", kb, "name", s.Name);
            Run("set", kb, "command", $"{LauncherCommand()} {s.Flag}");
            Run("set", kb, "binding", HotkeyFormats.ToGnome(s.Combo));
            if (!paths.Contains(path)) paths.Add(path);
        }
        Run("set", Schema, Key, GsettingsList.Format(paths));
    }

    public static void Remove(IEnumerable<string> ids)
    {
        var ours = ids.Select(PathFor).ToHashSet();
        var paths = GsettingsList.Parse(Run("get", Schema, Key)).Where(p => !ours.Contains(p)).ToList();
        Run("set", Schema, Key, GsettingsList.Format(paths));
        foreach (var p in ours) Run("reset-recursively", $"{Schema}.custom-keybinding:{p}");
    }
}
