using Common.Logging;
using Ipfs.CoreApi;
using Ipfs.Engine.CoreApi;
using Ipfs.Engine.Cryptography;
using Ipfs.Engine.Migration;
using Makaretu.Dns;
using Nito.AsyncEx;
using PeerTalk;
using PeerTalk.Cryptography;
using PeerTalk.Discovery;
using PeerTalk.SecureCommunication;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

namespace Ipfs.Engine
{
    /// <summary>
    /// Implements the <see cref="ICoreApi">Core API</see> which makes it possible to
    /// create a decentralised and distributed application without relying on an "IPFS daemon".
    /// </summary>
    /// <remarks>
    /// The engine should be used as a shared object in your program. It is thread safe (re-entrant)
    /// and conserves resources when only one instance is used.
    /// </remarks>
    public partial class IpfsEngine : ICoreApi, IService, IDisposable
    {
        private static readonly ILog log = LogManager.GetLogger<IpfsEngine>();

        private KeyChain keyChain;
        private readonly SecureString passphrase;
        private ConcurrentBag<Func<Task>> stopTasks = [];

        /// <summary>
        /// Creates a new instance of the <see cref="IpfsEngine"/> class with the IPFS_PASS
        /// environment variable.
        /// </summary>
        /// <remarks>Th passphrase must be in the IPFS_PASS environment variable.</remarks>
        public IpfsEngine()
        {
            string s = Environment.GetEnvironmentVariable("IPFS_PASS") ?? throw new Exception("The IPFS_PASS environement variable is missing.");
            passphrase = new SecureString();
            foreach (char c in s)
            {
                passphrase.AppendChar(c);
            }

            Init();
        }

        /// <summary>
        /// Creates a new instance of the <see cref="IpfsEngine"/> class with the specified passphrase.
        /// </summary>
        /// <param name="passphrase">The password used to access the keychain.</param>
        /// <remarks>
        /// A <b>SecureString</b> copy of the passphrase is made so that the array can be zeroed out
        /// after the call.
        /// </remarks>
        public IpfsEngine(char[] passphrase)
        {
            this.passphrase = new SecureString();
            foreach (char c in passphrase)
            {
                this.passphrase.AppendChar(c);
            }
            Init();
        }

        /// <summary>
        /// Creates a new instance of the <see cref="IpfsEngine"/> class with the specified passphrase.
        /// </summary>
        /// <param name="passphrase">The password used to access the keychain.</param>
        /// <remarks>A copy of the <paramref name="passphrase"/> is made.</remarks>
        public IpfsEngine(SecureString passphrase)
        {
            this.passphrase = passphrase.Copy();
            Init();
        }

