using System.Text;
using OpenAI.Chat;

namespace TransactionApproval.Services;

public class EvidenceExtractorService
{
    private static readonly HashSet<string> ImageExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".bmp", ".tiff" };

    private static readonly Dictionary<string, string> MimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"]  = "image/png",
        [".jpg"]  = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".bmp"]  = "image/bmp",
        [".tiff"] = "image/tiff",
    };

    public bool IsImageFile(string path) =>
        ImageExtensions.Contains(Path.GetExtension(path));

    public UserChatMessage BuildUserMessage(string transactionJson, IEnumerable<string> evidencePaths)
    {
        var paths = evidencePaths.ToList();
        if (paths.Count == 0)
            return new UserChatMessage(transactionJson);

        bool hasImages = paths.Any(IsImageFile);

        if (!hasImages)
        {
            var sb = new StringBuilder(transactionJson);
            for (int i = 0; i < paths.Count; i++)
            {
                var label = Label(paths[i], i + 1);
                try
                {
                    sb.Append($"\n\n{label}:\n{ExtractText(paths[i])}");
                }
                catch (Exception ex)
                {
                    sb.Append($"\n\n{label}: [Could not extract — {ex.Message}]");
                }
            }
            return new UserChatMessage(sb.ToString());
        }

        var parts = new List<ChatMessageContentPart>
        {
            ChatMessageContentPart.CreateTextPart(transactionJson)
        };

        for (int i = 0; i < paths.Count; i++)
        {
            var path = paths[i];
            var label = Label(path, i + 1);
            if (IsImageFile(path))
            {
                parts.Add(ChatMessageContentPart.CreateTextPart($"\n\n{label}:"));
                try
                {
                    var bytes = File.ReadAllBytes(path);
                    var mime = MimeTypes.GetValueOrDefault(Path.GetExtension(path), "image/png");
                    parts.Add(ChatMessageContentPart.CreateImagePart(
                        BinaryData.FromBytes(bytes), mime, ChatImageDetailLevel.High));
                }
                catch (Exception ex)
                {
                    // Degrade gracefully (matching the text-evidence branch) so a missing or
                    // unreadable image file can't take down the whole /agent/analyze request.
                    parts.Add(ChatMessageContentPart.CreateTextPart($"[Could not load image — {ex.Message}]"));
                }
            }
            else
            {
                try
                {
                    parts.Add(ChatMessageContentPart.CreateTextPart($"\n\n{label}:\n{ExtractText(path)}"));
                }
                catch (Exception ex)
                {
                    parts.Add(ChatMessageContentPart.CreateTextPart($"\n\n{label}: [Could not extract — {ex.Message}]"));
                }
            }
        }

        return new UserChatMessage(parts);
    }

    public string ExtractText(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Evidence file not found: {path}");

        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".xlsx" or ".xls" => ExtractExcel(path),
            ".pdf"            => ExtractPdf(path),
            ".msg"            => ExtractMsg(path),
            var ext           => throw new NotSupportedException($"Unsupported file type: {ext}"),
        };
    }

    private static string ExtractExcel(string path)
    {
        var sections = new List<string>();
        using var wb = new ClosedXML.Excel.XLWorkbook(path);
        foreach (var ws in wb.Worksheets)
        {
            var lines = new List<string> { $"Sheet: {ws.Name}" };
            foreach (var row in ws.RowsUsed())
            {
                var cells = row.CellsUsed()
                    .Select(c => c.Value.ToString() ?? "")
                    .ToArray();
                if (cells.Length > 0)
                    lines.Add(string.Join("  |  ", cells));
            }
            if (lines.Count > 1)
                sections.Add(string.Join("\n", lines));
        }
        return sections.Count > 0 ? string.Join("\n\n", sections) : "(empty workbook)";
    }

    private static string ExtractPdf(string path)
    {
        var pages = new List<string>();
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(path);
        int pageNum = 0;
        foreach (var page in pdf.GetPages())
        {
            pageNum++;
            var text = UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor
                .ContentOrderTextExtractor.GetText(page);
            if (!string.IsNullOrWhiteSpace(text))
                pages.Add($"--- Page {pageNum} ---\n{text.Trim()}");
        }
        return pages.Count > 0 ? string.Join("\n\n", pages) : "(no text found in PDF)";
    }

    private static string ExtractMsg(string path)
    {
        using var msg = new MsgReader.Outlook.Storage.Message(path);
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(msg.Subject))
            parts.Add($"Subject: {msg.Subject}");
        if (msg.Sender?.DisplayName is { } sender && !string.IsNullOrEmpty(sender))
            parts.Add($"From: {sender}");
        else if (msg.Sender?.Email is { } email && !string.IsNullOrEmpty(email))
            parts.Add($"From: {email}");
        if (msg.SentOn.HasValue)
            parts.Add($"Date: {msg.SentOn}");
        if (!string.IsNullOrEmpty(msg.BodyText))
            parts.Add($"\nBody:\n{msg.BodyText.Trim()}");
        return string.Join("\n", parts);
    }

    private static string Label(string path, int index) =>
        $"Evidence {index} ({Path.GetExtension(path).TrimStart('.').ToUpperInvariant()})";
}
