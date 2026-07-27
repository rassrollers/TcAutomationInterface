using EnvDTE80;
using System.Collections;

namespace AutomationInterface.core;

public class ErrorItemsEnumerable : IEnumerable<ErrorItem>
{
    private readonly ErrorItems _errorItems;

    public ErrorItemsEnumerable(ErrorItems errorItems)
    {
        _errorItems = errorItems ?? throw new ArgumentNullException(nameof(errorItems));
    }

    public IEnumerator<ErrorItem> GetEnumerator()
    {
        return new ErrorItemsEnumerator(_errorItems);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}

public class ErrorItemsEnumerator : IEnumerator<ErrorItem>
{
    private readonly ErrorItems _errorItems;
    private int _index = -1; // Start before first element

    public ErrorItemsEnumerator(ErrorItems errorItems)
    {
        _errorItems = errorItems ?? throw new ArgumentNullException(nameof(errorItems));
    }

    public ErrorItem Current => _errorItems.Item(_index + 1); // FIXED: Calling Item() as a method

    object IEnumerator.Current => Current;

    public bool MoveNext()
    {
        if (_index + 1 < _errorItems.Count)
        {
            _index++;
            return true;
        }
        return false;
    }

    public void Reset()
    {
        _index = -1;
    }

    public void Dispose() { }
}
