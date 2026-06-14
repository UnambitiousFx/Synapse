using System.Collections;

namespace UnambitiousFx.Synapse.Generator;

/// <summary>
///     An immutable array wrapper with structural (value) equality, suitable for use as state in the
///     incremental generator pipeline. <see cref="System.Collections.Immutable.ImmutableArray{T}" /> uses
///     reference equality, which defeats incremental caching; this type compares element-by-element instead.
/// </summary>
/// <typeparam name="T">Element type; must itself be value-equatable.</typeparam>
public readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    private readonly T[]? _array;

    public EquatableArray(T[]? array)
    {
        _array = array;
    }

    public int Count => _array?.Length ?? 0;

    public T this[int index] => _array![index];

    public ReadOnlySpan<T> AsSpan()
    {
        return _array.AsSpan();
    }

    public bool Equals(EquatableArray<T> other)
    {
        return AsSpan().SequenceEqual(other.AsSpan());
    }

    public override bool Equals(object? obj)
    {
        return obj is EquatableArray<T> other && Equals(other);
    }

    public override int GetHashCode()
    {
        if (_array is null)
        {
            return 0;
        }

        unchecked
        {
            var hash = 17;
            foreach (var item in _array)
            {
                hash = hash * 31 + (item?.GetHashCode() ?? 0);
            }

            return hash;
        }
    }

    public IEnumerator<T> GetEnumerator()
    {
        return ((IEnumerable<T>)(_array ?? [])).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public static EquatableArray<T> From(IEnumerable<T> source)
    {
        return new EquatableArray<T>(source.ToArray());
    }
}
