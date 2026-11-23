using AutomationPortal.Core;

namespace AutomationPortal.App.Integration;

/// <summary>
/// Every path and registry location the Windows integration touches, so tests can point all of it at a throwaway
/// registry key and temp folders. <see cref="ForCurrentUser"/> is the real thing.
/// </summary>
public sealed class ShellOptions
{
    /// <summary>False = nothing is registered or written (custom data root without a sandbox).</summary>
    public bool Enabled { get; init; } = true;
    public bool Packaged { get; init; }
    /// <summary>False in the Microsoft Store package (no unvirtualized-resources capability): nothing is written to Start menu Programs or SendTo.</summary>
    public bool WriteStartAndSendTo { get; init; } = true;
    /// <summary>AutomationPortal.exe: what the registry verbs (protocol, file association, right-click menu, Start with Windows) run.</summary>
    public required string ExePath { get; init; }
    /// <summary>What .lnk files run. Same as <see cref="ExePath"/> unpackaged; the stable <c>ap.exe</c> alias when packaged (the package folder changes on every update).</summary>
    public required string LauncherPath { get; init; }
    /// <summary>HKCU subkey that holds Classes. Real: <c>Software\Classes</c>.</summary>
    public string ClassesKey { get; init; } = @"Software\Classes";
    /// <summary>HKCU subkey of the Run list. Real: <c>Software\Microsoft\Windows\CurrentVersion\Run</c>.</summary>
    public string RunKey { get; init; } = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public required string SendToFolder { get; init; }
    public required string DesktopFolder { get; init; }
    /// <summary>The user's Start menu "Programs" folder.</summary>
    public required string ProgramsFolder { get; init; }
    public required string IconsFolder { get; init; }
    public required string StateFile { get; init; }
    public bool NotifyShell { get; init; } = true;
    public bool UseJumpList { get; init; } = true;

    public const string RunValueName = "AutomationPortal";
    public const string StartMenuFolderName = "Automation Portal Shortcuts";
    public const string StartupTaskId = "AutomationPortalStartup";

    public static ShellOptions ForCurrentUser(AutomationPortalPaths paths)
    {
        var exe = Environment.ProcessPath ?? "AutomationPortal.exe";
        var packaged = PackageIdentity.IsPackaged;
        var alias = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "ap.exe");
        return new ShellOptions
        {
            Packaged = packaged,
            WriteStartAndSendTo = PackageCapabilities.UnvirtualizedWritesAllowed,
            ExePath = exe,
            LauncherPath = packaged ? alias : exe,
            SendToFolder = Environment.GetFolderPath(Environment.SpecialFolder.SendTo),
            DesktopFolder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            ProgramsFolder = Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            IconsFolder = paths.IconsFolder,
            StateFile = Path.Combine(paths.LocalRoot, "shell-state.json"),
        };
    }

    /// <summary>Everything under <paramref name="root"/> and a throwaway HKCU key. Used by tests and by AUTOMATIONPORTAL_SHELL_SANDBOX.</summary>
    public static ShellOptions Sandbox(string root, string registryRoot, string exePath, bool packaged = false, bool useJumpList = false) => new()
    {
        Packaged = packaged,
        ExePath = exePath,
        LauncherPath = exePath,
        ClassesKey = registryRoot + @"\Classes",
        RunKey = registryRoot + @"\Run",
        SendToFolder = Path.Combine(root, "SendTo"),
        DesktopFolder = Path.Combine(root, "Desktop"),
        ProgramsFolder = Path.Combine(root, "Programs"),
        IconsFolder = Path.Combine(root, "icons"),
        StateFile = Path.Combine(root, "shell-state.json"),
        NotifyShell = false,
        UseJumpList = useJumpList,
    };

    public static ShellOptions Disabled(AutomationPortalPaths paths) => new()
    {
        Enabled = false, ExePath = Environment.ProcessPath ?? "AutomationPortal.exe", LauncherPath = Environment.ProcessPath ?? "AutomationPortal.exe",
        SendToFolder = "", DesktopFolder = "", ProgramsFolder = "", IconsFolder = paths.IconsFolder, StateFile = Path.Combine(paths.LocalRoot, "shell-state.json"),
        NotifyShell = false, UseJumpList = false,
    };
}
