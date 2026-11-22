namespace AutomationPortal.Core;

public enum AiQuestionKind { Choice, Folder, File, Text, YesNo }

/// <summary>One thing "Describe it" wants to know before it builds. <paramref name="Default"/> is what the person gets if they just move on.</summary>
public sealed record AiQuestion(string Text, AiQuestionKind Kind, IReadOnlyList<string> Options, string Default);
