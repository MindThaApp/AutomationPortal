namespace AutomationPortal.Core.Actions;

public sealed class SetVariableAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "var.set", Title = Strings.Get("Action_VarSet_Title"), Category = ActionCategories.Variables, Icon = "BracesVariable24",
        Keywords = new[] { "store", "remember", "name", "assign" },
        Summary = Strings.Get("Action_VarSet_Summary"),
        Description = Strings.Get("Action_VarSet_Description"),
        OutputName = Strings.Get("Action_VarSet_OutputName"),
        Parameters = new[]
        {
            new ParameterDescriptor { Key = "name", Label = Strings.Get("Action_VarSet_Param_Name_Label"), Type = ParamType.VariableName, Required = true, AllowVariables = false, AllowAskEachTime = false, Placeholder = Strings.Get("Action_VarSet_Param_Name_Placeholder") },
            ParameterDescriptor.Content("value", Strings.Get("Action_VarSet_Param_Value_Label"), isInput: true),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var name = (await ctx.GetTextAsync("name")).Trim();
        if (name.Length == 0) throw new ActionException(Strings.Get("VariableAndUiActions_GiveTheVariableA"));
        var value = await ctx.GetContentAsync("value");
        ctx.SetVariable(name, value);
        return value;
    }
}

public sealed class AddToVariableAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "var.add", Title = Strings.Get("Action_VarAdd_Title"), Category = ActionCategories.Variables, Icon = "Add24",
        Keywords = new[] { "append", "collect", "list", "accumulate" },
        Summary = Strings.Get("Action_VarAdd_Summary"),
        Description = Strings.Get("Action_VarAdd_Description"),
        OutputName = Strings.Get("Action_VarAdd_OutputName"),
        Parameters = new[]
        {
            new ParameterDescriptor { Key = "name", Label = Strings.Get("Action_VarAdd_Param_Name_Label"), Type = ParamType.VariableName, Required = true, AllowVariables = false, AllowAskEachTime = false },
            ParameterDescriptor.Content("value", Strings.Get("Action_VarAdd_Param_Value_Label"), isInput: true),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var name = (await ctx.GetTextAsync("name")).Trim();
        if (name.Length == 0) throw new ActionException(Strings.Get("VariableAndUiActions_GiveTheVariableA"));
        var value = ContentValue.Concat(ctx.GetVariable(name), await ctx.GetContentAsync("value"));
        ctx.SetVariable(name, value);
        return value;
    }
}

public sealed class GetVariableAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "var.get", Title = Strings.Get("Action_VarGet_Title"), Category = ActionCategories.Variables, Icon = "BracesVariable24",
        Keywords = new[] { "recall", "read", "value" },
        Summary = Strings.Get("Action_VarGet_Summary"),
        Description = Strings.Get("Action_VarGet_Description"),
        OutputName = Strings.Get("Action_VarGet_OutputName"),
        Parameters = new[] { new ParameterDescriptor { Key = "name", Label = Strings.Get("Action_VarGet_Param_Name_Label"), Type = ParamType.VariableName, Required = true, AllowVariables = false, AllowAskEachTime = false } },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx) =>
        ctx.GetVariable((await ctx.GetTextAsync("name")).Trim());
}

public sealed class SaveValueAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "var.saveValue", Title = Strings.Get("Action_VarSaveValue_Title"), Category = ActionCategories.Variables, Icon = "Save24",
        Keywords = new[] { "remember", "persist", "counter", "store", "keep" },
        Summary = Strings.Get("Action_VarSaveValue_Summary"),
        Description = Strings.Get("Action_VarSaveValue_Description"),
        HasOutput = false,
        Parameters = new[]
        {
            new ParameterDescriptor { Key = "key", Label = Strings.Get("Action_VarSaveValue_Param_Key_Label"), Type = ParamType.Text, Required = true, Placeholder = Strings.Get("Action_VarSaveValue_Param_Key_Placeholder") },
            ParameterDescriptor.Content("value", Strings.Get("Action_VarSaveValue_Param_Value_Label"), isInput: true),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var key = (await ctx.GetTextAsync("key")).Trim();
        if (key.Length == 0) throw new ActionException(Strings.Get("VariableAndUiActions_GiveTheSavedValue"));
        ctx.Services.KeyValues.Set(key, await ctx.GetContentAsync("value"));
        return ctx.Input;
    }
}

