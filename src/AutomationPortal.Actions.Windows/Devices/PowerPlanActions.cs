using System.Diagnostics;
using AutomationPortal.Core.Platform;
using Microsoft.Win32;

namespace AutomationPortal.Actions.Windows.Devices;

internal static class PowerPlanApis
{
    /// <summary>Swapped for a fake in tests.</summary>
    public static IPowerPlanApi Current { get; set; } = new WindowsPowerPlanApi();
}

public sealed class SwitchPowerPlanAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "dev.powerPlan.switch", Title = "Switch Power Plan", Category = ActionCategories.DeviceAndSettings, Icon = "Flash24",
        Keywords = new[] { "power plan", "balanced", "high performance", "power saver", "battery", "scheme" },
        Summary = "Switch to the {plan} power plan",
        Description = "Switches Windows to one of its power plans, such as Balanced or Power saver. Gives back the plan's name. Plans the PC doesn't have cause an error that lists the plans it does have.",
        IsDestructive = true, OutputKind = ContentKind.Text, OutputName = "Power Plan",
        Parameters = new[]
        {
            ParameterDescriptor.DynamicChoice("plan", "Power plan", (_, _) =>
                Task.FromResult<IReadOnlyList<Choice>>(PowerPlanApis.Current.List().Select(p => new Choice(p.Name, p.Name)).ToList())),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var wanted = await ctx.GetChoiceAsync("plan");
        var api = PowerPlanApis.Current;
        var plans = api.List();
        var plan = PowerPlanLogic.Find(plans, wanted)
            ?? throw new ActionException(string.IsNullOrWhiteSpace(wanted)
                ? "Choose a power plan to switch to."
                : $"This PC doesn't have a power plan called \"{wanted}\". Available plans: {string.Join(", ", plans.Select(p => p.Name))}.");
        try { api.SetActive(plan.Id); }
        catch (InvalidOperationException ex) { throw new ActionException($"Windows wouldn't switch to the \"{plan.Name}\" power plan.", ex); }
        return ContentValue.Text(plan.Name);
    }
}

public sealed class GetCurrentPowerPlanAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "dev.powerPlan.get", Title = "Get Current Power Plan", Category = ActionCategories.DeviceAndSettings, Icon = "Flash24",
        Keywords = new[] { "power plan", "active plan", "scheme", "which plan" },
        Summary = "Get the current power plan {info}",
        Description = "Gets the name of the power plan Windows is using now, or its name and ID together.",
        OutputKind = ContentKind.Text, OutputName = "Power Plan",
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("info", "Get", "name", new Choice("name", "name"), new Choice("details", "details (name and ID)")),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var plan = PowerPlanApis.Current.GetActive() ?? throw new ActionException("Windows didn't say which power plan is active.");
        return await ctx.GetChoiceAsync("info", "name") == "details"
            ? ContentValue.Dict(new[]
            {
                new KeyValuePair<string, ContentValue>("Name", ContentValue.Text(plan.Name)),
                new KeyValuePair<string, ContentValue>("ID", ContentValue.Text(plan.Id.ToString())),
            }, plan.Name)
            : ContentValue.Text(plan.Name);
    }
}

public sealed class ListPowerPlansAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "dev.powerPlan.list", Title = "List Power Plans", Category = ActionCategories.DeviceAndSettings, Icon = "Flash24",
        Keywords = new[] { "power plans", "schemes", "all plans" },
        Summary = "List the power plans",
        Description = "Gives back the names of all the power plans on this PC.",
        OutputKind = ContentKind.Text, OutputName = "Power Plans",
    };
    public override ActionDescriptor Descriptor => D;

    public override Task<ContentValue> RunAsync(ActionContext ctx) =>
        Task.FromResult(ContentValue.Texts(PowerPlanApis.Current.List().Select(p => p.Name)));
}

// ---- Power Plan Manager (separate app, driven by its links) -----------------------------------------

public interface IProtocolRegistry { bool IsRegistered(string scheme); }

public interface ILinkLauncher { void Launch(string link); }

internal sealed class RegistryProtocols : IProtocolRegistry
{
    public bool IsRegistered(string scheme)
    {
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = hive.OpenSubKey(@"Software\Classes\" + scheme);
            if (key?.GetValue("URL Protocol") is not null) return true;
        }
        using var root = Registry.ClassesRoot.OpenSubKey(scheme);
        return root?.GetValue("URL Protocol") is not null;
    }
}

internal sealed class ShellLinkLauncher : ILinkLauncher
{
    public void Launch(string link) => Process.Start(new ProcessStartInfo(link) { UseShellExecute = true })?.Dispose();
}

