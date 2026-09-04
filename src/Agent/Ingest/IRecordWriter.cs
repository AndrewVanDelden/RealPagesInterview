namespace Agent.Ingest;

public interface IRecordWriter<in T>
{
    void WriteAll(TextWriter writer, IEnumerable<T> records);
}