public sealed class LoadValueAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "var.loadValue", Title = Strings.Get("Action_VarLoadValue_Title"), Category = ActionCategories.Variables, Icon = "ArrowDownload24",
        Keywords = new[] { "recall", "counter", "remembered", "load" },
        Summary = Strings.Get("Action_VarLoadValue_Summary"),
        Description = Strings.Get("Action_VarLoadValue_Description"),
        OutputName = Strings.Get("Action_VarLoadValue_OutputName"),
        Parameters = new[]
        {
            new ParameterDescriptor { Key = "key", Label = Strings.Get("Action_VarLoadValue_Param_Key_Label"), Type = ParamType.Text, Required = true },
            ParameterDescriptor.Content("fallback", Strings.Get("Action_VarLoadValue_Param_Fallback_Label")),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var key = (await ctx.GetTextAsync("key")).Trim();
        return ctx.Services.KeyValues.Get(key) ?? await ctx.GetContentAsync("fallback");
    }
}

public sealed class TextAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "content.text", Title = Strings.Get("Action_ContentText_Title"), Category = ActionCategories.Text, Icon = "TextT24",
        Keywords = new[] { "string", "words", "message", "type" },
        Summary = Strings.Get("Action_ContentText_Summary"),
        Description = Strings.Get("Action_ContentText_Description"),
        OutputKind = ContentKind.Text, OutputName = Strings.Get("Action_ContentText_OutputName"),
        Parameters = new[] { ParameterDescriptor.MultilineText("text", Strings.Get("Action_ContentText_Param_Text_Label")) },
    };
    public override ActionDescriptor Descriptor => D;
    public override async Task<ContentValue> RunAsync(ActionContext ctx) => ContentValue.Text(await ctx.GetTextAsync("text"));
}

public sealed class NumberAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "content.number", Title = Strings.Get("Action_ContentNumber_Title"), Category = ActionCategories.Numbers, Icon = "NumberSymbol24",
        Keywords = new[] { "digit", "value", "amount" },
        Summary = Strings.Get("Action_ContentNumber_Summary"),
        Description = Strings.Get("Action_ContentNumber_Description"),
        OutputKind = ContentKind.Number, OutputName = Strings.Get("Action_ContentNumber_OutputName"),
        Parameters = new[] { ParameterDescriptor.Number("value", Strings.Get("Action_ContentNumber_Param_Value_Label"), 0) },
    };
    public override ActionDescriptor Descriptor => D;
    public override async Task<ContentValue> RunAsync(ActionContext ctx) => ContentValue.Number(await ctx.GetNumberAsync("value"));
}

public sealed class ListAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "content.list", Title = Strings.Get("Action_ContentList_Title"), Category = ActionCategories.ListsAndDictionaries, Icon = "List24",
        Keywords = new[] { "items", "array", "collection" },
        Summary = Strings.Get("Action_ContentList_Summary"),
        Description = Strings.Get("Action_ContentList_Description"),
        OutputName = Strings.Get("Action_ContentList_OutputName"),
        Parameters = new[] { ParameterDescriptor.ListParam("items", Strings.Get("Action_ContentList_Param_Items_Label")) },
    };
    public override ActionDescriptor Descriptor => D;
    public override async Task<ContentValue> RunAsync(ActionContext ctx) =>
        ContentValue.FromItems(await ctx.GetListAsync("items"));
}

