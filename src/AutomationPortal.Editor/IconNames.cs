using Wpf.Ui.Controls;

namespace Flowmate.Editor;

/// <summary>Validated access to WPF-UI <see cref="SymbolRegular"/> icons stored by name (e.g. "Globe24"). The app can reuse this everywhere.</summary>
public static class IconNames
{
    public const string Fallback = "Flash24";

    private static readonly HashSet<string> Known = new(Enum.GetNames<SymbolRegular>(), StringComparer.Ordinal);

    public static bool IsValid(string? name) => !string.IsNullOrWhiteSpace(name) && Known.Contains(name);

    /// <summary>The symbol for <paramref name="name"/>, or Flash24 when the name is missing or unknown.</summary>
    public static SymbolRegular Resolve(string? name) =>
        IsValid(name) ? Enum.Parse<SymbolRegular>(name!) : SymbolRegular.Flash24;

    /// <summary>The name itself when valid, else "Flash24".</summary>
    public static string Normalize(string? name) => IsValid(name) ? name! : Fallback;

    /// <summary>~170 hand-picked icons shown in the icon picker. Every name is checked against the real enum at startup of the picker.</summary>
    public static IReadOnlyList<string> Curated => _curated ??= Build();
    private static IReadOnlyList<string>? _curated;

    private static readonly string[] Raw =
    {
        "Flash24", "Home24", "Star24", "Heart24", "Globe24", "Mail24", "Calendar24", "Clock24", "Alert24", "Bookmark24",
        "Camera24", "Image24", "Video24", "MusicNote124", "Speaker224", "Mic24", "Headphones24", "Phone24", "Chat24", "Comment24",
        "Document24", "DocumentText24", "Folder24", "FolderOpen24", "Open24", "Save24", "ArrowDownload24", "ArrowUpload24", "Share24", "Link24",
        "Search24", "Settings24", "Wrench24", "Options24", "LockClosed24", "Key24", "Shield24", "Person24", "People24", "Building24",
        "Cart24", "Money24", "Wallet24", "Gift24", "Tag24", "Pin24", "Location24", "Map24", "CompassNorthwest24", "Airplane24",
        "VehicleCar24", "VehicleBus24", "Run24", "Bed24", "Food24", "FoodPizza24", "DrinkCoffee24", "WeatherSunny24", "WeatherMoon24", "WeatherSnow24",
        "WeatherCloudy24", "Fire24", "Drop24", "LeafOne24", "HandWave24", "EmojiSmileSlight24", "Lightbulb24", "Battery024", "BatteryCharge24", "Power24",
        "PlugConnected24", "Wifi124", "Bluetooth24", "Desktop24", "Laptop24", "Tablet24", "Keyboard24", "Print24", "Scan24", "Screenshot24",
        "Window24", "WindowNew24", "Apps24", "AppsList24", "Grid24", "Table24", "List24", "TextBulletList24", "TextT24", "TextFont24",
        "Code24", "Braces24", "BracesVariable24", "Bug24", "Beaker24", "Calculator24", "NumberSymbol24", "Timer24", "ClockAlarm24", "History24",
        "ArrowSync24", "ArrowRepeatAll24", "ArrowShuffle24", "ArrowUndo24", "ArrowRight24", "ArrowLeft24", "ArrowUp24", "ArrowDown24", "Play24", "Pause24",
        "Stop24", "Record24", "FastForward24", "Rewind24", "Next24", "Previous24", "Speaker024", "SpeakerMute24", "Eye24", "EyeOff24",
        "Delete24", "Add24", "Checkmark24", "CheckmarkCircle24", "DismissCircle24", "Info24", "QuestionCircle24", "Warning24", "Pen24", "Edit24",
        "Copy24", "Clipboard24", "ClipboardPaste24", "Cut24", "Attach24", "Backpack24", "Briefcase24", "Book24", "BookOpen24", "Library24",
        "Games24", "Trophy24", "Ribbon24", "Diamond24", "Sparkle24", "Rocket24", "Target24", "Flag24", "Navigation24", "Pulse24",
        "HeartPulse24", "Stethoscope24", "Dumbbell24", "Cloud24", "CloudArrowUp24", "Database24", "Server24", "HardDrive24", "UsbStick24", "Archive24",
        "FolderZip24", "QrCode24", "Color24", "PaintBrush24", "Wand24", "Crop24", "Filter24", "DarkTheme24", "Premium24", "Translate24",
        "Brain24", "Bot24", "Megaphone24", "Send24", "Notebook24", "NoteEdit24", "Note24", "BuildingBank24", "ShoppingBag24", "Receipt24",
        "Toolbox24", "Cursor24", "CursorClick24", "Hourglass24", "CalendarClock24", "Rss24", "Cast24", "Umbrella24", "Glasses24", "Gauge24",
        "DataBarVertical24", "DataPie24", "DataTrending24", "DocumentPdf24", "ImageMultiple24", "VideoClip24", "WifiOff24", "BrightnessHigh24", "Flashlight24", "Handshake24",
        "PersonAdd24", "LockOpen24", "ShieldCheckmark24", "Fingerprint24", "Password24", "Beach24", "Luggage24", "Pill24", "AnimalDog24", "AnimalCat24",
        "TreeDeciduous24", "Gavel24", "Crown24", "Eraser24", "Highlight24", "Poll24", "TaskListLtr24", "CheckboxChecked24", "AppStore24", "BookmarkAdd24",
    };

    internal static IReadOnlyList<string> RawNames => Raw;

    private static IReadOnlyList<string> Build() => Raw.Where(IsValid).Distinct().ToArray();

    /// <summary>Plain-English search text for an icon name ("WeatherSunny24" becomes "weather sunny").</summary>
    internal static string Words(string name)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var c in name.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9'))
        {
            if (char.IsUpper(c) && sb.Length > 0) sb.Append(' ');
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}
