using AutomationPortal.Core;
using AutomationPortal.Core.Testing;
using AutomationPortal.Triggers;

namespace AutomationPortal.Triggers.Tests;

public sealed class FakeTrigger : TriggerProvider
{
    private readonly TriggerDescriptor _descriptor;
    public List<ITriggerSink> Sinks { get; } = new();
    public int Started { get; private set; }
    public int Stopped { get; private set; }
    public Exception? ThrowOnStart { get; set; }

    public FakeTrigger(string id = "test.fake", ContentKind output = ContentKind.Text)
    {
        _descriptor = new TriggerDescriptor { Id = id, Title = "Fake", Category = "Test", OutputKind = output };
    }

    public override TriggerDescriptor Descriptor => _descriptor;

    public override IDisposable Start(TriggerConfig config, ITriggerSink sink)
    {
        if (ThrowOnStart is not null) throw ThrowOnStart;
        Started++;
        Sinks.Add(sink);
        return new DisposableAction(() => Stopped++);
    }
}

public sealed class OneShotFakeTrigger : TriggerProvider, IOneShotTrigger
{
    public override TriggerDescriptor Descriptor { get; } = new() { Id = "test.once", Title = "Once", Category = "Test" };
    public override IDisposable Start(TriggerConfig config, ITriggerSink sink) => new DisposableAction(() => { });
}

public sealed class FakeCondition : ConditionProvider
{
    private readonly ConditionDescriptor _descriptor;
    public bool Result { get; set; } = true;
    public bool Throw { get; set; }
    public FakeCondition(string id = "test.cond") { _descriptor = new ConditionDescriptor { Id = id, Title = "Fake condition" }; }
    public override ConditionDescriptor Descriptor => _descriptor;
    public override Task<bool> IsMetAsync(TriggerConfig config, CancellationToken ct) =>
        Throw ? throw new InvalidOperationException("boom") : Task.FromResult(Result);
}

/// <summary>An engine wired to the Core test host, with a fake trigger and a shortcut that echoes its input.</summary>
internal sealed class EngineFixture : IDisposable
{
    public TestHost Host { get; } = new();
    public FakeTrigger Trigger { get; } = new();
    public FakeCondition Condition { get; } = new();
    public TriggerRegistry Triggers { get; } = new();
    public ConditionRegistry Conditions { get; } = new();
    public AutomationEngine Engine { get; }
    public DateTimeOffset Now { get; set; } = new(2026, 10, 2, 9, 0, 0, TimeSpan.Zero);
    public Shortcut Shortcut { get; }

    public EngineFixture()
    {
        Triggers.Register(Trigger);
        Triggers.Register(new OneShotFakeTrigger());
        Conditions.Register(Condition);
        Engine = new AutomationEngine(Host.Services, Triggers, Conditions, clock: () => Now);
        Shortcut = new Shortcut { Name = "Echo" };
        Shortcut.Actions.Add(ActionInstance.Create("content.text", ("text", ParamValue.ShortcutInput())));
        Host.Library.Save(Shortcut);
    }

    public Automation Add(Action<Automation>? configure = null, string triggerId = "test.fake")
    {
        var a = new Automation { ShortcutId = Shortcut.Id, Trigger = new TriggerInstance { TriggerId = triggerId } };
        configure?.Invoke(a);
        Host.Library.SaveAutomation(a);
        return a;
    }

    public Automation Latest(Automation a) => Host.Library.Automations.First(x => x.Id == a.Id);

    public async Task<AutomationOutcome> FireAsync(Automation a, string text = "hello", string? description = "something happened") =>
        await Engine.HandleFireAsync(a.Id, ContentValue.Text(text), description);

    public void Dispose()
    {
        Engine.Dispose();
        Host.Dispose();
    }
}