public sealed class DictionaryAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "content.dict", Title = Strings.Get("Action_ContentDict_Title"), Category = ActionCategories.ListsAndDictionaries, Icon = "Table24",
        Keywords = new[] { "keys", "values", "object", "json", "record" },
        Summary = Strings.Get("Action_ContentDict_Summary"),
        Description = Strings.Get("Action_ContentDict_Description"),
        OutputKind = ContentKind.Dictionary, OutputName = Strings.Get("Action_ContentDict_OutputName"),
        Parameters = new[] { new ParameterDescriptor { Key = "items", Label = Strings.Get("Action_ContentDict_Param_Items_Label"), Type = ParamType.Dictionary } },
    };
    public override ActionDescriptor Descriptor => D;
    public override async Task<ContentValue> RunAsync(ActionContext ctx) =>
        await ctx.GetContentAsync("items") is { IsEmpty: false } c ? c : ContentValue.From(DictionaryItem.Empty);
}

// ---- Show & Ask -----------------------------------------------------------------------------

public sealed class ShowResultAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "ui.showResult", Title = Strings.Get("Action_UiShowResult_Title"), Category = ActionCategories.ShowAndAsk, Icon = "Chat24",
        Keywords = new[] { "display", "output", "print", "show" },
        Summary = Strings.Get("Action_UiShowResult_Summary"),
        Description = Strings.Get("Action_UiShowResult_Description"),
        HasOutput = false,
        Parameters = new[] { ParameterDescriptor.Content("content", Strings.Get("Action_UiShowResult_Param_Content_Label"), isInput: true) },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var value = await ctx.GetContentAsync("content");
        await ctx.Services.UserInteraction.ShowResultAsync(ctx.ShortcutName, value, ctx.CancellationToken);
        return value;
    }
}

public sealed class ShowAlertAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "ui.alert", Title = Strings.Get("Action_UiAlert_Title"), Category = ActionCategories.ShowAndAsk, Icon = "Alert24",
        Keywords = new[] { "message", "popup", "dialog", "confirm", "warning" },
        Summary = Strings.Get("Action_UiAlert_Summary"),
        Description = Strings.Get("Action_UiAlert_Description"),
        HasOutput = false,
        Parameters = new[]
        {
            ParameterDescriptor.Text("title", Strings.Get("Action_UiAlert_Param_Title_Label"), "Automation Portal"),
            ParameterDescriptor.MultilineText("message", Strings.Get("Action_UiAlert_Param_Message_Label")) with { Required = true },
            ParameterDescriptor.Bool("showCancel", Strings.Get("Action_UiAlert_Param_ShowCancel_Label"), true),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var ok = await ctx.Services.UserInteraction.ShowAlertAsync(
            await ctx.GetTextAsync("title", "Automation Portal"), await ctx.GetTextAsync("message"),
            await ctx.GetBoolAsync("showCancel", true), ctx.CancellationToken);
        if (!ok) throw new UserCancelledException();
        return ctx.Input;
    }
}