internal static class PowerPlanManagerLinks
{
    public static IProtocolRegistry Protocols { get; set; } = new RegistryProtocols();
    public static ILinkLauncher Launcher { get; set; } = new ShellLinkLauncher();

    public const int MaxNameLength = 100, MaxLinkLength = 1024;
    public const string NotInstalled = "Power Plan Manager isn't installed. Get it from the Microsoft Store.";
    public const string StoreLink = "ms-windows-store://search/?query=Power%20Plan%20Manager";

    public static readonly string[] Pages =
    {
        "General", "PowerPlans", "Automation", "Shortcuts", "Presets", "History", "Power", "EnergyHistory",
        "EnergySettings", "Fans", "Sensors", "Battery", "UI", "Profiles", "About",
    };

    public static string ValidName(string? name, string what)
    {
        var n = name?.Trim() ?? "";
        if (n.Length == 0) throw new ActionException($"Enter the {what} name.");
        if (n.Length > MaxNameLength) throw new ActionException($"The {what} name is {n.Length} characters long. Power Plan Manager accepts up to {MaxNameLength}.");
        if (n.Any(char.IsControl)) throw new ActionException($"The {what} name can't contain control characters such as line breaks or tabs.");
        return n;
    }

    public static string ValidLimit(double value)
    {
        if (value != Math.Floor(value) || value < 50 || value > 100)
            throw new ActionException("The charge limit must be a whole number from 50 to 100 percent.");
        return ((int)value).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public static string ValidPage(string? page) =>
        Pages.FirstOrDefault(p => string.Equals(p, page?.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? throw new ActionException($"Power Plan Manager has no page called \"{page}\". Choose one from the list.");

    public static string Build(string scheme, string command, params (string Key, string Value)[] query)
    {
        var link = scheme + ":" + command;
        if (query.Length > 0)
            link += "?" + string.Join("&", query.Select(q => q.Key + "=" + Uri.EscapeDataString(q.Value)));
        if (link.Length > MaxLinkLength)
            throw new ActionException($"That link would be {link.Length} characters long. Power Plan Manager accepts up to {MaxLinkLength}.");
        return link;
    }

    /// <summary>The full app wins. Without it, only the commands the Lite edition understands can go to the Lite app.</summary>
    public static string ResolveScheme(bool needsPro)
    {
        if (Protocols.IsRegistered("powerplanmanager")) return "powerplanmanager";
        if (!needsPro && Protocols.IsRegistered("powerplanmanager-lite")) return "powerplanmanager-lite";
        if (needsPro && Protocols.IsRegistered("powerplanmanager-lite"))
            throw new ActionException("This needs Power Plan Manager Pro. Get it from the Microsoft Store.");
        throw new ActionException(NotInstalled);
    }

    public static async Task SendAsync(bool needsPro, string command, CancellationToken ct, params (string Key, string Value)[] query)
    {
        var link = Build(ResolveScheme(needsPro), command, query);
        await PpmPipe.EnsureLinksAllowedAsync(ct).ConfigureAwait(false);
        try { Launcher.Launch(link); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new ActionException("Windows couldn't open Power Plan Manager.", ex);
        }
    }

    public const string Note = " Power Plan Manager must have 'Allow links from other apps' turned on. It shows the result as a notification.";
    public const string ProNote = " Needs Power Plan Manager Pro; charging changes are confirmed in Power Plan Manager.";
}

public abstract class PowerPlanManagerAction : ShortcutAction
{
    protected static ActionDescriptor Make(string id, string title, string summary, string description, bool pro, params ParameterDescriptor[] parameters) => new()
    {
        Id = "ppm." + id, Title = title, Category = ActionCategories.DeviceAndSettings, Icon = "Flash24",
        Keywords = new[] { "power plan manager", "ppm", "power plan", "battery" },
        Summary = summary, Description = description + (pro ? PowerPlanManagerLinks.ProNote : "") + PowerPlanManagerLinks.Note,
        IsDestructive = true, HasOutput = false, Parameters = parameters,
    };
}

public sealed class PpmSwitchPlanAction : PowerPlanManagerAction
{
    private static readonly ActionDescriptor D = Make("plan", "Power Plan Manager: Switch Plan", "Switch to {name} in Power Plan Manager",
        "Asks Power Plan Manager to switch to a power plan by name.", false, ParameterDescriptor.Text("name", "Plan name", null, "Balanced"));
    public override ActionDescriptor Descriptor => D;
    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        await PowerPlanManagerLinks.SendAsync(false, "plan", ctx.CancellationToken, ("name", PowerPlanManagerLinks.ValidName(await ctx.GetTextAsync("name"), "plan")));
        return ContentValue.Empty;
    }
}

public sealed class PpmApplyPresetAction : PowerPlanManagerAction
{
    private static readonly ActionDescriptor D = Make("preset", "Power Plan Manager: Apply Preset", "Apply the {name} preset in Power Plan Manager",
        "Asks Power Plan Manager to apply a preset by name.", true, ParameterDescriptor.Text("name", "Preset name", null, "Travel"));
    public override ActionDescriptor Descriptor => D;
    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        await PowerPlanManagerLinks.SendAsync(true, "preset", ctx.CancellationToken, ("name", PowerPlanManagerLinks.ValidName(await ctx.GetTextAsync("name"), "preset")));
        return ContentValue.Empty;
    }
}

public sealed class PpmSetChargeLimitAction : PowerPlanManagerAction
{
    private static readonly ActionDescriptor D = Make("limit", "Power Plan Manager: Set Charge Limit", "Set the charge limit to {value}% in Power Plan Manager",
        "Asks Power Plan Manager to stop charging at a percentage from 50 to 100.", true, ParameterDescriptor.Number("value", "Charge limit", 80, 50, 100, "%"));
    public override ActionDescriptor Descriptor => D;
    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        await PowerPlanManagerLinks.SendAsync(true, "limit", ctx.CancellationToken, ("value", PowerPlanManagerLinks.ValidLimit(await ctx.GetNumberAsync("value", 80))));
        return ContentValue.Empty;
    }
}

