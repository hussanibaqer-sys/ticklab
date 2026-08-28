using System.Collections.Concurrent;

namespace TickLab.Core.History;

public sealed class DataRangeCoordinator
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks =
        new(StringComparer.OrdinalIgnoreCase);

    public async ValueTask<IAsyncDisposable> AcquireAsync(
        string connectorId,
        string symbol,
        string dataKind,
        CancellationToken cancellationToken)
    {
        string key = string.Join("|", connectorId.Trim(), symbol.Trim(), dataKind.Trim());
        SemaphoreSlim gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Lease(gate);
    }

    private sealed class Lease : IAsyncDisposable
    {
        private SemaphoreSlim? _gate;

        public Lease(SemaphoreSlim gate) => _gate = gate;

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref _gate, null)?.Release();
            return ValueTask.CompletedTask;
        }
    }
}