        private void Init()
        {
            // Init the core api inteface.
            Bitswap = new BitswapApi(this);
            Block = new BlockApi(this);
            BlockRepository = new BlockRepositoryApi(this);
            Bootstrap = new BootstrapApi(this);
            Config = new ConfigApi(this);
            Dag = new DagApi(this);
            Dht = new DhtApi(this);
            Dns = new DnsApi(this);
            FileSystem = new FileSystemApi(this);
            Generic = new GenericApi(this);
            Key = new KeyApi(this);
            Name = new NameApi(this);
            Object = new ObjectApi(this);
            Pin = new PinApi(this);
            PubSub = new PubSubApi(this);
            Stats = new StatsApi(this);
            Swarm = new SwarmApi(this);

            MigrationManager = new MigrationManager(this);

            // Async properties
            LocalPeer = new AsyncLazy<Peer>(async () =>
            {
                log.Debug("Building local peer");
                KeyChain keyChain = await KeyChainAsync().ConfigureAwait(false);
                log.Debug("Getting key info about self");
                IKey self = await keyChain.FindKeyByNameAsync("self").ConfigureAwait(false);
                Peer localPeer = new()
                {
                    Id = self.Id,
                    PublicKey = await keyChain.GetPublicKeyAsync("self").ConfigureAwait(false),
                    ProtocolVersion = "ipfs/0.1.0"
                };
                Version version = typeof(IpfsEngine).GetTypeInfo().Assembly.GetName().Version;
                localPeer.AgentVersion = $"net-ipfs/{version.Major}.{version.Minor}.{version.Revision}";
                log.Debug("Built local peer");
                return localPeer;
            });
            SwarmService = new AsyncLazy<Swarm>(async () =>
            {
                log.Debug("Building swarm service");
                if (Options.Swarm.PrivateNetworkKey == null)
                {
                    string path = Path.Combine(Options.Repository.Folder, "swarm.key");
                    if (File.Exists(path))
                    {
                        using StreamReader x = File.OpenText(path);
                        Options.Swarm.PrivateNetworkKey = new PreSharedKey();
                        Options.Swarm.PrivateNetworkKey.Import(x);
                    }
                }
                Peer peer = await LocalPeer.ConfigureAwait(false);
                KeyChain keyChain = await KeyChainAsync().ConfigureAwait(false);
                Org.BouncyCastle.Crypto.AsymmetricKeyParameter self = await keyChain.GetPrivateKeyAsync("self").ConfigureAwait(false);
                Swarm swarm = new()
                {
                    LocalPeer = peer,
                    LocalPeerKey = PeerTalk.Cryptography.Key.CreatePrivateKey(self),
                    NetworkProtector = Options.Swarm.PrivateNetworkKey == null
                        ? null
                        : new Psk1Protector { Key = Options.Swarm.PrivateNetworkKey }
                };
                if (Options.Swarm.PrivateNetworkKey != null)
                {
                    log.Debug($"Private network {Options.Swarm.PrivateNetworkKey.Fingerprint().ToHexString()}");
                }

                log.Debug("Built swarm service");
                return swarm;
            });
            BitswapService = new AsyncLazy<BlockExchange.Bitswap>(async () =>
            {
                log.Debug("Building bitswap service");
                BlockExchange.Bitswap bitswap = new()
                {
                    Swarm = await SwarmService.ConfigureAwait(false),
                    BlockService = Block
                };
                log.Debug("Built bitswap service");
                return bitswap;
            });
            DhtService = new AsyncLazy<PeerTalk.Routing.Dht1>(async () =>
            {
                log.Debug("Building DHT service");
                PeerTalk.Routing.Dht1 dht = new()
                {
                    Swarm = await SwarmService.ConfigureAwait(false)
                };
                dht.Swarm.Router = dht;
                log.Debug("Built DHT service");
                return dht;
            });
            PingService = new AsyncLazy<PeerTalk.Protocols.Ping1>(async () =>
            {
                log.Debug("Building Ping service");
                PeerTalk.Protocols.Ping1 ping = new()
                {
                    Swarm = await SwarmService.ConfigureAwait(false)
                };
                log.Debug("Built Ping service");
                return ping;
            });
            PubSubService = new AsyncLazy<PeerTalk.PubSub.NotificationService>(async () =>
            {
                log.Debug("Building PubSub service");
                PeerTalk.PubSub.NotificationService pubsub = new()
                {
                    LocalPeer = await LocalPeer.ConfigureAwait(false)
                };
                pubsub.Routers.Add(new PeerTalk.PubSub.FloodRouter
                {
                    Swarm = await SwarmService.ConfigureAwait(false)
                });
                log.Debug("Built PubSub service");
                return pubsub;
            });
        }

        /// <summary>
        /// The configuration options.
        /// </summary>
        public IpfsEngineOptions Options { get; set; } = new IpfsEngineOptions();

        /// <summary>
        /// Manages the version of the repository.
        /// </summary>
        public MigrationManager MigrationManager { get; set; }

        /// <inheritdoc/>
        public IBitswapApi Bitswap { get; set; }

        /// <inheritdoc/>
        public IBlockApi Block { get; set; }

        /// <inheritdoc/>
        public IBlockRepositoryApi BlockRepository { get; set; }

        /// <inheritdoc/>
        public IBootstrapApi Bootstrap { get; set; }

        /// <inheritdoc/>
        public IConfigApi Config { get; set; }

        /// <inheritdoc/>
        public IDagApi Dag { get; set; }

        /// <inheritdoc/>
        public IDhtApi Dht { get; set; }

        /// <inheritdoc/>
        public IDnsApi Dns { get; set; }

        /// <inheritdoc/>
        public IFileSystemApi FileSystem { get; set; }

        /// <inheritdoc/>
        public IGenericApi Generic { get; set; }