public abstract class PpmBatteryCommandAction : PowerPlanManagerAction
{
    protected abstract string Command { get; }
    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        await PowerPlanManagerLinks.SendAsync(true, Command, ctx.CancellationToken);
        return ContentValue.Empty;
    }
}

public sealed class PpmTopUpAction : PpmBatteryCommandAction
{
    private static readonly ActionDescriptor D = Make("topup", "Power Plan Manager: Top Up Battery", "Top up the battery in Power Plan Manager",
        "Asks Power Plan Manager to charge the battery to full once.", true);
    public override ActionDescriptor Descriptor => D;
    protected override string Command => "topup";
}

public sealed class PpmPauseChargingAction : PpmBatteryCommandAction
{
    private static readonly ActionDescriptor D = Make("pause", "Power Plan Manager: Pause Charging", "Pause charging in Power Plan Manager",
        "Asks Power Plan Manager to pause charging.", true);
    public override ActionDescriptor Descriptor => D;
    protected override string Command => "pause";
}

public sealed class PpmResumeChargingAction : PpmBatteryCommandAction
{
    private static readonly ActionDescriptor D = Make("resume", "Power Plan Manager: Resume Charging", "Resume charging in Power Plan Manager",
        "Asks Power Plan Manager to resume charging.", true);
    public override ActionDescriptor Descriptor => D;
    protected override string Command => "resume";
}

public sealed class PpmCalibrateAction : PpmBatteryCommandAction
{
    private static readonly ActionDescriptor D = Make("calibrate", "Power Plan Manager: Calibrate Battery", "Calibrate the battery in Power Plan Manager",
        "Asks Power Plan Manager to start a battery calibration.", true);
    public override ActionDescriptor Descriptor => D;
    protected override string Command => "calibrate";
}

public sealed class PpmKeepAwakeAction : PowerPlanManagerAction
{
    private static readonly ActionDescriptor D = Make("keepawake", "Power Plan Manager: Keep Awake", "Turn Keep Awake {state} in Power Plan Manager",
        "Asks Power Plan Manager to turn its Keep Awake on or off.", false,
        ParameterDescriptor.ChoiceOf("state", "Keep awake", "on", new Choice("on", "on"), new Choice("off", "off")));
    public override ActionDescriptor Descriptor => D;
    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        await PowerPlanManagerLinks.SendAsync(false, "keepawake", ctx.CancellationToken, ("on", await ctx.GetChoiceAsync("state", "on") == "off" ? "false" : "true"));
        return ContentValue.Empty;
    }
}

public sealed class PpmOpenAction : PowerPlanManagerAction
{
    private static readonly ActionDescriptor D = Make("show", "Open Power Plan Manager", "Open the {page} page of Power Plan Manager",
        "Opens Power Plan Manager on a page you choose.", false,
        ParameterDescriptor.ChoiceOf("page", "Page", "General", PowerPlanManagerLinks.Pages.Select(p => new Choice(p, p)).ToArray()));
    public override ActionDescriptor Descriptor => D;
    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        await PowerPlanManagerLinks.SendAsync(false, "show", ctx.CancellationToken, ("page", PowerPlanManagerLinks.ValidPage(await ctx.GetChoiceAsync("page", "General"))));
        return ContentValue.Empty;
    }
}
