using System.Text;

namespace Terraria_Wiki.Services;

public sealed class BatchLineWriter : IDisposable
{
    private readonly string _filePath;
    private readonly int _batchSize;
    private readonly List<string> _buffer;
    private readonly HashSet<string> _knownLines;
    private readonly object _lock = new();
    private bool _disposed;

    public BatchLineWriter(string filePath, int batchSize = 200)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        _filePath = filePath;
        _batchSize = batchSize;
        _buffer = new List<string>(batchSize);
        _knownLines = File.Exists(filePath)
            ? File.ReadLines(filePath).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
    }

    public void Add(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_knownLines.Add(line)) return;
            _buffer.Add(line);
            if (_buffer.Count >= _batchSize) FlushInternal();
        }
    }

    public void Flush()
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            FlushInternal();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            FlushInternal();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }

    private void FlushInternal()
    {
        if (_buffer.Count == 0) return;
        File.AppendAllLines(_filePath, _buffer, new UTF8Encoding(false));
        _buffer.Clear();
    }
}

public sealed class BatchLineItem
{
    private readonly BatchLineProvider _provider;
    private int _completed;

    internal BatchLineItem(BatchLineProvider provider, string line, int lineNumber)
    {
        _provider = provider;
        Line = line;
        LineNumber = lineNumber;
    }

    public string Line { get; }
    internal int LineNumber { get; }

    public void Complete()
    {
        if (Interlocked.Exchange(ref _completed, 1) == 0)
            _provider.Complete(this);
    }
}

public sealed class BatchLineProvider : IDisposable
{
    private readonly string _filePath;
    private readonly object _fileLock = new();
    private readonly HashSet<int> _completedOutOfOrder = [];
    private int _nextLine;
    private int _completedLine;
    private int _completedItemCount;
    private bool _isFileExhausted;
    private bool _disposed;

    public BatchLineProvider(string filePath, int batchSize = 50, int startLine = 0, int initialCompletedCount = 0, IEnumerable<int>? completedLines = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        ArgumentOutOfRangeException.ThrowIfNegative(startLine);
        ArgumentOutOfRangeException.ThrowIfNegative(initialCompletedCount);
        _filePath = filePath;
        _nextLine = startLine;
        _completedLine = startLine;
        _completedItemCount = initialCompletedCount;
        if (completedLines is not null)
        {
            foreach (var line in completedLines.Where(line => line >= startLine))
                _completedOutOfOrder.Add(line);
            while (_completedOutOfOrder.Remove(_completedLine))
                _completedLine++;
        }
    }

    public int CompletedItemCount
    {
        get
        {
            lock (_fileLock) return _completedItemCount;
        }
    }

    public int CompletedLineCount
    {
        get
        {
            lock (_fileLock) return _completedLine;
        }
    }

    public Task<BatchLineItem?> GetNextItemAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_fileLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_isFileExhausted) return Task.FromResult<BatchLineItem?>(null);

            while (_completedOutOfOrder.Contains(_nextLine))
                _nextLine++;

            var lines = ReadNextLines(_filePath, _nextLine, 1);
            if (lines.Count == 0)
            {
                _isFileExhausted = true;
                return Task.FromResult<BatchLineItem?>(null);
            }

            var item = new BatchLineItem(this, lines[0], _nextLine);
            _nextLine++;
            return Task.FromResult<BatchLineItem?>(item);
        }
    }

    public IReadOnlyCollection<int> GetCompletedLineNumbers()
    {
        lock (_fileLock)
            return Enumerable.Range(0, _completedLine).Concat(_completedOutOfOrder).ToArray();
    }

    internal void Complete(BatchLineItem item)
    {
        lock (_fileLock)
        {
            if (item.LineNumber < _completedLine) return;
            _completedItemCount++;
            _completedOutOfOrder.Add(item.LineNumber);
            while (_completedOutOfOrder.Remove(_completedLine))
                _completedLine++;
        }
    }

    public void Dispose()
    {
        lock (_fileLock)
        {
            if (_disposed) return;
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }

    private static List<string> ReadNextLines(string filePath, int startLine, int count)
    {
        if (!File.Exists(filePath)) return [];
        return File.ReadLines(filePath)
            .Skip(startLine)
            .Take(count)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();
    }
}
