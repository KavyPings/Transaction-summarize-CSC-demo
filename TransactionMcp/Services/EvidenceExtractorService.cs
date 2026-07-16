namespace TransactionMcp.Services;

public class EvidenceExtractorService
{
    private readonly string _tessDataPath;

    public EvidenceExtractorService(IWebHostEnvironment env)
    {
        _tessDataPath = Path.Combine(env.ContentRootPath, "tessdata");
    }

    public string ExtractText(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Evidence file not found: {path}");

        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".xlsx" or ".xls"               => ExtractExcel(path),
            ".pdf"                           => ExtractPdf(path),
            ".msg"                           => ExtractMsg(path),
            ".png" or ".jpg" or ".jpeg"
                or ".bmp" or ".tiff"        => ExtractImage(path),
            var ext => throw new NotSupportedException($"Unsupported file type: {ext}"),
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
            if (lines.Count > 1) sections.Add(string.Join("\n", lines));
        }
        return sections.Count > 0 ? string.Join("\n\n", sections) : "(empty workbook)";
    }

    private static string ExtractPdf(string path)
    {
        var pages = new List<string>();
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(path);
        int n = 0;
        foreach (var page in pdf.GetPages())
        {
            n++;
            var text = UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor
                .ContentOrderTextExtractor.GetText(page);
            if (!string.IsNullOrWhiteSpace(text))
                pages.Add($"--- Page {n} ---\n{text.Trim()}");
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

    private string ExtractImage(string path)
    {
        if (!Directory.Exists(_tessDataPath))
            return $"[Image file — OCR unavailable. Place eng.traineddata in {_tessDataPath}]";

        try
        {
            using var engine = new Tesseract.TesseractEngine(_tessDataPath, "eng", Tesseract.EngineMode.Default);
            using var img = Tesseract.Pix.LoadFromFile(path);
            using var page = engine.Process(img);
            var text = page.GetText().Trim();
            return string.IsNullOrEmpty(text) ? "(no text detected in image)" : text;
        }
        catch (Exception ex)
        {
            return $"[Image OCR failed: {ex.Message}]";
        }
    }

    public static string Label(string path, int index) =>
        $"Evidence {index} ({Path.GetExtension(path).TrimStart('.').ToUpperInvariant()})";
}
