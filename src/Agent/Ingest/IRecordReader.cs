using Agent.Domain;

namespace Agent.Ingest;

public interface IRecordReader
{
    IReadOnlyList<ProspectCase> ReadAll(TextReader reader);
}
