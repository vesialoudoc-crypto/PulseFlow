using System.ComponentModel.DataAnnotations;

namespace PulseFlow.Api.Ingestion;

public sealed class IngestionOptions
{
    public const string SectionName = "Ingestion";
    public const long BytesPerMebibyte = 1024L * 1024L;
    public const long DefaultMaxBatchBytes = 10L * BytesPerMebibyte;
    public const long MaximumMaxBatchBytes = 100L * BytesPerMebibyte;

    [Range(1, int.MaxValue)]
    public int ChunkCapacity { get; init; }

    [Range(1, MaximumMaxBatchBytes)]
    public long MaxBatchBytes { get; init; }
}
