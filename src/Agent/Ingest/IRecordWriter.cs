namespace Agent.Ingest;

public interface IRecordWriter<in T>
{
    Task WriteAllAsync(TextWriter writer, IEnumerable<T> records, CancellationToken cancellationToken = default);
}
