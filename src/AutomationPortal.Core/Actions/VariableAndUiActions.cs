namespace AutomationPortal.Core.Actions;

public sealed class SetVariableAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "var.set", Title = "Set Variable", Category = ActionCategories.Variables, Icon = "BracesVariable24",
        Keywords = new[] { "store", "remember", "name", "assign" },
        Summary = "Set variable {name} to {value}",
        Description = "Stores a value under a name so you can use it later in the shortcut.",
        OutputName = "Variable",
        Parameters = new[]
        {
            new ParameterDescriptor { Key = "name", Label = "Variable name", Type = ParamType.VariableName, Required = true, AllowVariables = false, AllowAskEachTime = false, Placeholder = "Variable name" },
            ParameterDescriptor.Content("value", "Value", isInput: true),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var name = (await ctx.GetTextAsync("name")).Trim();
        if (name.Length == 0) throw new ActionException("Give the variable a name.");
        var value = await ctx.GetContentAsync("value");
        ctx.SetVariable(name, value);
        return value;
    }
}

public sealed class AddToVariableAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "var.add", Title = "Add to Variable", Category = ActionCategories.Variables, Icon = "Add24",
        Keywords = new[] { "append", "collect", "list", "accumulate" },
        Summary = "Add {value} to variable {name}",
        Description = "Adds a value to the end of a variable. If the variable doesn't exist yet it is created.",
        OutputName = "Variable",
        Parameters = new[]
        {
            new ParameterDescriptor { Key = "name", Label = "Variable name", Type = ParamType.VariableName, Required = true, AllowVariables = false, AllowAskEachTime = false },
            ParameterDescriptor.Content("value", "Value", isInput: true),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var name = (await ctx.GetTextAsync("name")).Trim();
        if (name.Length == 0) throw new ActionException("Give the variable a name.");
        var value = ContentValue.Concat(ctx.GetVariable(name), await ctx.GetContentAsync("value"));
        ctx.SetVariable(name, value);
        return value;
    }
}

public sealed class GetVariableAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "var.get", Title = "Get Variable", Category = ActionCategories.Variables, Icon = "BracesVariable24",
        Keywords = new[] { "recall", "read", "value" },
        Summary = "Get variable {name}",
        Description = "Gets the value stored in a variable.",
        OutputName = "Variable Value",
        Parameters = new[] { new ParameterDescriptor { Key = "name", Label = "Variable name", Type = ParamType.VariableName, Required = true, AllowVariables = false, AllowAskEachTime = false } },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx) =>
        ctx.GetVariable((await ctx.GetTextAsync("name")).Trim());
}

public sealed class SaveValueAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "var.saveValue", Title = "Save Value", Category = ActionCategories.Variables, Icon = "Save24",
        Keywords = new[] { "remember", "persist", "counter", "store", "keep" },
        Summary = "Save {value} as {key}",
        Description = "Remembers a value even after the shortcut finishes, so the next run can read it. Good for counters and \"last time\" dates.",
        HasOutput = false,
        Parameters = new[]
        {
            new ParameterDescriptor { Key = "key", Label = "Name", Type = ParamType.Text, Required = true, Placeholder = "e.g. Last backup" },
            ParameterDescriptor.Content("value", "Value", isInput: true),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var key = (await ctx.GetTextAsync("key")).Trim();
        if (key.Length == 0) throw new ActionException("Give the saved value a name.");
        ctx.Services.KeyValues.Set(key, await ctx.GetContentAsync("value"));
        return ctx.Input;
    }
}

