namespace AutomationPortal.App.Pages;

public enum AppPage { Library, Pinned, Folder, Automations, Snippets, Gallery, History, Settings }

/// <summary>Where to go in the main window. <see cref="Item"/> is page specific (a run id in History, a gallery file name).</summary>
public sealed record NavTarget(AppPage Page, Guid? FolderId = null, string? Query = null, string? Item = null);

/// <summary>Implemented by every page shown in the main window.</summary>
public interface IAppPage
{
    /// <summary>Called every time the page is (re)shown, with where the user wanted to go.</summary>
    void Navigated(NavTarget target);
}
