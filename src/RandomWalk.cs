using Common.Logging;
using Ipfs.CoreApi;
using PeerTalk;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Ipfs.Engine;

/// <summary>
/// Periodically queries the DHT to discover new peers.
/// </summary>
/// <remarks>
/// A backgroud task is created to query the DHT. It is designed to run often at startup and
/// then less often at time increases.
/// </remarks>
public class RandomWalk : IService
{
    /// <summary>
    /// The time to wait until running the query.
    /// </summary>
    public TimeSpan Delay = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The time to add to the <see cref="Delay"/>.
    /// </summary>
    public TimeSpan DelayIncrement = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The maximum <see cref="Delay"/>.
    /// </summary>
    public TimeSpan DelayMax = TimeSpan.FromMinutes(9);

    private static readonly ILog log = LogManager.GetLogger<RandomWalk>();
    private CancellationTokenSource cancel;

    /// <summary>
    /// The Distributed Hash Table to query.
    /// </summary>
    public IDhtApi Dht { get; set; }

    /// <summary>
    /// Start a background process that will run a random walk every <see cref="Delay"/>.
    /// </summary>
    public Task StartAsync()
    {
        if (cancel is not null)
        {
            throw new Exception("Already started.");
        }

        cancel = new CancellationTokenSource();
        RunnerAsync(cancel.Token).Forget();

        log.Debug("started");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Stop the background process.
    /// </summary>
    public async Task StopAsync()
    {
        if (cancel is not null)
        {
            await cancel.CancelAsync();
            cancel.Dispose();
            cancel = null;
        }

        log.Debug("stopped");
    }

    /// <summary>
    /// The background process.
    /// </summary>
    private async Task RunnerAsync(CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Delay, cancellation);
                await RunQueryAsync(cancellation).ConfigureAwait(false);
                log.Debug("query finished");
                Delay += DelayIncrement;
                if (Delay > DelayMax)
                {
                    Delay = DelayMax;
                }
            }
            catch (TaskCanceledException)
            {
                // eat it.
            }
            catch (Exception e)
            {
                log.Error("run query failed", e);
                // eat all exceptions
            }
        }
    }

    private async Task RunQueryAsync(CancellationToken cancel = default)
    {
        // Tests may not set a DHT.
        if (Dht is null)
        {
            return;
        }

        log.Debug("Running a query");

        // Get a random peer id.
        byte[] x = new byte[32];
        Random rng = new();
        rng.NextBytes(x);
        MultiHash id = MultiHash.ComputeHash(x);

        _ = await Dht.FindPeerAsync(id, cancel).ConfigureAwait(false);
    }
}