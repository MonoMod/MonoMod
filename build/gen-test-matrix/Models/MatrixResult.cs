using System.Text.Json.Serialization;

namespace GenTestMatrix.Models;

internal sealed record MatrixResult<T>
{
    [JsonPropertyName("include")]
    public required IEnumerable<T> Jobs { get; init; }
}
