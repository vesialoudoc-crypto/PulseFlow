using System.ComponentModel.DataAnnotations;

namespace PulseFlow.Api.Ingestion;

public sealed class IngestionOptions
{
    public const string SectionName = "Ingestion";

    [Range(1, int.MaxValue)]
    public int ChunkCapacity { get; init; }
}
