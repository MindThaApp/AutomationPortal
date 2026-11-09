using System.Windows.Media;
using Flowmate.Core;

namespace Flowmate.Editor;

/// <summary>
/// Lets the host supply the list of installed apps and their icons for every App parameter (editor, triggers, conditions).
/// Choice ids are what <c>AppValue.Id</c> stores: the program's .exe path, or the AppUserModelId of a Store app.
/// </summary>
public static class AppPickerHooks
{
    /// <summary>All installed apps. Used for App parameters that have no choices of their own.</summary>
    public static ChoicesProvider? Provider { get; set; }

    /// <summary>Icon for a choice id from <see cref="Provider"/>. May be slow: it is called off the UI thread and must return a frozen image.</summary>
    public static Func<string, ImageSource?>? IconLoader { get; set; }
}
