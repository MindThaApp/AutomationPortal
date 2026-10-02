using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using Flowmate.Core;
using Flowmate.Editor.Logic;

namespace Flowmate.Editor.UI;

/// <summary>
/// Text box with inline variable pills. Typing "{", right-click or <see cref="OpenPicker"/> opens the variable picker; clicking a pill
/// edits it. The content converts to a <see cref="ParamValue"/> (a lone pill becomes a whole-value variable).
/// </summary>
internal sealed class TokenTextBox : RichTextBox
{
    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(TokenTextBox), new PropertyMetadata(""));

    private static readonly DependencyPropertyKey IsEmptyPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsEmpty), typeof(bool), typeof(TokenTextBox), new PropertyMetadata(true));
    public static readonly DependencyProperty IsEmptyProperty = IsEmptyPropertyKey.DependencyProperty;

    private readonly EditorEnv _env;
    private readonly Func<IReadOnlyList<VariableOption>> _variables;
    private readonly bool _multiline;
    private bool _suppress;

    public TokenTextBox(EditorEnv env, Func<IReadOnlyList<VariableOption>> variables, bool multiline, bool allowVariables)
    {
        EditorResources.Ensure();
        _env = env;
        _variables = variables;
        _multiline = multiline;
        AllowVariables = allowVariables && env.WithVariables;
        if (TryFindResource("FmTokenBoxStyle") is Style style) Style = style;
        FontFamily = Ui.Font;
        FontSize = 14;
        AcceptsReturn = multiline;
        AcceptsTab = false;
        IsDocumentEnabled = true;
        SpellCheck.IsEnabled = false;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        if (multiline) { MinHeight = 76; MaxHeight = 220; }
        Document = new FlowDocument { PagePadding = new Thickness(0), LineHeight = 20 };
        Document.Blocks.Add(new Paragraph { Margin = new Thickness(0) });
        DataObject.AddPastingHandler(this, OnPaste);
        TextChanged += (_, _) =>
        {
            UpdateIsEmpty();
            if (!_suppress) RaiseEdited();
        };
        PreviewTextInput += OnPreviewTextInput;
        PreviewMouseRightButtonDown += OnRightClick;
    }

    public bool AllowVariables { get; }
    public bool IsEmpty => (bool)GetValue(IsEmptyProperty);
    public string Placeholder { get => (string)GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }

    /// <summary>Raised when the user changes the content (typing, picking or editing a pill).</summary>
    public event Action<ParamValue>? ValueEdited;

    public ParamValue Value => TokenText.FromParts(ReadParts());

    public void SetValue(ParamValue? value)
    {
        _suppress = true;
        try
        {
            Document.Blocks.Clear();
            var para = new Paragraph { Margin = new Thickness(0) };
            Document.Blocks.Add(para);
            foreach (var part in TokenText.PartsOf(value))
            {
                if (part.Var is { } v) { para.Inlines.Add(MakeContainer(v)); continue; }
                var text = (part.Literal ?? "").Replace("\r\n", "\n");
                if (!_multiline) text = text.Replace('\n', ' ');
                var lines = text.Split('\n');
                for (var i = 0; i < lines.Length; i++)
                {
                    if (i > 0) { para = new Paragraph { Margin = new Thickness(0) }; Document.Blocks.Add(para); }
                    if (lines[i].Length > 0) para.Inlines.Add(new Run(lines[i]));
                }
            }
            UpdateIsEmpty();
        }
        finally { _suppress = false; }
    }

    public IReadOnlyList<TextPart> ReadParts()
    {
        var parts = new List<TextPart>();
        var first = true;
        foreach (var block in Document.Blocks)
        {
            if (block is not Paragraph p) continue;
            if (!first) parts.Add(TextPart.Of("\n"));
            first = false;
            Walk(p.Inlines, parts);
        }
        return TokenText.Normalize(parts);
    }

    private static void Walk(InlineCollection inlines, List<TextPart> parts)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run r: parts.Add(TextPart.Of(r.Text)); break;
                case LineBreak: parts.Add(TextPart.Of("\n")); break;
                case InlineUIContainer { Child: FrameworkElement { Tag: VarRef v } }: parts.Add(TextPart.Of(v)); break;
                case Span s: Walk(s.Inlines, parts); break;
            }
        }
    }

    private void UpdateIsEmpty() => SetValue(IsEmptyPropertyKey, ReadParts().Count == 0);

    private void RaiseEdited() => ValueEdited?.Invoke(Value);

    // ---- variables ---------------------------------------------------------------------------

    private Border MakePill(VarRef reference)
    {
        var pill = Pills.Create(_env.Describe(reference));
        pill.Tag = reference;
        pill.MouseLeftButtonUp += OnPillClick;
        return pill;
    }

    private InlineUIContainer MakeContainer(VarRef reference) =>
        new(MakePill(reference)) { BaselineAlignment = BaselineAlignment.Center };

    /// <summary>Inserts a pill at the caret (replacing any selection).</summary>
    public void InsertVariable(VarRef reference, TextPointer? at = null)
    {
        if (at is null && !Selection.IsEmpty) Selection.Text = "";
        var pos = (at ?? CaretPosition).GetInsertionPosition(LogicalDirection.Forward);
        var container = new InlineUIContainer(MakePill(reference), pos) { BaselineAlignment = BaselineAlignment.Center };
        CaretPosition = container.ElementEnd;
        Focus();
    }

    public void OpenPicker() => ShowPicker(Selection.IsEmpty ? CaretPosition : Selection.Start, braceOnCancel: false);

    private void ShowPicker(TextPointer at, bool braceOnCancel)
    {
        if (!AllowVariables) return;
        VariablePicker.Show(this, _variables(), allowAsk: false, result =>
        {
            if (result?.Option is { } option) InsertVariable(option.Ref, at);
            else
            {
                if (braceOnCancel) { CaretPosition = at; CaretPosition.InsertTextInRun("{"); CaretPosition = CaretPosition.GetPositionAtOffset(1) ?? CaretPosition; }
                Focus();
            }
        });
    }

    private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (!AllowVariables || e.Text != "{") return;
        e.Handled = true;
        if (!Selection.IsEmpty) Selection.Text = "";
        ShowPicker(CaretPosition, braceOnCancel: true);
    }

    private void OnRightClick(object sender, MouseButtonEventArgs e)
    {
        if (!AllowVariables) return;
        e.Handled = true;
        Focus();
        var pos = GetPositionFromPoint(e.GetPosition(this), true);
        if (pos is not null) CaretPosition = pos;
        ShowPicker(CaretPosition, braceOnCancel: false);
    }

    private void OnPillClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is not Border pill || pill.Tag is not VarRef current) return;
        PillOptions.Show(pill, _env, current, _variables, updated =>
        {
            pill.Tag = updated;
            Pills.Update(pill, _env.Describe(updated));
            RaiseEdited();
        }, () =>
        {
            if (LogicalTreeHelper.GetParent(pill) is InlineUIContainer c)
            {
                if (c.Parent is Paragraph para) para.Inlines.Remove(c);
                else if (c.Parent is Span span) span.Inlines.Remove(c);
            }
            Focus();
        });
    }

    private void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        e.CancelCommand();
        string? text = null;
        if (e.DataObject.GetDataPresent(DataFormats.UnicodeText)) text = e.DataObject.GetData(DataFormats.UnicodeText) as string;
        else if (e.DataObject.GetDataPresent(DataFormats.Text)) text = e.DataObject.GetData(DataFormats.Text) as string;
        if (string.IsNullOrEmpty(text)) return;
        text = text.Replace("\r\n", "\n");
        if (!_multiline) text = text.Replace('\n', ' ');
        Selection.Text = text;
        CaretPosition = Selection.End;
    }
}
