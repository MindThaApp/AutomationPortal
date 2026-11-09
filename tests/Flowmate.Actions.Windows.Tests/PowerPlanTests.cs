using Flowmate.Actions.Windows.Devices;
using Flowmate.Core;
using Flowmate.Core.Platform;
using Flowmate.Core.Testing;

namespace Flowmate.Actions.Windows.Tests;

[Collection("PowerPlanStatics")]
public class PowerPlanTests : IDisposable
{
    private sealed class FakeApi : IPowerPlanApi
    {
        public static readonly PowerPlan Balanced = new(Guid.NewGuid(), "Balanced");
        public static readonly PowerPlan Saver = new(Guid.NewGuid(), "Power saver");
        public PowerPlan Active = Balanced;
        public IReadOnlyList<PowerPlan> List() => new[] { Balanced, Saver };
        public PowerPlan? GetActive() => Active;
        public void SetActive(Guid id) => Active = List().Single(p => p.Id == id);
        public IDisposable Subscribe(Action<Guid> cb) => throw new NotSupportedException();
    }

    private sealed class FakeProtocols : IProtocolRegistry
    {
        public HashSet<string> Registered { get; } = new();
        public bool IsRegistered(string scheme) => Registered.Contains(scheme);
    }

    private sealed class FakeLauncher : ILinkLauncher
    {
        public List<string> Links { get; } = new();
        public void Launch(string link) => Links.Add(link);
    }

    private sealed class FakePipe : IPpmStatusClient
    {
        public Func<string, string?> Reply = _ => null;
        public List<string> Requests { get; } = new();
        public Task<string?> RequestAsync(string request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(Reply(request));
        }
    }

    private const string FullStatus = "{\"plan\":{\"name\":\"Balanced\",\"guid\":\"381b4222-f694-41f0-9685-ff5bb260df2e\"},\"preset\":\"Travel\",\"chargeLimit\":80,\"chargingState\":\"Paused\",\"keepAwake\":true,\"allowExternalLinks\":true,\"edition\":\"Pro\"}";

    private readonly FakePipe _pipe = new();
    private readonly FakeApi _api = new();
    private readonly FakeProtocols _protocols = new();
    private readonly FakeLauncher _launcher = new();

    public PowerPlanTests()
    {
        PowerPlanApis.Current = _api;
        PowerPlanManagerLinks.Protocols = _protocols;
        PowerPlanManagerLinks.Launcher = _launcher;
        PpmPipe.Client = _pipe;
    }

    public void Dispose()
    {
        PowerPlanApis.Current = new WindowsPowerPlanApi();
        PowerPlanManagerLinks.Protocols = new RegistryProtocols();
        PowerPlanManagerLinks.Launcher = new ShellLinkLauncher();
        PpmPipe.Client = new NamedPipeStatusClient();
    }

    private static async Task<ContentValue> Run(ShortcutAction action, params (string, ParamValue)[] p)
    {
        using var host = new TestHost();
        return await ActionTestHarness.RunAsync(host, action, p);
    }

    private static async Task<ActionException> Fails(ShortcutAction action, params (string, ParamValue)[] p)
    {
        using var host = new TestHost();
        return await Assert.ThrowsAsync<ActionException>(() => ActionTestHarness.RunAsync(host, action, p));
    }

    [Fact]
    public async Task List_gives_the_plan_names_from_the_api()
    {
        var r = await Run(new ListPowerPlansAction());
        Assert.Equal(new[] { "Balanced", "Power saver" }, r.Items.Select(i => i.DisplayText).ToArray());
    }

    [Fact]
    public async Task Get_current_gives_the_active_plan()
    {
        _api.Active = FakeApi.Saver;
        Assert.Equal("Power saver", (await Run(new GetCurrentPowerPlanAction())).ToString());
    }

    [Fact]
    public async Task Switch_changes_the_plan_by_name_in_any_case()
    {
        await Run(new SwitchPowerPlanAction(), ("plan", ParamValue.Choice("POWER SAVER")));
        Assert.Equal(FakeApi.Saver, _api.Active);
    }

    [Fact]
    public async Task Switch_to_a_missing_plan_lists_the_real_ones()
    {
        var ex = await Fails(new SwitchPowerPlanAction(), ("plan", ParamValue.Choice("Turbo")));
        Assert.Contains("Turbo", ex.Message);
        Assert.Contains("Balanced", ex.Message);
        Assert.Equal(FakeApi.Balanced, _api.Active);
    }

    [Fact]
    public void Plans_are_found_by_name_or_id()
    {
        var plans = _api.List();
        Assert.Equal(FakeApi.Saver, PowerPlanLogic.Find(plans, " power saver "));
        Assert.Equal(FakeApi.Balanced, PowerPlanLogic.Find(plans, FakeApi.Balanced.Id.ToString()));
        Assert.Null(PowerPlanLogic.Find(plans, ""));
    }

