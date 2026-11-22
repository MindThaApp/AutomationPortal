using AutomationPortal.Actions.Documents;
using AutomationPortal.Core;
using AutomationPortal.Editor;
using AutomationPortal.Editor.UI;

namespace AutomationPortal.App.Services;

/// <summary>Wires the Explain / Fix it panel to the app's AI settings for a failed run. Nothing here saves or runs a shortcut.</summary>
internal static class ExplainFix
{
    public static ExplainFixPanel PanelFor(AppHost host, Shortcut shortcut, RunRecord run)
    {
        var snapshot = shortcut.Clone();
        return new ExplainFixPanel(new ExplainFixSource(
            async ct =>
            {
                var r = await ShortcutAiService.ExplainErrorAsync(host.Services, host.Actions, host.Triggers, snapshot, run, ct);
                return new AiExplanation(r.Text, r.Error, r.NeedsSetup);
            },
            ct => ShortcutAiService.FixAsync(host.Services, host.Actions, host.Triggers, snapshot, run, ct),
            fix => (host.EditorLauncher as EditorLauncher)?.OpenSuggestedFix(snapshot, fix),
            host.OpenAiSettings,
            () => ShortcutAiService.IsReady(host.Services),
            ShortcutAiService.PrivacyLine(host.Services),
            FixPrivacyLine: ShortcutAiService.PrivacyLine(host.Services, forFix: true)));
    }
}
