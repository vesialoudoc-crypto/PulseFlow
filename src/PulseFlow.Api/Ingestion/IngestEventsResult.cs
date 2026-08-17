namespace PulseFlow.Api.Ingestion;

public sealed class IngestEventsResult
{
    internal IngestEventsResult(int total, int accepted)
    {
        Total = total;
        Accepted = accepted;
    }

    public int Total { get; }

    public int Accepted { get; }

    public int Rejected => Total - Accepted;
}
