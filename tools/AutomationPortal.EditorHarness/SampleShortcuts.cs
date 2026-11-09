using AutomationPortal.Core;

namespace AutomationPortal.EditorHarness;

internal static class SampleShortcuts
{
    /// <summary>A shortcut that uses every kind of structure: variables, If / Otherwise, menu, repeat, an unknown action and a failing step.</summary>
    public static Shortcut Showcase()
    {
        var s = new Shortcut { Name = "Editor Showcase", Icon = "Sparkle24", Color = "indigo", Description = "Try everything the editor can do." };
        s.Actions.Add(ActionInstance.Create("content.text", ("text", "Hello from Automation Portal")).WithUid("t1"));
        s.Actions[^1].OutputName = "Greeting";
        s.Actions.Add(ActionInstance.Create("sample.files", ("count", 3)).WithUid("f1"));

        s.Actions.Add(new ActionInstance { ActionId = FlowIds.RepeatEach, Uid = "r1" }.WithFlow("g1", FlowRole.Start).WithParam("items", ParamValue.Output("f1")));
        s.Actions.Add(ActionInstance.Create("content.text", ("text", ParamValue.Text("File: ", VarRef.RepeatItem.WithProperty("Name"), " (item ", VarRef.RepeatIndex, ")"))).WithUid("t2"));
        s.Actions.Add(new ActionInstance { ActionId = FlowIds.RepeatEach, Uid = "r1e" }.WithFlow("g1", FlowRole.End));

        s.Actions.Add(new ActionInstance { ActionId = FlowIds.If, Uid = "i1" }.WithFlow("g2", FlowRole.Start)
            .WithParam("conditions", ParamValue.AllOf(ConditionRow.Of(ParamValue.Output("t1"), ConditionOperators.Contains, "Hello"))));
        s.Actions.Add(ActionInstance.Create("content.text", ("text", "It says hello!")).WithUid("t3"));
        s.Actions.Add(new ActionInstance { ActionId = FlowIds.If, Uid = "i1m" }.WithFlow("g2", FlowRole.Middle));
        s.Actions.Add(ActionInstance.Create("content.text", ("text", "No greeting.")).WithUid("t4"));
        s.Actions.Add(new ActionInstance { ActionId = FlowIds.If, Uid = "i1e" }.WithFlow("g2", FlowRole.End));

        s.Actions.Add(new ActionInstance { ActionId = FlowIds.Menu, Uid = "m1" }.WithFlow("g3", FlowRole.Start)
            .WithParam("prompt", "What next?").WithParam("items", ParamValue.List("Look at a picture", "Count to three")));
        s.Actions.Add(new ActionInstance { ActionId = FlowIds.Menu, Uid = "m1a" }.WithFlow("g3", FlowRole.Middle, "Look at a picture"));
        s.Actions.Add(ActionInstance.Create("sample.image", ("size", 120)).WithUid("img"));
        s.Actions.Add(new ActionInstance { ActionId = FlowIds.Menu, Uid = "m1b" }.WithFlow("g3", FlowRole.Middle, "Count to three"));
        s.Actions.Add(ActionInstance.Create("content.number", ("value", 3)).WithUid("n1"));
        s.Actions.Add(new ActionInstance { ActionId = FlowIds.Menu, Uid = "m1e" }.WithFlow("g3", FlowRole.End));

        s.Actions.Add(ActionInstance.Create("var.set", ("name", "Total"), ("value", 3)).WithUid("v1"));
        s.Actions.Add(ActionInstance.Create("sample.person").WithUid("p1"));
        s.Actions.Add(ActionInstance.Create("sample.slow", ("seconds", ParamValue.Duration(2))).WithUid("slow"));
        s.Actions.Add(ActionInstance.Create("ui.showResult", ("content", ParamValue.Text("Total is ", VarRef.Named("Total"), " for ", VarRef.Output("p1").WithProperty("key:name")))).WithUid("show"));
        s.Actions.Add(ActionInstance.Create("future.magic", ("wand", "kept"), ("spell", ParamValue.Number(7))).WithUid("unk"));
        s.Actions.Add(ActionInstance.Create("sample.fail").WithUid("fail").OnErrorContinue());
        s.Actions.Add(ActionInstance.Create("sample.danger").WithUid("danger"));
        s.Actions.Add(ActionInstance.Create("sample.everything").WithUid("every"));
        s.Actions.Add(ActionInstance.Create("ui.ask", ("prompt", ParamValue.Ask())).WithUid("ask"));
        // switched off, and pointing at a step that comes later, to show the validation banner without breaking the run
        var every = s.Actions.First(a => a.Uid == "every");
        every.Disabled = true;
        every.Parameters["text"] = ParamValue.Text("Waiting for ", VarRef.Output("ask"));
        s.Actions.First(a => a.Uid == "unk").Disabled = true;
        return s;
    }

    public static Shortcut Small()
    {
        var s = new Shortcut { Name = "Say Hello", Icon = "HandWave24", Color = "orange" };
        s.Actions.Add(ActionInstance.Create("content.text", ("text", "Hello!")).WithUid("a"));
        s.Actions.Add(ActionInstance.Create("ui.showResult", ("content", ParamValue.Output("a"))).WithUid("b"));
        return s;
    }

    /// <summary>300 actions with a few nested blocks, to check the list stays smooth.</summary>
    public static Shortcut Stress()
    {
        var s = new Shortcut { Name = "Stress Test (300 actions)", Icon = "Rocket24", Color = "red" };
        for (var i = 0; i < 300; i++)
        {
            if (i % 50 == 10)
            {
                var gid = "g" + i;
                s.Actions.Add(new ActionInstance { ActionId = FlowIds.Repeat }.WithFlow(gid, FlowRole.Start).WithParam("count", 3));
                s.Actions.Add(ActionInstance.Create("content.text", ("text", ParamValue.Text("Inside repeat ", VarRef.RepeatIndex))));
                s.Actions.Add(new ActionInstance { ActionId = FlowIds.Repeat }.WithFlow(gid, FlowRole.End));
                continue;
            }
            s.Actions.Add(i % 3 == 0
                ? ActionInstance.Create("content.number", ("value", i))
                : ActionInstance.Create("content.text", ("text", $"Step number {i}")));
        }
        return s;
    }
}
