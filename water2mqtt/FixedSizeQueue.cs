namespace water2mqtt;

class FixedSizeList<T>
{
    private readonly int _maxSize;
    private readonly List<T> _list;

    public FixedSizeList(int maxSize)
    {
        if (maxSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxSize));

        _maxSize = maxSize;
        _list = new List<T>(maxSize);
    }

    public void Add(T item)
    {
        if (_list.Count == _maxSize)
        {
            _list.RemoveAt(0);   // drop oldest
        }
        _list.Add(item);
    }

    public T this[int index] => _list[index];
    public int Count => _list.Count;
    public IReadOnlyList<T> Items => _list;
}