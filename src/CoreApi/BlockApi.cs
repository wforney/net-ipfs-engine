using Common.Logging;
using Ipfs.CoreApi;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Ipfs.Engine.CoreApi
{
    internal class BlockApi(IpfsEngine ipfs) : IBlockApi
    {
        private static readonly DataBlock emptyDirectory = new()
        {
            DataBytes = ObjectApi.EmptyDirectory.ToArray(),
            Id = ObjectApi.EmptyDirectory.Id,
            Size = ObjectApi.EmptyDirectory.ToArray().Length
        };

        private static readonly DataBlock emptyNode = new()
        {
            DataBytes = ObjectApi.EmptyNode.ToArray(),
            Id = ObjectApi.EmptyNode.Id,
            Size = ObjectApi.EmptyNode.ToArray().Length
        };

        private static readonly ILog log = LogManager.GetLogger<BlockApi>();
        private FileStore<Cid, DataBlock> store;

        public FileStore<Cid, DataBlock> Store
        {
            get
            {
                if (store is null)
                {
                    string folder = Path.Combine(ipfs.Options.Repository.Folder, "blocks");
                    if (!Directory.Exists(folder))
                    {
                        _ = Directory.CreateDirectory(folder);
                    }

                    store = new FileStore<Cid, DataBlock>
                    {
                        Folder = folder,
                        NameToKey = (cid) => cid.Hash.ToBase32(),
                        KeyToName = (key) => new MultiHash(key.FromBase32()),
                        Serialize = async (stream, cid, block, cancel) =>
                        {
                            await stream.WriteAsync(block.DataBytes.AsMemory(0, block.DataBytes.Length), cancel).ConfigureAwait(false);
                        },
                        Deserialize = async (stream, cid, cancel) =>
                        {
                            DataBlock block = new()
                            {
                                Id = cid,
                                Size = stream.Length
                            };
                            block.DataBytes = new byte[block.Size];
                            for (int i = 0, n; i < block.Size; i += n)
                            {
                                n = await stream.ReadAsync(block.DataBytes.AsMemory(i, (int)block.Size - i), cancel).ConfigureAwait(false);
                            }

                            return block;
                        }
                    };
                }
                return store;
            }
        }

        public async Task<IDataBlock> GetAsync(Cid id, CancellationToken cancel = default)
        {
            // Hack for empty object and empty directory object
            if (id == emptyDirectory.Id)
            {
                return emptyDirectory;
            }

            if (id == emptyNode.Id)
            {
                return emptyNode;
            }

            // If identity hash, then CID has the content.
            if (id.Hash.IsIdentityHash)
            {
                return new DataBlock
                {
                    DataBytes = id.Hash.Digest,
                    Id = id,
                    Size = id.Hash.Digest.Length
                };
            }

            // Check the local filesystem for the block.
            DataBlock block = await Store.TryGetAsync(id, cancel).ConfigureAwait(false);
            if (block != null)
            {
                return block;
            }

            // Query the network, via DHT, for peers that can provide the content. As a provider
            // peer is found, it is connected to and the bitswap want lists are exchanged. Hopefully
            // the provider will then send the block to us via bitswap and the get task will finish.
            using CancellationTokenSource queryCancel = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            System.Runtime.CompilerServices.ConfiguredTaskAwaitable<IDataBlock> bitswapGet = ipfs.Bitswap.GetAsync(id, queryCancel.Token).ConfigureAwait(false);
            PeerTalk.Routing.Dht1 dht = await ipfs.DhtService;
            Task<IEnumerable<Peer>> _ = dht.FindProvidersAsync(
                id: id,
                limit: 20, // TODO: remove this
                cancel: queryCancel.Token,
                action: (peer) => { System.Runtime.CompilerServices.ConfiguredTaskAwaitable __ = ProviderFoundAsync(peer, queryCancel.Token).ConfigureAwait(false); }
            );

            IDataBlock got = await bitswapGet;
            log.Debug("bitswap got the block");

            await queryCancel.CancelAsync(); // stop the network query.
            return got;
        }

        public async Task<Cid> PutAsync(
            byte[] data,
            string contentType = Cid.DefaultContentType,
            string multiHash = MultiHash.DefaultAlgorithmName,
            string encoding = MultiBase.DefaultAlgorithmName,
            bool pin = false,
            CancellationToken cancel = default)
        {
            if (data.Length > ipfs.Options.Block.MaxBlockSize)
            {
                throw new ArgumentOutOfRangeException("data.Length", $"Block length can not exceed {ipfs.Options.Block.MaxBlockSize}.");
            }

            // Small enough for an inline CID?
            if (ipfs.Options.Block.AllowInlineCid && data.Length <= ipfs.Options.Block.InlineCidLimit)
            {
                return new Cid
                {
                    ContentType = contentType,
                    Hash = MultiHash.ComputeHash(data, "identity")
                };
            }

            // CID V1 encoding defaulting to base32 which is not the multibase default.
            Cid cid = new()
            {
                ContentType = contentType,
                Hash = MultiHash.ComputeHash(data, multiHash)
            };
            if (encoding != "base58btc")
            {
                cid.Encoding = encoding;
            }
            DataBlock block = new()
            {
                DataBytes = data,
                Id = cid,
                Size = data.Length
            };
            if (await Store.ExistsAsync(cid, cancel).ConfigureAwait(false))
            {
                log.DebugFormat("Block '{0}' already present", cid);
            }
            else
            {
                await Store.PutAsync(cid, block, cancel).ConfigureAwait(false);
                if (ipfs.IsStarted)
                {
                    await ipfs.Dht.ProvideAsync(cid, advertise: false, cancel: cancel).ConfigureAwait(false);
                }
                log.DebugFormat("Added block '{0}'", cid);
            }

            // Inform the Bitswap service.
            _ = (await ipfs.BitswapService.ConfigureAwait(false)).Found(block);

            // To pin or not.
            _ = pin
                ? await ipfs.Pin.AddAsync(cid, recursive: false, cancel: cancel).ConfigureAwait(false)
                : await ipfs.Pin.RemoveAsync(cid, recursive: false, cancel: cancel).ConfigureAwait(false);

            return cid;
        }

        public async Task<Cid> PutAsync(
            Stream data,
            string contentType = Cid.DefaultContentType,
            string multiHash = MultiHash.DefaultAlgorithmName,
            string encoding = MultiBase.DefaultAlgorithmName,
            bool pin = false,
            CancellationToken cancel = default)
        {
            using MemoryStream ms = new();
            await data.CopyToAsync(ms, cancel).ConfigureAwait(false);
            return await PutAsync(ms.ToArray(), contentType, multiHash, encoding, pin, cancel).ConfigureAwait(false);
        }

        public async Task<Cid> RemoveAsync(Cid id, bool ignoreNonexistent = false, CancellationToken cancel = default)
        {
            if (id.Hash.IsIdentityHash)
            {
                return id;
            }
            if (await Store.ExistsAsync(id, cancel).ConfigureAwait(false))
            {
                await Store.RemoveAsync(id, cancel).ConfigureAwait(false);
                _ = await ipfs.Pin.RemoveAsync(id, recursive: false, cancel: cancel).ConfigureAwait(false);
                return id;
            }
            return ignoreNonexistent ? null : throw new KeyNotFoundException($"Block '{id.Encode()}' does not exist.");
        }

        public async Task<IDataBlock> StatAsync(Cid id, CancellationToken cancel = default)
        {
            if (id.Hash.IsIdentityHash)
            {
                return await GetAsync(id, cancel).ConfigureAwait(false);
            }

            IDataBlock block = null;
            long? length = await Store.LengthAsync(id, cancel).ConfigureAwait(false);
            if (length.HasValue)
            {
                block = new DataBlock
                {
                    Id = id,
                    Size = length.Value
                };
            }

            return block;
        }

        private async Task ProviderFoundAsync(Peer peer, CancellationToken cancel)
        {
            if (cancel.IsCancellationRequested)
            {
                return;
            }

            log.Debug($"Connecting to provider {peer.Id}");
            PeerTalk.Swarm swarm = await ipfs.SwarmService.ConfigureAwait(false);
            try
            {
                _ = await swarm.ConnectAsync(peer, cancel).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                log.Warn($"Connection to provider {peer.Id} failed, {e.Message}");
            }
        }
    }
}