        /// <inheritdoc/>
        public IKeyApi Key { get; set; }

        /// <inheritdoc/>
        public INameApi Name { get; set; }

        /// <inheritdoc/>
        public IObjectApi Object { get; set; }

        /// <inheritdoc/>
        public IPinApi Pin { get; set; }

        /// <inheritdoc/>
        public IPubSubApi PubSub { get; set; }

        /// <inheritdoc/>
        public ISwarmApi Swarm { get; set; }

        /// <inheritdoc/>
        public IStatsApi Stats { get; set; }

        /// <summary>
        /// Provides access to the <see cref="KeyChain"/>.
        /// </summary>
        /// <param name="cancel">
        /// Is used to stop the task. When cancelled, the <see cref="TaskCanceledException"/> is raised.
        /// </param>
        /// <returns>
        /// A task that represents the asynchronous operation. The task's result is the <see cref="KeyChain"/>.
        /// </returns>
        public async Task<KeyChain> KeyChainAsync(CancellationToken cancel = default)
        {
            // TODO: this should be a LazyAsync property.
            if (keyChain is null)
            {
                lock (this)
                {
                    keyChain ??= new KeyChain(this)
                    {
                        Options = Options.KeyChain
                    };
                }

                await keyChain.SetPassphraseAsync(passphrase, cancel).ConfigureAwait(false);

                // Maybe create "self" key, this is the local peer's id.
                IKey self = await keyChain.FindKeyByNameAsync("self", cancel).ConfigureAwait(false);
                _ = self ?? await keyChain.CreateAsync("self", null, 0, cancel).ConfigureAwait(false);
            }

            return keyChain;
        }

        /// <summary>
        /// Provides access to the local peer.
        /// </summary>
        /// <returns>
        /// A task that represents the asynchronous operation. The task's result is a <see cref="Peer"/>.
        /// </returns>
        public AsyncLazy<Peer> LocalPeer { get; private set; }

        /// <summary>
        /// Resolve an "IPFS path" to a content ID.
        /// </summary>
        /// <param name="path">A IPFS path, such as "Qm...", "Qm.../a/b/c" or "/ipfs/QM..."</param>
        /// <param name="cancel">
        /// Is used to stop the task. When cancelled, the <see cref="TaskCanceledException"/> is raised.
        /// </param>
        /// <returns>The content ID of <paramref name="path"/>.</returns>
        /// <exception cref="ArgumentException">The <paramref name="path"/> cannot be resolved.</exception>
        public async Task<Cid> ResolveIpfsPathToCidAsync(string path, CancellationToken cancel = default)
        {
            string r = await Generic.ResolveAsync(path, true, cancel).ConfigureAwait(false);
            return Cid.Decode(r[6..]);  // strip '/ipfs/'.
        }

        /// <summary>
        /// Determines if the engine has started.
        /// </summary>
        /// <value><b>true</b> if the engine has started; otherwise, <b>false</b>.</value>
        /// <seealso cref="Start"/>
        /// <seealso cref="StartAsync"/>
        public bool IsStarted => !stopTasks.IsEmpty;