    [Theory]
    [InlineData(49, false)]
    [InlineData(50, true)]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public async Task Charge_limit_must_be_50_to_100(int value, bool ok)
    {
        _protocols.Registered.Add("powerplanmanager");
        if (ok)
        {
            await Run(new PpmSetChargeLimitAction(), ("value", value));
            Assert.Equal($"powerplanmanager:limit?value={value}", Assert.Single(_launcher.Links));
        }
        else
        {
            Assert.Contains("50 to 100", (await Fails(new PpmSetChargeLimitAction(), ("value", value))).Message);
            Assert.Empty(_launcher.Links);
        }
    }

    [Fact]
    public async Task Names_are_url_encoded()
    {
        _protocols.Registered.Add("powerplanmanager");
        await Run(new PpmSwitchPlanAction(), ("name", "Work & play/100%"));
        Assert.Equal("powerplanmanager:plan?name=Work%20%26%20play%2F100%25", Assert.Single(_launcher.Links));
    }

    [Fact]
    public async Task A_name_of_100_characters_is_fine_and_101_is_not()
    {
        _protocols.Registered.Add("powerplanmanager");
        await Run(new PpmApplyPresetAction(), ("name", new string('a', 100)));
        Assert.Single(_launcher.Links);
        var ex = await Fails(new PpmApplyPresetAction(), ("name", new string('a', 101)));
        Assert.Contains("101", ex.Message);
        Assert.Single(_launcher.Links);
    }

    [Fact]
    public async Task Control_characters_and_empty_names_are_refused()
    {
        _protocols.Registered.Add("powerplanmanager");
        Assert.Contains("control", (await Fails(new PpmSwitchPlanAction(), ("name", "a\nb"))).Message);
        Assert.Contains("Enter", (await Fails(new PpmSwitchPlanAction(), ("name", "  "))).Message);
        Assert.Empty(_launcher.Links);
    }

    [Fact]
    public void Links_over_1024_characters_are_refused()
    {
        var ex = Assert.Throws<ActionException>(() => PowerPlanManagerLinks.Build("powerplanmanager", "plan", ("name", new string('é', 200))));
        Assert.Contains("1024", ex.Message);
    }

    [Fact]
    public async Task The_lite_app_is_used_for_plain_commands_only()
    {
        _protocols.Registered.Add("powerplanmanager-lite");
        await Run(new PpmKeepAwakeAction(), ("state", ParamValue.Choice("off")));
        Assert.Equal("powerplanmanager-lite:keepawake?on=false", Assert.Single(_launcher.Links));
        await Run(new PpmOpenAction(), ("page", ParamValue.Choice("EnergyHistory")));
        Assert.Equal("powerplanmanager-lite:show?page=EnergyHistory", _launcher.Links[1]);
        Assert.Contains("Pro", (await Fails(new PpmTopUpAction())).Message);
        Assert.Equal(2, _launcher.Links.Count);
    }

    [Fact]
    public async Task The_full_app_wins_over_the_lite_app()
    {
        _protocols.Registered.Add("powerplanmanager");
        _protocols.Registered.Add("powerplanmanager-lite");
        await Run(new PpmSwitchPlanAction(), ("name", "Balanced"));
        Assert.StartsWith("powerplanmanager:plan", Assert.Single(_launcher.Links));
    }

    [Theory]
    [InlineData(typeof(PpmTopUpAction), "powerplanmanager:topup")]
    [InlineData(typeof(PpmPauseChargingAction), "powerplanmanager:pause")]
    [InlineData(typeof(PpmResumeChargingAction), "powerplanmanager:resume")]
    [InlineData(typeof(PpmCalibrateAction), "powerplanmanager:calibrate")]
    public async Task Battery_commands_send_their_link(Type action, string link)
    {
        _protocols.Registered.Add("powerplanmanager");
        await Run((ShortcutAction)Activator.CreateInstance(action)!);
        Assert.Equal(link, Assert.Single(_launcher.Links));
    }

    [Fact]
    public async Task Nothing_installed_gives_the_store_message()
    {
        var ex = await Fails(new PpmSwitchPlanAction(), ("name", "Balanced"));
        Assert.Equal("Power Plan Manager isn't installed. Get it from the Microsoft Store.", ex.Message);
        Assert.Empty(_launcher.Links);
    }

    [Fact]
    public async Task Unknown_pages_are_refused()
    {
        _protocols.Registered.Add("powerplanmanager");
        Assert.Contains("Nope", (await Fails(new PpmOpenAction(), ("page", ParamValue.Choice("Nope")))).Message);
    }

    [Fact]
    public void Status_parses_every_field()
    {
        var s = PpmPipe.ParseStatus(FullStatus);
        Assert.Equal(new PpmStatus("Balanced", "381b4222-f694-41f0-9685-ff5bb260df2e", "Travel", 80, "Paused", true, true, "Pro"), s);
    }

