using System.Collections;

namespace RepoAtlas.McpServer.Tests.TestHelpers;

/// <summary>
/// A lock-protected <see cref="ICollection{T}"/>, for use as the export target of an OpenTelemetry
/// in-memory exporter under <see cref="RepoAtlasTelemetry"/>'s process-wide, statically shared
/// <c>ActivitySource</c>/<c>Meter</c>. Because those instruments are shared across the whole test
/// process, an exporter subscribed to them can receive concurrent <see cref="Add"/> calls from
/// activities/metrics emitted by unrelated tests running in parallel; a plain <see cref="List{T}"/>
/// throws "Collection was modified" if enumerated (e.g. by an assertion) while that happens.
/// </summary>
public sealed class ThreadSafeCollection<T> : ICollection<T>
{
    private readonly List<T> _items = new();
    private readonly object _gate = new();

    public void Add(T item)
    {
        lock (_gate)
        {
            _items.Add(item);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _items.Clear();
        }
    }

    public bool Contains(T item)
    {
        lock (_gate)
        {
            return _items.Contains(item);
        }
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
        lock (_gate)
        {
            _items.CopyTo(array, arrayIndex);
        }
    }

    public bool Remove(T item)
    {
        lock (_gate)
        {
            return _items.Remove(item);
        }
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _items.Count;
            }
        }
    }

    public bool IsReadOnly => false;

    /// <summary>Returns an enumerator over a point-in-time snapshot, taken under lock.</summary>
    public IEnumerator<T> GetEnumerator()
    {
        lock (_gate)
        {
            return _items.ToList().GetEnumerator();
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
