using Flowmate.Core;
using Flowmate.Core.Platform;
using Flowmate.Triggers;

namespace Flowmate.Triggers.Tests;

[Collection("PowerPlanStatics")]
public class PowerPlanTriggerTests : IDisposable
{
    private static readonly PowerPlan Balanced = new(Guid.NewGuid(), "Balanced");
    private static readonly PowerPlan Saver = new(Guid.NewGuid(), "Power saver");

    private sealed class FakeApi : IPowerPlanApi
    {
        public PowerPlan Active = Balanced;
        public Action<Guid>? Callback;
        public bool Disposed;
        public IReadOnlyList<PowerPlan> List() => new[] { Balanced, Saver };
        public PowerPlan? GetActive() => Active;
        public void SetActive(Guid id) => throw new NotSupportedException();
        public IDisposable Subscribe(Action<Guid> cb)
        {
            Callback = cb;
            cb(Active.Id); // Windows notifies right after registering
            return new Handle(this);
        }
        private sealed class Handle : IDisposable
        {
            private readonly FakeApi _api;
            public Handle(FakeApi api) { _api = api; }
            public void Dispose() => _api.Disposed = true;
        }
    }

    private sealed class Sink : ITriggerSink
    {
        public List<string> Fired { get; } = new();
        public void Fire(ContentValue output, string? description = null) => Fired.Add(output.ToString()!);
    }

    private readonly FakeApi _api = new();

    public PowerPlanTriggerTests() { PowerPlanApis.Current = _api; }
    public void Dispose() { PowerPlanApis.Current = new WindowsPowerPlanApi(); }

    [Fact]
    public void The_notification_on_registration_does_not_fire()
    {
        var sink = new Sink();
        using var h = new PowerPlanChangedTrigger().Start(TriggerConfig.Of(), sink);
        Assert.Empty(sink.Fired);
    }

    [Fact]
    public void A_change_fires_once_even_when_the_notification_repeats()
    {
        var sink = new Sink();
        using var h = new PowerPlanChangedTrigger().Start(TriggerConfig.Of(), sink);
        _api.Callback!(Saver.Id);
        _api.Callback!(Saver.Id);
        Assert.Equal(new[] { "Power saver" }, sink.Fired);
        _api.Callback!(Balanced.Id);
        Assert.Equal(2, sink.Fired.Count);
    }

    [Fact]
    public void The_plan_filter_only_lets_that_plan_through()
    {
        var sink = new Sink();
        using var h = new PowerPlanChangedTrigger().Start(TriggerConfig.Of(("plan", ParamValue.Choice("power SAVER"))), sink);
        _api.Callback!(Saver.Id);
        _api.Callback!(Balanced.Id);
        Assert.Equal(new[] { "Power saver" }, sink.Fired);
    }

    [Fact]
    public void Disposing_unsubscribes()
    {
        using (new PowerPlanChangedTrigger().Start(TriggerConfig.Of(), new Sink())) { }
        Assert.True(_api.Disposed);
    }

    [Fact]
    public void Filter_ignores_the_current_plan_and_repeats()
    {
        var f = new PowerPlanChangeFilter(Balanced.Id);
        Assert.False(f.Accept(Balanced.Id));
        Assert.True(f.Accept(Saver.Id));
        Assert.False(f.Accept(Saver.Id));
        Assert.True(f.Accept(Balanced.Id));
    }

    [Fact]
    public async Task The_condition_matches_the_active_plan_only()
    {
        var c = new PowerPlanCondition();
        Assert.True(await c.IsMetAsync(TriggerConfig.Of(("plan", ParamValue.Choice("balanced"))), CancellationToken.None));
        Assert.False(await c.IsMetAsync(TriggerConfig.Of(("plan", ParamValue.Choice("Power saver"))), CancellationToken.None));
        Assert.False(await c.IsMetAsync(TriggerConfig.Of(), CancellationToken.None));
    }
}