        /// <summary>
        /// Starts the network services.
        /// </summary>
        /// <returns>A task that represents the asynchronous operation.</returns>
        /// <remarks>
        /// Starts the various IPFS and PeerTalk network services. This should be called after any
        /// configuration changes.
        /// </remarks>
        /// <exception cref="Exception">When the engine is already started.</exception>
        public async Task StartAsync()
        {
            if (!stopTasks.IsEmpty)
            {
                throw new Exception("IPFS engine is already started.");
            }

            // Repository must be at the correct version.
            await MigrationManager.MirgrateToVersionAsync(MigrationManager.LatestVersion)
                .ConfigureAwait(false);

            Peer localPeer = await LocalPeer.ConfigureAwait(false);
            log.Debug($"starting {localPeer.Id}");

            // Everybody needs the swarm.
            Swarm swarm = await SwarmService.ConfigureAwait(false);
            stopTasks.Add(swarm.StopAsync);
            await swarm.StartAsync().ConfigureAwait(false);

            PeerManager peerManager = new() { Swarm = swarm };
            await peerManager.StartAsync().ConfigureAwait(false);
            stopTasks.Add(peerManager.StopAsync);

            // Start the primary services.
            List<Func<Task>> tasks =
            [
                async () =>
                {
                    BlockExchange.Bitswap bitswap = await BitswapService.ConfigureAwait(false);
                    stopTasks.Add(async () => await bitswap.StopAsync().ConfigureAwait(false));
                    await bitswap.StartAsync().ConfigureAwait(false);
                },
                async () =>
                {
                    PeerTalk.Routing.Dht1 dht = await DhtService.ConfigureAwait(false);
                    stopTasks.Add(async () => await dht.StopAsync().ConfigureAwait(false));
                    await dht.StartAsync().ConfigureAwait(false);
                },
                async () =>
                {
                    PeerTalk.Protocols.Ping1 ping = await PingService.ConfigureAwait(false);
                    stopTasks.Add(async () => await ping.StopAsync().ConfigureAwait(false));
                    await ping.StartAsync().ConfigureAwait(false);
                },
                async () =>
                {
                    PeerTalk.PubSub.NotificationService pubsub = await PubSubService.ConfigureAwait(false);
                    stopTasks.Add(async () => await pubsub.StopAsync().ConfigureAwait(false));
                    await pubsub.StartAsync().ConfigureAwait(false);
                },
            ];

            log.Debug("waiting for services to start");
            await Task.WhenAll(tasks.Select(t => t())).ConfigureAwait(false);

            // Starting listening to the swarm.
            Newtonsoft.Json.Linq.JToken json = await Config.GetAsync("Addresses.Swarm").ConfigureAwait(false);
            int numberListeners = 0;
            foreach (string a in json.Select(v => (string)v))
            {
                try
                {
                    _ = await swarm.StartListeningAsync(a).ConfigureAwait(false);
                    ++numberListeners;
                }
                catch (Exception e)
                {
                    log.Warn($"Listener failure for '{a}'", e);
                    // eat the exception
                }
            }

            if (numberListeners == 0)
            {
                log.Error("No listeners were created.");
            }

            // Now that the listener addresses are established, the discovery services can begin.
            MulticastService multicast = null;
            if (!Options.Discovery.DisableMdns)
            {
                multicast = new MulticastService();
                stopTasks.Add(() => Task.Run(multicast.Dispose));
            }

            AutoDialer autodialer = new(swarm)
            {
                MinConnections = Options.Swarm.MinConnections
            };
            stopTasks.Add(() => Task.Run(autodialer.Dispose));

            tasks =
            [
                // Bootstrap discovery
                async () =>
                {
                    Bootstrap bootstrap = new() {
                        Addresses = await Bootstrap.ListAsync()
                    };
                    bootstrap.PeerDiscovered += OnPeerDiscovered;
                    stopTasks.Add(async () => await bootstrap.StopAsync().ConfigureAwait(false));
                    await bootstrap.StartAsync().ConfigureAwait(false);
                },
                // New multicast DNS discovery
                async () =>
                {
                    if (Options.Discovery.DisableMdns) { return; } MdnsNext mdns = new() {
                        LocalPeer = localPeer,
                        MulticastService = multicast
                    };
                    if (Options.Swarm.PrivateNetworkKey != null)
                    {
                        mdns.ServiceName = $"_p2p-{Options.Swarm.PrivateNetworkKey.Fingerprint().ToHexString()}._udp";
                    }
                    mdns.PeerDiscovered += OnPeerDiscovered;
                    stopTasks.Add(async () => await mdns.StopAsync().ConfigureAwait(false));
                    await mdns.StartAsync().ConfigureAwait(false);
                },
                // Old style JS multicast DNS discovery
                async () =>
                {
                    if (Options.Discovery.DisableMdns || Options.Swarm.PrivateNetworkKey != null) { return; } MdnsJs mdns = new() {
                        LocalPeer = localPeer,
                        MulticastService = multicast
                    };
                    mdns.PeerDiscovered += OnPeerDiscovered;
                    stopTasks.Add(async () => await mdns.StopAsync().ConfigureAwait(false));
                    await mdns.StartAsync().ConfigureAwait(false);
                },
                // Old style GO multicast DNS discovery
                async () =>
                {
                    if (Options.Discovery.DisableMdns || Options.Swarm.PrivateNetworkKey != null) { return; } MdnsGo mdns = new() {
                        LocalPeer = localPeer,
                        MulticastService = multicast
                    };
                    mdns.PeerDiscovered += OnPeerDiscovered;
                    stopTasks.Add(async () => await mdns.StopAsync().ConfigureAwait(false));
                    await mdns.StartAsync().ConfigureAwait(false);
                },
                async () =>
                {
                    if (Options.Discovery.DisableRandomWalk) { return; } RandomWalk randomWalk = new() { Dht = Dht };
                    stopTasks.Add(async () => await randomWalk.StopAsync().ConfigureAwait(false));
                    await randomWalk.StartAsync().ConfigureAwait(false);
                }
            ];
            log.Debug("waiting for discovery services to start");
            await Task.WhenAll(tasks.Select(t => t())).ConfigureAwait(false);

            multicast?.Start();

            log.Debug("started");
        }

