using System.Collections.Concurrent;
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

    internal BatchLineItem(BatchLineProvider provider, string line, object batch)
    {
        _provider = provider;
        Line = line;
        Batch = batch;
    }

    public string Line { get; }
    internal object Batch { get; }

    public void Complete()
    {
        if (Interlocked.Exchange(ref _completed, 1) == 0)
            _provider.Complete(this);
    }
}

public sealed class BatchLineProvider : IDisposable
{
    private sealed class BatchState
    {
        public required long TruncatePosition { get; init; }
        public int Remaining;
    }

    private readonly string _filePath;
    private readonly int _batchSize;
    private readonly ConcurrentQueue<BatchLineItem> _memoryQueue = new();
    private readonly object _fileLock = new();
    private TaskCompletionSource<bool> _stateChanged = NewSignal();
    private BatchState? _currentBatch;
    private bool _isFileExhausted;
    private bool _disposed;

    public BatchLineProvider(string filePath, int batchSize = 50)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        _filePath = filePath;
        _batchSize = batchSize;
    }

    public async Task<BatchLineItem?> GetNextItemAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            Task waitTask;
            lock (_fileLock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_memoryQueue.TryDequeue(out var item)) return item;
                if (_isFileExhausted) return null;

                if (_currentBatch is null)
                {
                    var (lines, position) = PeekLastNLines(_filePath, _batchSize);
                    if (lines.Count == 0)
                    {
                        _isFileExhausted = true;
                        return null;
                    }

                    _currentBatch = new BatchState
                    {
                        TruncatePosition = position,
                        Remaining = lines.Count
                    };
                    foreach (var line in lines)
                        _memoryQueue.Enqueue(new BatchLineItem(this, line, _currentBatch));
                    return _memoryQueue.TryDequeue(out item) ? item : null;
                }

                waitTask = _stateChanged.Task;
            }

            await waitTask.WaitAsync(cancellationToken);
        }
    }

    internal void Complete(BatchLineItem item)
    {
        lock (_fileLock)
        {
            if (item.Batch is not BatchState batch || !ReferenceEquals(batch, _currentBatch)) return;
            if (--batch.Remaining != 0) return;

            TruncateFile(_filePath, batch.TruncatePosition);
            _currentBatch = null;
            SignalStateChanged();
        }
    }

    public void Dispose()
    {
        lock (_fileLock)
        {
            if (_disposed) return;
            _disposed = true;
            SignalStateChanged();
        }
        GC.SuppressFinalize(this);
    }

    private static TaskCompletionSource<bool> NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private void SignalStateChanged()
    {
        _stateChanged.TrySetResult(true);
        _stateChanged = NewSignal();
    }

    private static (List<string> lines, long newPosition) PeekLastNLines(string filePath, int count)
    {
        if (!File.Exists(filePath)) return ([], 0);
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (fs.Length == 0) return ([], 0);

        long pos = fs.Length - 1;
        int linesFound = 0;
        while (pos >= 0)
        {
            fs.Position = pos;
            if (fs.ReadByte() == '\n' && ++linesFound > count)
            {
                pos++;
                break;
            }
            pos--;
        }

        if (pos < 0) pos = 0;
        fs.Position = pos;
        byte[] buffer = new byte[fs.Length - pos];
        fs.ReadExactly(buffer);
        var resultLines = Encoding.UTF8.GetString(buffer).Trim()
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        return (resultLines, pos);
    }

    private static void TruncateFile(string filePath, long length)
    {
        if (!File.Exists(filePath)) return;
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
        fs.SetLength(length);
    }
}
