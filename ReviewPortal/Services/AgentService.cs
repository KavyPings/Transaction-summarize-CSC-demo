namespace ReviewPortal.Services;

public static class AgentService
{
    public static (string Summary, List<string> Questions) ParseAnalyzeResponse(string raw)
    {
        string summary = "";
        var questions = new List<string>();

        if (!raw.Contains("SUMMARY:"))
            return (raw.Trim(), questions);

        var afterSummary = raw.Split("SUMMARY:", 2)[1];
        if (afterSummary.Contains("QUESTIONS:"))
        {
            var idx = afterSummary.IndexOf("QUESTIONS:", StringComparison.Ordinal);
            summary = afterSummary[..idx].Trim();
            var questionsPart = afterSummary[(idx + "QUESTIONS:".Length)..].Trim();
            foreach (var line in questionsPart.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith('-'))
                    questions.Add(trimmed.TrimStart('-').Trim());
            }
        }
        else
        {
            summary = afterSummary.Trim();
        }

        return (summary, questions);
    }

    public static (string Answer, List<string> FollowUps) ParseChatResponse(string raw)
    {
        const string delimiter = "\nFOLLOW_UP:";
        if (!raw.Contains(delimiter))
            return (raw.Trim(), []);

        var idx = raw.IndexOf(delimiter, StringComparison.Ordinal);
        var answer = raw[..idx].Trim();
        var followUpPart = raw[(idx + delimiter.Length)..].Trim();

        var followUps = followUpPart
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith('-'))
            .Select(l => l.TrimStart('-').Trim())
            .ToList();

        return (answer, followUps);
    }
}
