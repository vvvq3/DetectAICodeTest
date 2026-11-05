using System.Text.Json;

namespace AiDetect.Core.Analysis;

/// <summary>
/// Параметры итоговой модели: переопределения весов признаков, смещение и температура.
/// По умолчанию используются экспертные веса из исследования; команда <c>calibrate</c> подбирает их на размеченных MR.
/// </summary>
public sealed class ModelParameters
{
    /// <summary>Априорный логит (0 = 50/50).</summary>
    public double Bias { get; set; } = -0.8;

    /// <summary>Делитель итогового логита: больше — осторожнее вероятности.</summary>
    public double Temperature { get; set; } = 2.5;

    /// <summary>Переопределения весов: id признака → вес в логитах.</summary>
    public Dictionary<string, double> Weights { get; set; } = new(StringComparer.Ordinal);

    public string? Description { get; set; }

    public double WeightFor(string id, double defaultWeight) =>
        Weights.TryGetValue(id, out var w) ? w : defaultWeight;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static ModelParameters Load(string path) =>
        JsonSerializer.Deserialize<ModelParameters>(File.ReadAllText(path), JsonOptions) ?? new ModelParameters();

    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));

    /// <summary>
    /// Веса языка: ищет weights.{язык}.json рядом с программой (и в model/); для C# также старое имя weights.json.
    /// Если файла нет — экспертные веса по умолчанию.
    /// </summary>
    public static ModelParameters LoadDefault(string languageId = "csharp")
    {
        var names = new List<string> { $"weights.{languageId}.json" };
        if (languageId == "csharp")
        {
            names.Add("weights.json");
        }

        foreach (var name in names)
        {
            foreach (var candidate in new[]
                     {
                         Path.Combine(AppContext.BaseDirectory, name),
                         Path.Combine(AppContext.BaseDirectory, "model", name),
                     })
            {
                if (File.Exists(candidate))
                {
                    try
                    {
                        return Load(candidate);
                    }
                    catch (JsonException)
                    {
                        // Повреждённый файл весов — работаем на значениях по умолчанию.
                    }
                }
            }
        }

        return new ModelParameters();
    }
}