public sealed class LoadValueAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "var.loadValue", Title = "Get Saved Value", Category = ActionCategories.Variables, Icon = "ArrowDownload24",
        Keywords = new[] { "recall", "counter", "remembered", "load" },
        Summary = "Get saved value {key}",
        Description = "Gets a value saved earlier with Save Value. Gives the fallback (or nothing) if it was never saved.",
        OutputName = "Saved Value",
        Parameters = new[]
        {
            new ParameterDescriptor { Key = "key", Label = "Name", Type = ParamType.Text, Required = true },
            ParameterDescriptor.Content("fallback", "If not saved yet"),
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
        Id = "content.text", Title = "Text", Category = ActionCategories.Text, Icon = "TextT24",
        Keywords = new[] { "string", "words", "message", "type" },
        Summary = "{text}",
        Description = "A piece of text. Type variables into it to build a message.",
        OutputKind = ContentKind.Text, OutputName = "Text",
        Parameters = new[] { ParameterDescriptor.MultilineText("text", "Text") },
    };
    public override ActionDescriptor Descriptor => D;
    public override async Task<ContentValue> RunAsync(ActionContext ctx) => ContentValue.Text(await ctx.GetTextAsync("text"));
}

public sealed class NumberAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "content.number", Title = "Number", Category = ActionCategories.Numbers, Icon = "NumberSymbol24",
        Keywords = new[] { "digit", "value", "amount" },
        Summary = "{value}",
        Description = "A number.",
        OutputKind = ContentKind.Number, OutputName = "Number",
        Parameters = new[] { ParameterDescriptor.Number("value", "Number", 0) },
    };
    public override ActionDescriptor Descriptor => D;
    public override async Task<ContentValue> RunAsync(ActionContext ctx) => ContentValue.Number(await ctx.GetNumberAsync("value"));
}

public sealed class ListAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "content.list", Title = "List", Category = ActionCategories.ListsAndDictionaries, Icon = "List24",
        Keywords = new[] { "items", "array", "collection" },
        Summary = "List of {items}",
        Description = "A list of items. Each item can be text, a variable or anything else.",
        OutputName = "List",
        Parameters = new[] { ParameterDescriptor.ListParam("items", "Items") },
    };
    public override ActionDescriptor Descriptor => D;
    public override async Task<ContentValue> RunAsync(ActionContext ctx) =>
        ContentValue.FromItems(await ctx.GetListAsync("items"));
}

