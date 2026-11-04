using System.Text.Encodings.Web;
using System.Text.Json;
using AiDetect.Core.Model;

namespace AiDetect.Core.Sources;

/// <summary>Сохранение и загрузка MR в JSON — для повторного анализа без сети и для корпуса тестов.</summary>
public sealed class JsonMergeRequestStore(string path) : IMergeRequestSource
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public async Task<MergeRequestData> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<MergeRequestData>(stream, Options, cancellationToken)
               ?? throw new SourceException($"Пустой файл MR: {path}");
    }

    public static async Task SaveAsync(MergeRequestData data, string path, CancellationToken cancellationToken = default)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (dir is not null)
        {
            Directory.CreateDirectory(dir);
        }

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, data, Options, cancellationToken);
    }
}
