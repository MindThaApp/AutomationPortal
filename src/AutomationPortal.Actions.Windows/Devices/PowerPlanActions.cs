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
        Id = "dev.powerPlan.switch", Title = Strings.Get("SwitchPowerPlanAction_Title"), Category = ActionCategories.DeviceAndSettings, Icon = "Flash24",
        Keywords = new[] { "power plan", "balanced", "high performance", "power saver", "battery", "scheme" },
        Summary = Strings.Get("SwitchPowerPlanAction_Summary"),
        Description = Strings.Get("SwitchPowerPlanAction_Description"),
        IsDestructive = true, OutputKind = ContentKind.Text, OutputName = Strings.Get("SwitchPowerPlanAction_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.DynamicChoice("plan", Strings.Get("SwitchPowerPlanAction_Param_Plan_Label"), (_, _) =>
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
                ? Strings.Get("SwitchPowerPlanAction_Error_ChoosePlan")
                : Strings.Format("SwitchPowerPlanAction_Error_NoSuchPlan", wanted, string.Join(", ", plans.Select(p => p.Name))));
        try { api.SetActive(plan.Id); }
        catch (InvalidOperationException ex) { throw new ActionException(Strings.Format("SwitchPowerPlanAction_Error_Windows_Wouldn_T_Switch_To_The", plan.Name), ex); }
        return ContentValue.Text(plan.Name);
    }
}

public sealed class GetCurrentPowerPlanAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "dev.powerPlan.get", Title = Strings.Get("GetCurrentPowerPlanAction_Title"), Category = ActionCategories.DeviceAndSettings, Icon = "Flash24",
        Keywords = new[] { "power plan", "active plan", "scheme", "which plan" },
        Summary = Strings.Get("GetCurrentPowerPlanAction_Summary"),
        Description = Strings.Get("GetCurrentPowerPlanAction_Description"),
        OutputKind = ContentKind.Text, OutputName = Strings.Get("GetCurrentPowerPlanAction_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("info", Strings.Get("GetCurrentPowerPlanAction_Param_Info_Label"), "name", new Choice("name", Strings.Get("GetCurrentPowerPlanAction_Param_Info_Choice_Name")), new Choice("details", Strings.Get("GetCurrentPowerPlanAction_Param_Info_Choice_Details"))),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var plan = PowerPlanApis.Current.GetActive() ?? throw new ActionException(Strings.Get("GetCurrentPowerPlanAction_Error_Windows_Didn_T_Say_Which_Power"));
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
        Id = "dev.powerPlan.list", Title = Strings.Get("ListPowerPlansAction_Title"), Category = ActionCategories.DeviceAndSettings, Icon = "Flash24",
        Keywords = new[] { "power plans", "schemes", "all plans" },
        Summary = Strings.Get("ListPowerPlansAction_Summary"),
        Description = Strings.Get("ListPowerPlansAction_Description"),
        OutputKind = ContentKind.Text, OutputName = Strings.Get("ListPowerPlansAction_OutputName"),
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
    public static string NotInstalled => Strings.Get("PowerPlanManagerLinks_NotInstalled");
    public const string StoreLink = "ms-windows-store://search/?query=Power%20Plan%20Manager";

    public static readonly string[] Pages =
    {
        "General", "PowerPlans", "Automation", "Shortcuts", "Presets", "History", "Power", "EnergyHistory",
        "EnergySettings", "Fans", "Sensors", "Battery", "UI", "Profiles", "About",
    };

    public static string ValidName(string? name, string what)
    {
        var n = name?.Trim() ?? "";
        if (n.Length == 0) throw new ActionException(Strings.Format("PowerPlanManagerLinks_Error_Enter_The_0_Name", what));
        if (n.Length > MaxNameLength) throw new ActionException(Strings.Format("PowerPlanManagerLinks_Error_The_0_Name_Is_1", what, n.Length, MaxNameLength));
        if (n.Any(char.IsControl)) throw new ActionException(Strings.Format("PowerPlanManagerLinks_Error_The_0_Name_Can_T_Contain", what));
        return n;
    }

    public static string ValidLimit(double value)
    {
        if (value != Math.Floor(value) || value < 50 || value > 100)
            throw new ActionException(Strings.Get("PowerPlanManagerLinks_Error_The_Charge_Limit_Must_Be"));
        return ((int)value).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public static string ValidPage(string? page) =>
        Pages.FirstOrDefault(p => string.Equals(p, page?.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? throw new ActionException(Strings.Format("PowerPlanManagerLinks_Error_Power_Plan_Manager_Has_No", page));

    public static string Build(string scheme, string command, params (string Key, string Value)[] query)
    {
        var link = scheme + ":" + command;
        if (query.Length > 0)
            link += "?" + string.Join("&", query.Select(q => q.Key + "=" + Uri.EscapeDataString(q.Value)));
        if (link.Length > MaxLinkLength)
            throw new ActionException(Strings.Format("PowerPlanManagerLinks_Error_That_Link_Would_Be_0", link.Length, MaxLinkLength));
        return link;
    }

    /// <summary>The full app wins. Without it, only the commands the Lite edition understands can go to the Lite app.</summary>
    public static string ResolveScheme(bool needsPro)
    {
        if (Protocols.IsRegistered("powerplanmanager")) return "powerplanmanager";
        if (!needsPro && Protocols.IsRegistered("powerplanmanager-lite")) return "powerplanmanager-lite";
        if (needsPro && Protocols.IsRegistered("powerplanmanager-lite"))
            throw new ActionException(Strings.Get("PowerPlanManagerLinks_Error_This_Needs_Power_Plan_Manager"));
        throw new ActionException(NotInstalled);
    }

    public static async Task SendAsync(bool needsPro, string command, CancellationToken ct, params (string Key, string Value)[] query)
    {
        var link = Build(ResolveScheme(needsPro), command, query);
        await PpmPipe.EnsureLinksAllowedAsync(ct).ConfigureAwait(false);
        try { Launcher.Launch(link); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new ActionException(Strings.Get("PowerPlanManagerLinks_Error_Windows_Couldn_T_Open_Power_Plan"), ex);
        }
    }

    public static string Note => Strings.Get("PowerPlanManagerLinks_Note");
    public static string ProNote => Strings.Get("PowerPlanManagerLinks_ProNote");
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
    private static readonly ActionDescriptor D = Make("plan", Strings.Get("PpmSwitchPlanAction_Title"), Strings.Get("PpmSwitchPlanAction_Summary"),
        Strings.Get("PpmSwitchPlanAction_Description"), false, ParameterDescriptor.Text("name", Strings.Get("PpmSwitchPlanAction_Param_Name_Label"), null, "Balanced"));
    public override ActionDescriptor Descriptor => D;
    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        await PowerPlanManagerLinks.SendAsync(false, "plan", ctx.CancellationToken, ("name", PowerPlanManagerLinks.ValidName(await ctx.GetTextAsync("name"), "plan")));
        return ContentValue.Empty;
    }
}

public sealed class PpmApplyPresetAction : PowerPlanManagerAction
{
    private static readonly ActionDescriptor D = Make("preset", Strings.Get("PpmApplyPresetAction_Title"), Strings.Get("PpmApplyPresetAction_Summary"),
        Strings.Get("PpmApplyPresetAction_Description"), true, ParameterDescriptor.Text("name", Strings.Get("PpmApplyPresetAction_Param_Name_Label"), null, "Travel"));
    public override ActionDescriptor Descriptor => D;
    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        await PowerPlanManagerLinks.SendAsync(true, "preset", ctx.CancellationToken, ("name", PowerPlanManagerLinks.ValidName(await ctx.GetTextAsync("name"), "preset")));
        return ContentValue.Empty;
    }
}

public sealed class PpmSetChargeLimitAction : PowerPlanManagerAction
{
    private static readonly ActionDescriptor D = Make("limit", Strings.Get("PpmSetChargeLimitAction_Title"), Strings.Get("PpmSetChargeLimitAction_Summary"),
        Strings.Get("PpmSetChargeLimitAction_Description"), true, ParameterDescriptor.Number("value", Strings.Get("PpmSetChargeLimitAction_Param_Value_Label"), 80, 50, 100, "%"));
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
    private static readonly ActionDescriptor D = Make("topup", Strings.Get("PpmTopUpAction_Title"), Strings.Get("PpmTopUpAction_Summary"),
        Strings.Get("PpmTopUpAction_Description"), true);
    public override ActionDescriptor Descriptor => D;
    protected override string Command => "topup";
}

public sealed class PpmPauseChargingAction : PpmBatteryCommandAction
{
    private static readonly ActionDescriptor D = Make("pause", Strings.Get("PpmPauseChargingAction_Title"), Strings.Get("PpmPauseChargingAction_Summary"),
        Strings.Get("PpmPauseChargingAction_Description"), true);
    public override ActionDescriptor Descriptor => D;
    protected override string Command => "pause";
}

public sealed class PpmResumeChargingAction : PpmBatteryCommandAction
{
    private static readonly ActionDescriptor D = Make("resume", Strings.Get("PpmResumeChargingAction_Title"), Strings.Get("PpmResumeChargingAction_Summary"),
        Strings.Get("PpmResumeChargingAction_Description"), true);
    public override ActionDescriptor Descriptor => D;
    protected override string Command => "resume";
}

public sealed class PpmCalibrateAction : PpmBatteryCommandAction
{
    private static readonly ActionDescriptor D = Make("calibrate", Strings.Get("PpmCalibrateAction_Title"), Strings.Get("PpmCalibrateAction_Summary"),
        Strings.Get("PpmCalibrateAction_Description"), true);
    public override ActionDescriptor Descriptor => D;
    protected override string Command => "calibrate";
}

public sealed class PpmKeepAwakeAction : PowerPlanManagerAction
{
    private static readonly ActionDescriptor D = Make("keepawake", Strings.Get("PpmKeepAwakeAction_Title"), Strings.Get("PpmKeepAwakeAction_Summary"),
        Strings.Get("PpmKeepAwakeAction_Description"), false,
        ParameterDescriptor.ChoiceOf("state", Strings.Get("PpmKeepAwakeAction_Param_State_Label"), "on", new Choice("on", Strings.Get("PpmKeepAwakeAction_Param_State_Choice_On")), new Choice("off", Strings.Get("PpmKeepAwakeAction_Param_State_Choice_Off"))));
    public override ActionDescriptor Descriptor => D;
    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        await PowerPlanManagerLinks.SendAsync(false, "keepawake", ctx.CancellationToken, ("on", await ctx.GetChoiceAsync("state", "on") == "off" ? "false" : "true"));
        return ContentValue.Empty;
    }
}

public sealed class PpmOpenAction : PowerPlanManagerAction
{
    private static readonly ActionDescriptor D = Make("show", Strings.Get("PpmOpenAction_Title"), Strings.Get("PpmOpenAction_Summary"),
        Strings.Get("PpmOpenAction_Description"), false,
        ParameterDescriptor.ChoiceOf("page", Strings.Get("PpmOpenAction_Param_Page_Label"), "General", PowerPlanManagerLinks.Pages.Select(p => new Choice(p, p)).ToArray()));
    public override ActionDescriptor Descriptor => D;
    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        await PowerPlanManagerLinks.SendAsync(false, "show", ctx.CancellationToken, ("page", PowerPlanManagerLinks.ValidPage(await ctx.GetChoiceAsync("page", "General"))));
        return ContentValue.Empty;
    }
}