public sealed class DictionaryAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "content.dict", Title = "Dictionary", Category = ActionCategories.ListsAndDictionaries, Icon = "Table24",
        Keywords = new[] { "keys", "values", "object", "json", "record" },
        Summary = "Dictionary with {items}",
        Description = "A set of named values, like name → \"Sam\" and age → 30.",
        OutputKind = ContentKind.Dictionary, OutputName = "Dictionary",
        Parameters = new[] { new ParameterDescriptor { Key = "items", Label = "Items", Type = ParamType.Dictionary } },
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
        Id = "ui.showResult", Title = "Show Result", Category = ActionCategories.ShowAndAsk, Icon = "Chat24",
        Keywords = new[] { "display", "output", "print", "show" },
        Summary = "Show {content}",
        Description = "Shows something in a window, so you can read or copy it.",
        HasOutput = false,
        Parameters = new[] { ParameterDescriptor.Content("content", "Result", isInput: true) },
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
        Id = "ui.alert", Title = "Show Alert", Category = ActionCategories.ShowAndAsk, Icon = "Alert24",
        Keywords = new[] { "message", "popup", "dialog", "confirm", "warning" },
        Summary = "Show alert {message}",
        Description = "Shows a message box. With Cancel button on, choosing Cancel stops the shortcut.",
        HasOutput = false,
        Parameters = new[]
        {
            ParameterDescriptor.Text("title", "Title", "Automation Portal"),
            ParameterDescriptor.MultilineText("message", "Message") with { Required = true },
            ParameterDescriptor.Bool("showCancel", "Show Cancel button", true),
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
        SensitiveOutput = true, Id = "ui.ask", Title = "Ask for Input", Category = ActionCategories.ShowAndAsk, Icon = "QuestionCircle24",
        Keywords = new[] { "prompt", "type", "enter", "question", "input" },
        Summary = "Ask for {kind} with {prompt}",
        Description = "Asks you to type something when the shortcut runs.",
        OutputName = "Provided Input",
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("kind", "Input type", "text",
                new Choice("text", "Text"), new Choice("multiline", "Long text"), new Choice("number", "Number"),
                new Choice("date", "Date"), new Choice("time", "Time"), new Choice("url", "Link")) with { AllowAskEachTime = false },
            ParameterDescriptor.Text("prompt", "Question", "Enter a value"),
            ParameterDescriptor.Text("default", "Starting value"),
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
        SensitiveOutput = true, Id = "ui.askMany", Title = "Ask for Several Things", Category = ActionCategories.ShowAndAsk, Icon = "TextBulletList24",
        Keywords = new[] { "form", "fields", "questions", "prompt", "inputs" },
        Summary = "Ask {fields}",
        Description = "Shows one form with several questions and gives back all the answers.",
        OutputKind = ContentKind.Dictionary, OutputName = "Form Answers",
        Parameters = new[]
        {
            ParameterDescriptor.Text("title", "Form title", "Please fill in"),
            ParameterDescriptor.ListParam("fields", "Questions") with
            {
                Required = true, AllowVariables = false, AllowAskEachTime = false,
                Hint = "List of dictionaries with keys: label, kind (text, multiline, number, date, time, choice), default, options (list of choices).",
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
        if (fields.Count == 0) throw new ActionException("Add at least one question to ask.");
        return await ctx.Services.UserInteraction.AskForFormAsync(await ctx.GetTextAsync("title", "Please fill in"), fields, ctx.CancellationToken);
    }
}

public sealed class ChooseFromListAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "ui.chooseList", Title = "Choose from List", Category = ActionCategories.ShowAndAsk, Icon = "CheckboxChecked24",
        Keywords = new[] { "pick", "select", "choose", "options" },
        Summary = "Choose from {items}",
        Description = "Shows a list and lets you pick one or more items.",
        OutputName = "Chosen Items",
        Parameters = new[]
        {
            ParameterDescriptor.Content("items", "List", isInput: true, required: true),
            ParameterDescriptor.Text("prompt", "Prompt", "Choose an item"),
            ParameterDescriptor.Bool("multiple", "Select multiple", false),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var items = await ctx.GetListAsync("items");
        if (items.Count == 0) throw new ActionException("There's nothing to choose from. The list is empty.");
        return await ctx.Services.UserInteraction.ChooseFromListAsync(
            await ctx.GetTextAsync("prompt", "Choose an item"), items, await ctx.GetBoolAsync("multiple"), ctx.CancellationToken);
    }
}

public sealed class ShowNotificationAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "ui.notify", Title = "Show Notification", Category = ActionCategories.ShowAndAsk, Icon = "AlertOn24",
        Keywords = new[] { "toast", "alert", "banner", "message", "reply", "buttons" },
        Summary = "Show notification {body}",
        Description = "Pops up a Windows notification. It can have buttons or a reply box, and the shortcut can wait for your answer.",
        OutputName = "Notification Response",
        Parameters = new[]
        {
            ParameterDescriptor.Text("title", "Title", "Automation Portal"),
            ParameterDescriptor.MultilineText("body", "Message") with { Required = true },
            ParameterDescriptor.ListParam("buttons", "Buttons") with { Hint = "Button labels. Leave empty for none." },
            ParameterDescriptor.Bool("reply", "Let me type a reply", false),
            ParameterDescriptor.Bool("wait", "Wait for a response", true),
            ParameterDescriptor.Number("timeout", "Give up after", 60, min: 5, unit: "seconds"),
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
        Id = "ui.preview", Title = "Preview", Category = ActionCategories.ShowAndAsk, Icon = "Eye24",
        Keywords = new[] { "quick look", "view", "open", "see" },
        Summary = "Preview {content}",
        Description = "Shows a quick preview of files, images or text.",
        HasOutput = false,
        Parameters = new[] { ParameterDescriptor.Content("content", "Content", isInput: true) },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var value = await ctx.GetContentAsync("content");
        await ctx.Services.UserInteraction.PreviewAsync(value, ctx.CancellationToken);
        return value;
    }
}
