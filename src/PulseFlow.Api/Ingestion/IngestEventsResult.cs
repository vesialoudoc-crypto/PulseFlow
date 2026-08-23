namespace PulseFlow.Api.Ingestion;

public sealed class IngestEventsResult
{
    public int Total { get; }

    public int Accepted { get; }

    public int Rejected => Total - Accepted;

    internal IngestEventsResult(int total, int accepted)
    {
        Total = total;
        Accepted = accepted;
    }
}
