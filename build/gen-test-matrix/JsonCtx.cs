using GenTestMatrix.Models;
using System.Text.Json.Serialization;

namespace GenTestMatrix
{
    [JsonSourceGenerationOptions(
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        GenerationMode = JsonSourceGenerationMode.Default,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        UseStringEnumConverter = true)]
    [JsonSerializable(typeof(OS))]
    [JsonSerializable(typeof(Dotnet))]
    [JsonSerializable(typeof(Emulator))]
    [JsonSerializable(typeof(Job))]
    [JsonSerializable(typeof(JobGroup))]
    [JsonSerializable(typeof(JobBatch))]
    [JsonSerializable(typeof(MatrixResult<JobBatch>))]
    internal sealed partial class JsonCtx : JsonSerializerContext
    {
    }
}