    [Fact]
    public void Lite_status_has_nulls()
    {
        var s = PpmPipe.ParseStatus("{\"plan\":{\"name\":\"Balanced\",\"guid\":\"x\"},\"preset\":null,\"chargeLimit\":null,\"chargingState\":\"Unknown\",\"keepAwake\":false,\"allowExternalLinks\":true,\"edition\":\"Lite\"}");
        Assert.Null(s.Preset);
        Assert.Null(s.ChargeLimit);
        Assert.Equal("Lite", s.Edition);
        Assert.Empty(PpmPipe.ParsePresets("{\"presets\":[]}"));
        Assert.Equal(new[] { "A", "B" }, PpmPipe.ParsePresets("{\"presets\":[\"A\",\"B\"]}"));
    }

    [Fact]
    public async Task Status_action_gives_a_dictionary()
    {
        _pipe.Reply = _ => FullStatus;
        var r = await Run(new GetPpmStatusAction());
        var d = Assert.IsType<DictionaryItem>(r.FirstItem);
        Assert.Contains(d.Entries, e => e.Key == "Charging State");
    }

    [Fact]
    public async Task Status_when_not_running_says_so()
    {
        Assert.Equal(PpmPipe.NotRunning, (await Fails(new GetPpmStatusAction())).Message);
        Assert.Equal(PpmPipe.NotRunning, (await Fails(new GetPpmPresetsAction())).Message);
    }

    [Fact]
    public async Task Status_timeout_is_a_plain_error()
    {
        _pipe.Reply = _ => throw new TimeoutException();
        Assert.Contains("didn't answer", (await Fails(new GetPpmStatusAction())).Message);
    }

    [Fact]
    public async Task Links_off_blocks_the_link()
    {
        _protocols.Registered.Add("powerplanmanager");
        _pipe.Reply = _ => "{\"allowExternalLinks\":false}";
        Assert.Equal(PpmPipe.LinksOff, (await Fails(new PpmSwitchPlanAction(), ("name", "Balanced"))).Message);
        Assert.Empty(_launcher.Links);
    }

    [Fact]
    public async Task Links_on_or_no_pipe_or_timeout_still_sends()
    {
        _protocols.Registered.Add("powerplanmanager");
        _pipe.Reply = _ => FullStatus;
        await Run(new PpmSwitchPlanAction(), ("name", "A"));
        _pipe.Reply = _ => null;
        await Run(new PpmSwitchPlanAction(), ("name", "B"));
        _pipe.Reply = _ => throw new TimeoutException();
        await Run(new PpmSwitchPlanAction(), ("name", "C"));
        Assert.Equal(3, _launcher.Links.Count);
    }

    [Fact]
    public async Task A_server_that_isnt_power_plan_manager_is_refused()
    {
        _protocols.Registered.Add("powerplanmanager");
        _pipe.Reply = _ => throw new ActionException("Something other than Power Plan Manager is answering");
        Assert.Contains("Something other", (await Fails(new PpmSwitchPlanAction(), ("name", "A"))).Message);
        Assert.Empty(_launcher.Links);
    }

    [Theory]
    [InlineData(@"C:\Users\x\evil.exe", "34458MindtheApp.PowerPlanManager_abc", true)]
    [InlineData(@"C:\Users\x\evil.exe", "34458MindtheApp.PowerPlanManagerLite_abc", true)]
    [InlineData(@"C:\Users\x\evil.exe", "34458MindtheApp.PowerPlanManagerEvil_abc", false)]
    [InlineData(@"C:\Users\x\evil.exe", "Other.App_abc", false)]
    [InlineData(@"C:\Program Files\WindowsApps\34458MindtheApp.PowerPlanManager_1.2.3.0_x64__abc\PowerPlanTray.exe", null, true)]
    [InlineData(@"C:\Program Files\WindowsApps\34458MindtheApp.PowerPlanManager_1.2.3.0_x64__abc\..\..\..\Users\x\PowerPlanManager.exe", null, false)]
    [InlineData(@"C:\Program Files\WindowsApps\Other.App_1.0.0.0_x64__abc\PowerPlanManager.exe", null, false)]
    // Any program with the name somewhere in its path used to be trusted.
    [InlineData(@"C:\Program Files\PowerPlanManager\PowerPlanManager.exe", null, false)]
    [InlineData(@"C:\Users\x\AppData\Local\Temp\PowerPlanManager\x.exe", null, false)]
    [InlineData(@"C:\x\PowerPlanTray.exe", null, false)]
    [InlineData(null, null, false)]
    public void Server_identity_check(string? path, string? family, bool trusted) =>
        Assert.Equal(trusted, PpmPipe.IsTrustedServer(path, family, @"C:\Program Files\WindowsApps"));
}