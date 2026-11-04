using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using AiDetect.Core.Analysis;

namespace AiDetect.Core.Reporting;

public static class JsonReportWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string Render(AnalysisReport report) => JsonSerializer.Serialize(report, Options);
}
