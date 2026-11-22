using System.Text;

namespace AutomationPortal.Core;

public enum AiQuestionKind { Choice, Folder, File, Text, YesNo }

/// <summary>One thing "Describe it" wants to know before it builds. <paramref name="Default"/> is what the person gets if they just move on.</summary>
public sealed record AiQuestion(string Text, AiQuestionKind Kind, IReadOnlyList<string> Options, string Default);

public static class AiQuestions
{
    /// <summary>The request plus the answers, ready for the build call. Unanswered questions are left out.</summary>
    public static string AppendAnswers(string description, IReadOnlyList<AiQuestion> questions, IReadOnlyList<string> answers)
    {
        var sb = new StringBuilder(description.Trim());
        var first = true;
        for (var i = 0; i < questions.Count && i < answers.Count; i++)
        {
            var answer = (answers[i] ?? "").Trim();
            if (answer.Length == 0) continue;
            if (first) { sb.Append("\n\nMore details from the person:"); first = false; }
            sb.Append("\n- ").Append(questions[i].Text).Append(" ").Append(answer);
        }
        return sb.ToString();
    }
}
