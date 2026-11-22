using AutomationPortal.App.Logic;
using AutomationPortal.Core;
using Microsoft.Win32;

namespace AutomationPortal.App.Services;

/// <summary>The trial record as one small file (trial.json in the local data folder). Written atomically; only this file is touched.</summary>
public sealed class FileTrialStorage(string path) : ITrialStorage
{
    public string? Read()
    {
        var read = StoreFile.ReadText(path);
        return read.Status switch
        {
            StoreReadStatus.Ok => read.Text,
            StoreReadStatus.Missing => null,
            _ => throw new IOException(read.Error ?? "trial record unreadable"),
        };
    }

    public void Write(string text) => AtomicFile.WriteAllText(path, text);
}

/// <summary>
/// The second copy under HKCU, so deleting the data folder alone doesn't restart the trial. Only its own key is touched. In the Store
/// package Windows keeps it in the package's private registry. Never used with a custom data root (tests, demos).
/// </summary>
public sealed class RegistryTrialMarker(string keyPath = RegistryTrialMarker.DefaultKeyPath) : ITrialStorage
{
    public const string DefaultKeyPath = @"Software\Automation Portal\Trial";
    private const string ValueName = "Record";

    public string? Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        return key?.GetValue(ValueName) as string;
    }

    public void Write(string text)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath);
        key.SetValue(ValueName, text, RegistryValueKind.String);
    }
}