public sealed class AskForInputAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        SensitiveOutput = true, Id = "ui.ask", Title = Strings.Get("Action_UiAsk_Title"), Category = ActionCategories.ShowAndAsk, Icon = "QuestionCircle24",
        Keywords = new[] { "prompt", "type", "enter", "question", "input" },
        Summary = Strings.Get("Action_UiAsk_Summary"),
        Description = Strings.Get("Action_UiAsk_Description"),
        OutputName = Strings.Get("Action_UiAsk_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("kind", Strings.Get("Action_UiAsk_Param_Kind_Label"), "text",
                new Choice("text", Strings.Get("Action_UiAsk_Param_Kind_Choice_Text")), new Choice("multiline", Strings.Get("Action_UiAsk_Param_Kind_Choice_Multiline")), new Choice("number", Strings.Get("Action_UiAsk_Param_Kind_Choice_Number")),
                new Choice("date", Strings.Get("Action_UiAsk_Param_Kind_Choice_Date")), new Choice("time", Strings.Get("Action_UiAsk_Param_Kind_Choice_Time")), new Choice("url", Strings.Get("Action_UiAsk_Param_Kind_Choice_Url"))) with { AllowAskEachTime = false },
            ParameterDescriptor.Text("prompt", Strings.Get("Action_UiAsk_Param_Prompt_Label"), "Enter a value"),
            ParameterDescriptor.Text("default", Strings.Get("Action_UiAsk_Param_Default_Label")),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var kind = (await ctx.GetChoiceAsync("kind", "text")) switch
        {
            "multiline" => InputKind.Multiline, "number" => InputKind.Number, "date" => InputKind.Date,
            "time" => InputKind.Time, "url" => InputKind.Url, _ => InputKind.Text,
        };
        var def = await ctx.GetTextAsync("default");
        return await ctx.Services.UserInteraction.AskForInputAsync(
            await ctx.GetTextAsync("prompt", "Enter a value"), kind, def.Length == 0 ? null : def, ctx.CancellationToken);
    }
}

public sealed class AskForSeveralAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        SensitiveOutput = true, Id = "ui.askMany", Title = Strings.Get("Action_UiAskMany_Title"), Category = ActionCategories.ShowAndAsk, Icon = "TextBulletList24",
        Keywords = new[] { "form", "fields", "questions", "prompt", "inputs" },
        Summary = Strings.Get("Action_UiAskMany_Summary"),
        Description = Strings.Get("Action_UiAskMany_Description"),
        OutputKind = ContentKind.Dictionary, OutputName = Strings.Get("Action_UiAskMany_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.Text("title", Strings.Get("Action_UiAskMany_Param_Title_Label"), Strings.Get("Action_Ui_FormDefaultTitle")),
            ParameterDescriptor.ListParam("fields", Strings.Get("Action_UiAskMany_Param_Fields_Label")) with
            {
                Required = true, AllowVariables = false, AllowAskEachTime = false,
                Hint = Strings.Get("Action_UiAskMany_Hint"),
            },
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var items = await ctx.GetListAsync("fields");
        var fields = new List<FormField>();
        foreach (var item in items)
        {
            if (item is not DictionaryItem d)
            {
                fields.Add(new FormField(item.DisplayText));
                continue;
            }
            var label = d.Get("label").AsText();
            if (label.Length == 0) continue;
            var kind = d.Get("kind").AsText().Trim().ToLowerInvariant() switch
            {
                "multiline" or "long text" => FormFieldKind.Multiline, "number" => FormFieldKind.Number, "date" => FormFieldKind.Date,
                "time" => FormFieldKind.Time, "choice" => FormFieldKind.Choice, _ => FormFieldKind.Text,
            };
            var optionsValue = d.Get("options");
            var options = optionsValue.Count == 1 && optionsValue[0] is TextItem t
                ? t.Value.Split(new[] { '\n', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : optionsValue.AsTexts().ToArray();
            var def = d.Get("default").AsText();
            fields.Add(new FormField(label, kind, def.Length == 0 ? null : def, options.Length == 0 ? null : options));
        }
        if (fields.Count == 0) throw new ActionException(Strings.Get("VariableAndUiActions_AddAtLeastOne"));
        return await ctx.Services.UserInteraction.AskForFormAsync(await ctx.GetTextAsync("title", Strings.Get("Action_Ui_FormDefaultTitle")), fields, ctx.CancellationToken);
    }
}

public sealed class ChooseFromListAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "ui.chooseList", Title = Strings.Get("Action_UiChooseList_Title"), Category = ActionCategories.ShowAndAsk, Icon = "CheckboxChecked24",
        Keywords = new[] { "pick", "select", "choose", "options" },
        Summary = Strings.Get("Action_UiChooseList_Summary"),
        Description = Strings.Get("Action_UiChooseList_Description"),
        OutputName = Strings.Get("Action_UiChooseList_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.Content("items", Strings.Get("Action_UiChooseList_Param_Items_Label"), isInput: true, required: true),
            ParameterDescriptor.Text("prompt", Strings.Get("Action_UiChooseList_Param_Prompt_Label"), Strings.Get("Action_Ui_ChooseDefaultPrompt")),
            ParameterDescriptor.Bool("multiple", Strings.Get("Action_UiChooseList_Param_Multiple_Label"), false),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var items = await ctx.GetListAsync("items");
        if (items.Count == 0) throw new ActionException(Strings.Get("VariableAndUiActions_ThereSNothingTo"));
        return await ctx.Services.UserInteraction.ChooseFromListAsync(
            await ctx.GetTextAsync("prompt", Strings.Get("Action_Ui_ChooseDefaultPrompt")), items, await ctx.GetBoolAsync("multiple"), ctx.CancellationToken);
    }
}