        /// <summary>
        /// Stops the running services.
        /// </summary>
        /// <returns>A task that represents the asynchronous operation.</returns>
        /// <remarks>Multiple calls are okay.</remarks>
        public async Task StopAsync()
        {
            log.Debug("stopping");
            try
            {
                Func<Task>[] tasks = stopTasks.ToArray();
                stopTasks = [];
                await Task.WhenAll(tasks.Select(t => t())).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                log.Error("Failure when stopping the engine", e);
            }

            // Many services use cancellation to stop. A cancellation may not run immediately, so we
            // need to give them some.
            // TODO: Would be nice to make this deterministic.
            await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);

            log.Debug("stopped");
        }

        /// <summary>
        /// A synchronous start.
        /// </summary>
        /// <remarks>Calls <see cref="StartAsync"/> and waits for it to complete.</remarks>
        public void Start()
        {
            StartAsync().RunSynchronously();
        }

        /// <summary>
        /// A synchronous stop.
        /// </summary>
        /// <remarks>Calls <see cref="StopAsync"/> and waits for it to complete.</remarks>
        public void Stop()
        {
            log.Debug("stopping");
            try
            {
                Func<Task>[] tasks = stopTasks.ToArray();
                stopTasks = [];
                foreach (Func<Task> task in tasks)
                {
                    task().RunSynchronously();
                }
            }
            catch (Exception e)
            {
                log.Error("Failure when stopping the engine", e);
            }
        }

        /// <summary>
        /// Manages communication with other peers.
        /// </summary>
        public AsyncLazy<Swarm> SwarmService { get; private set; }

        /// <summary>
        /// Manages publishng and subscribing to messages.
        /// </summary>
        public AsyncLazy<PeerTalk.PubSub.NotificationService> PubSubService { get; private set; }

        /// <summary>
        /// Exchange blocks with other peers.
        /// </summary>
        public AsyncLazy<BlockExchange.Bitswap> BitswapService { get; private set; }

        /// <summary>
        /// Finds information with a distributed hash table.
        /// </summary>
        public AsyncLazy<PeerTalk.Routing.Dht1> DhtService { get; private set; }

        /// <summary>
        /// Determines latency to a peer.
        /// </summary>
        public AsyncLazy<PeerTalk.Protocols.Ping1> PingService { get; private set; }

        /// <summary>
        /// Fired when a peer is discovered.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="peer"></param>
        /// <remarks>Registers the peer with the <see cref="SwarmService"/>.</remarks>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "<Pending>")]
        private async void OnPeerDiscovered(object sender, Peer peer)
        {
            try
            {
                Swarm swarm = await SwarmService.ConfigureAwait(false);
                _ = swarm.RegisterPeer(peer);
            }
            catch (Exception ex)
            {
                log.Warn("failed to register peer " + peer, ex);
                // eat it, nothing we can do.
            }
        }

        private bool disposed = false; // To detect redundant calls

        /// <summary>
        /// Releases the unmanaged and optionally managed resources.
        /// </summary>
        /// <param name="disposing">
        /// <b>true</b> to release both managed and unmanaged resources; <b>false</b> to release
        /// only unmanaged resources.
        /// </param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposed)
            {
                return;
            }
            disposed = true;

            if (disposing)
            {
                passphrase?.Dispose();
                Stop();
            }
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting
        /// unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
        }
    }
}