public sealed class ShowNotificationAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "ui.notify", Title = Strings.Get("Action_UiNotify_Title"), Category = ActionCategories.ShowAndAsk, Icon = "AlertOn24",
        Keywords = new[] { "toast", "alert", "banner", "message", "reply", "buttons" },
        Summary = Strings.Get("Action_UiNotify_Summary"),
        Description = Strings.Get("Action_UiNotify_Description"),
        OutputName = Strings.Get("Action_UiNotify_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.Text("title", Strings.Get("Action_UiNotify_Param_Title_Label"), "Automation Portal"),
            ParameterDescriptor.MultilineText("body", Strings.Get("Action_UiNotify_Param_Body_Label")) with { Required = true },
            ParameterDescriptor.ListParam("buttons", Strings.Get("Action_UiNotify_Param_Buttons_Label")) with { Hint = Strings.Get("Action_UiNotify_Hint") },
            ParameterDescriptor.Bool("reply", Strings.Get("Action_UiNotify_Param_Reply_Label"), false),
            ParameterDescriptor.Bool("wait", Strings.Get("Action_UiNotify_Param_Wait_Label"), true),
            ParameterDescriptor.Number("timeout", Strings.Get("Action_UiNotify_Param_Timeout_Label"), 60, min: 5, unit: "seconds"),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var title = await ctx.GetTextAsync("title", "Automation Portal");
        var body = await ctx.GetTextAsync("body");
        var buttons = (await ctx.GetListAsync("buttons")).Select(b => b.DisplayText).Where(b => b.Length > 0).ToList();
        var reply = await ctx.GetBoolAsync("reply");
        var wait = await ctx.GetBoolAsync("wait", true);
        if ((buttons.Count == 0 && !reply) || !wait)
        {
            ctx.Services.Notifications.Show(title, body);
            return ctx.Input;
        }
        var timeout = TimeSpan.FromSeconds(await ctx.GetNumberAsync("timeout", 60));
        var r = await ctx.Services.Notifications.ShowInteractiveAsync(title, body, buttons, reply, null, timeout, ctx.CancellationToken);
        if (r.Dismissed) return ContentValue.Empty;
        return ContentValue.Text(r.ButtonLabel ?? r.ReplyText ?? "");
    }
}

public sealed class PreviewAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "ui.preview", Title = Strings.Get("Action_UiPreview_Title"), Category = ActionCategories.ShowAndAsk, Icon = "Eye24",
        Keywords = new[] { "quick look", "view", "open", "see" },
        Summary = Strings.Get("Action_UiPreview_Summary"),
        Description = Strings.Get("Action_UiPreview_Description"),
        HasOutput = false,
        Parameters = new[] { ParameterDescriptor.Content("content", Strings.Get("Action_UiPreview_Param_Content_Label"), isInput: true) },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var value = await ctx.GetContentAsync("content");
        await ctx.Services.UserInteraction.PreviewAsync(value, ctx.CancellationToken);
        return value;
    }
}
