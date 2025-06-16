using Common.Logging;
using ICSharpCode.SharpZipLib.Tar;
using Ipfs.CoreApi;
using Ipfs.Engine.UnixFileSystem;
using ProtoBuf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Ipfs.Engine.CoreApi
{
    internal class FileSystemApi(IpfsEngine ipfs) : IFileSystemApi
    {
        private static readonly int DefaultLinksPerBlock = 174;
        private static readonly ILog log = LogManager.GetLogger<FileSystemApi>();

        public async Task<IFileSystemNode> AddAsync(
            Stream stream,
            string name,
            AddFileOptions options,
            CancellationToken cancel)
        {
            options ??= new AddFileOptions();

            // TODO: various options
            if (options.Trickle)
            {
                throw new NotImplementedException("Trickle");
            }

            IBlockApi blockService = GetBlockService(options);
            Cryptography.KeyChain keyChain = await ipfs.KeyChainAsync(cancel).ConfigureAwait(false);

            SizeChunker chunker = new();
            List<FileSystemNode> nodes = await chunker.ChunkAsync(stream, name, options, blockService, keyChain, cancel).ConfigureAwait(false);

            // Multiple nodes for the file?
            FileSystemNode node = await BuildTreeAsync(nodes, options, cancel);

            // Wrap in directory?
            if (options.Wrap)
            {
                IFileSystemLink link = node.ToLink(name);
                IFileSystemLink[] wlinks = [link];
                node = await CreateDirectoryAsync(wlinks, options, cancel).ConfigureAwait(false);
            }
            else
            {
                node.Name = name;
            }

            // Advertise the root node.
            if (options.Pin && ipfs.IsStarted)
            {
                await ipfs.Dht.ProvideAsync(node.Id, advertise: true, cancel: cancel).ConfigureAwait(false);
            }

            // Return the file system node.
            return node;
        }

        public async Task<IFileSystemNode> AddDirectoryAsync(
            string path,
            bool recursive = true,
            AddFileOptions options = default,
            CancellationToken cancel = default)
        {
            options ??= new AddFileOptions();
            options.Wrap = false;

            // Add the files and sub-directories.
            path = Path.GetFullPath(path);
            IEnumerable<Task<IFileSystemNode>> files = Directory
                .EnumerateFiles(path)
                .OrderBy(s => s)
                .Select(p => AddFileAsync(p, options, cancel));
            if (recursive)
            {
                IEnumerable<Task<IFileSystemNode>> folders = Directory
                    .EnumerateDirectories(path)
                    .OrderBy(s => s)
                    .Select(dir => AddDirectoryAsync(dir, recursive, options, cancel));
                files = files.Union(folders);
            }
            IFileSystemNode[] nodes = await Task.WhenAll(files).ConfigureAwait(false);

            // Create the DAG with links to the created files and sub-directories
            IFileSystemLink[] links = [.. nodes.Select(node => node.ToLink())];
            FileSystemNode fsn = await CreateDirectoryAsync(links, options, cancel).ConfigureAwait(false);
            fsn.Name = Path.GetFileName(path);
            return fsn;
        }

        public async Task<IFileSystemNode> AddFileAsync(
                            string path,
            AddFileOptions options = default,
            CancellationToken cancel = default)
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return await AddAsync(stream, Path.GetFileName(path), options, cancel).ConfigureAwait(false);
        }

        public async Task<IFileSystemNode> AddTextAsync(
            string text,
            AddFileOptions options = default,
            CancellationToken cancel = default)
        {
            using MemoryStream ms = new(Encoding.UTF8.GetBytes(text), false);
            return await AddAsync(ms, "", options, cancel).ConfigureAwait(false);
        }

        [Obsolete]
        public async Task<Stream> GetAsync(string path, bool compress = false, CancellationToken cancel = default)
        {
            Cid cid = await ipfs.ResolveIpfsPathToCidAsync(path, cancel).ConfigureAwait(false);
            MemoryStream ms = new();
            using (TarOutputStream tarStream = new(ms, 1))
            using (TarArchive archive = TarArchive.CreateOutputTarArchive(tarStream))
            {
                archive.IsStreamOwner = false;
                await AddTarNodeAsync(cid, cid.Encode(), tarStream, cancel).ConfigureAwait(false);
            }
            ms.Position = 0;
            return ms;
        }

        public async Task<IFileSystemNode> ListFileAsync(string path, CancellationToken cancel = default)
        {
            Cid cid = await ipfs.ResolveIpfsPathToCidAsync(path, cancel).ConfigureAwait(false);
            IDataBlock block = await ipfs.Block.GetAsync(cid, cancel).ConfigureAwait(false);

            // TODO: A content-type registry should be used.
            if (cid.ContentType == "dag-pb")
            {
                // fall thru
            }
            else
            {
                return cid.ContentType == "raw"
                    ? new FileSystemNode
                    {
                        Id = cid,
                        Size = block.Size
                    }
                    : cid.ContentType == "cms"
                                    ? (IFileSystemNode)new FileSystemNode
                                    {
                                        Id = cid,
                                        Size = block.Size
                                    }
                                    : throw new NotSupportedException($"Cannot read content type '{cid.ContentType}'.");
            }

            DagNode dag = new(block.DataStream);
            DataMessage dm = Serializer.Deserialize<DataMessage>(dag.DataStream);
            FileSystemNode fsn = new()
            {
                Id = cid,
                Links = [.. dag.Links
                    .Select(l => new FileSystemLink
                    {
                        Id = l.Id,
                        Name = l.Name,
                        Size = l.Size
                    })],
                IsDirectory = dm.Type == DataType.Directory,
                Size = (long)(dm.FileSize ?? 0)
            };

            return fsn;
        }

        public async Task<string> ReadAllTextAsync(string path, CancellationToken cancel = default)
        {
            using Stream data = await ReadFileAsync(path, cancel).ConfigureAwait(false);
            using StreamReader text = new(data);
            return await text.ReadToEndAsync(cancel).ConfigureAwait(false);
        }

        public async Task<Stream> ReadFileAsync(string path, CancellationToken cancel = default)
        {
            Cid cid = await ipfs.ResolveIpfsPathToCidAsync(path, cancel).ConfigureAwait(false);
            Cryptography.KeyChain keyChain = await ipfs.KeyChainAsync(cancel).ConfigureAwait(false);
            return await FileSystem.CreateReadStreamAsync(cid, ipfs.Block, keyChain, cancel).ConfigureAwait(false);
        }

        public async Task<Stream> ReadFileAsync(string path, long offset, long count = 0, CancellationToken cancel = default)
        {
            Stream stream = await ReadFileAsync(path, cancel).ConfigureAwait(false);
            return new SlicedStream(stream, offset, count);
        }

        private async Task AddTarNodeAsync(Cid cid, string name, TarOutputStream tar, CancellationToken cancel)
        {
            IDataBlock block = await ipfs.Block.GetAsync(cid, cancel).ConfigureAwait(false);
            DataMessage dm = new() { Type = DataType.Raw };
            DagNode dag = null;

            if (cid.ContentType == "dag-pb")
            {
                dag = new DagNode(block.DataStream);
                dm = Serializer.Deserialize<DataMessage>(dag.DataStream);
            }
            TarEntry entry = new(new TarHeader());
            TarHeader header = entry.TarHeader;
            header.Mode = 0x1ff; // 777 in octal
            header.LinkName = string.Empty;
            header.UserName = string.Empty;
            header.GroupName = string.Empty;
            header.Version = "00";
            header.Name = name;
            header.DevMajor = 0;
            header.DevMinor = 0;
            header.UserId = 0;
            header.GroupId = 0;
            header.ModTime = DateTime.Now;

            if (dm.Type == DataType.Directory)
            {
                header.TypeFlag = TarHeader.LF_DIR;
                header.Size = 0;
                tar.PutNextEntry(entry);
                tar.CloseEntry();
            }
            else // Must be a file
            {
                Stream content = await ReadFileAsync(cid, cancel).ConfigureAwait(false);
                header.TypeFlag = TarHeader.LF_NORMAL;
                header.Size = content.Length;
                tar.PutNextEntry(entry);
                await content.CopyToAsync(tar);
                tar.CloseEntry();
            }

            // Recurse over files and subdirectories
            if (dm.Type == DataType.Directory)
            {
                foreach (IMerkleLink link in dag.Links)
                {
                    await AddTarNodeAsync(link.Id, $"{name}/{link.Name}", tar, cancel).ConfigureAwait(false);
                }
            }
        }

        private async Task<FileSystemNode> BuildTreeAsync(
                                                            IEnumerable<FileSystemNode> nodes,
            AddFileOptions options,
            CancellationToken cancel)
        {
            if (nodes.Count() == 1)
            {
                return nodes.First();
            }

            // Bundle DefaultLinksPerBlock links into a block.
            List<FileSystemNode> tree = [];
            for (int i = 0; true; ++i)
            {
                IEnumerable<FileSystemNode> bundle = nodes
                    .Skip(DefaultLinksPerBlock * i)
                    .Take(DefaultLinksPerBlock);
                if (bundle.Count() == 0)
                {
                    break;
                }
                FileSystemNode node = await BuildTreeNodeAsync(bundle, options, cancel);
                tree.Add(node);
            }
            return await BuildTreeAsync(tree, options, cancel);
        }

        private async Task<FileSystemNode> BuildTreeNodeAsync(
            IEnumerable<FileSystemNode> nodes,
            AddFileOptions options,
            CancellationToken cancel)
        {
            IBlockApi blockService = GetBlockService(options);

            // Build the DAG that contains all the file nodes.
            IFileSystemLink[] links = nodes.Select(n => n.ToLink()).ToArray();
            ulong fileSize = (ulong)nodes.Sum(n => n.Size);
            long dagSize = nodes.Sum(n => n.DagSize);
            DataMessage dm = new()
            {
                Type = DataType.File,
                FileSize = fileSize,
                BlockSizes = nodes.Select(n => (ulong)n.Size).ToArray()
            };
            MemoryStream pb = new();
            ProtoBuf.Serializer.Serialize<DataMessage>(pb, dm);
            DagNode dag = new(pb.ToArray(), links, options.Hash);

            // Save it.
            dag.Id = await blockService.PutAsync(
                data: dag.ToArray(),
                multiHash: options.Hash,
                encoding: options.Encoding,
                pin: options.Pin,
                cancel: cancel).ConfigureAwait(false);

            return new FileSystemNode
            {
                Id = dag.Id,
                Size = (long)dm.FileSize,
                DagSize = dagSize + dag.Size,
                Links = links
            };
        }

        private async Task<FileSystemNode> CreateDirectoryAsync(IEnumerable<IFileSystemLink> links, AddFileOptions options, CancellationToken cancel)
        {
            DataMessage dm = new() { Type = DataType.Directory };
            MemoryStream pb = new();
            ProtoBuf.Serializer.Serialize<DataMessage>(pb, dm);
            DagNode dag = new(pb.ToArray(), links, options.Hash);

            // Save it.
            Cid cid = await GetBlockService(options).PutAsync(
                data: dag.ToArray(),
                multiHash: options.Hash,
                encoding: options.Encoding,
                pin: options.Pin,
                cancel: cancel).ConfigureAwait(false);

            return new FileSystemNode
            {
                Id = cid,
                Links = links,
                IsDirectory = true
            };
        }

        private IBlockApi GetBlockService(AddFileOptions options)
        {
            return options.OnlyHash
                ? new HashOnlyBlockService()
                : ipfs.Block;
        }

        /// <summary>
        /// A Block service that only computes the block's hash.
        /// </summary>
        private class HashOnlyBlockService : IBlockApi
        {
            public Task<IDataBlock> GetAsync(Cid id, CancellationToken cancel = default)
            {
                throw new NotImplementedException();
            }

            public Task<Cid> PutAsync(
                byte[] data,
                string contentType = Cid.DefaultContentType,
                string multiHash = MultiHash.DefaultAlgorithmName,
                string encoding = MultiBase.DefaultAlgorithmName,
                bool pin = false,
                CancellationToken cancel = default)
            {
                Cid cid = new()
                {
                    ContentType = contentType,
                    Encoding = encoding,
                    Hash = MultiHash.ComputeHash(data, multiHash),
                    Version = (contentType == "dag-pb" && multiHash == "sha2-256") ? 0 : 1
                };
                return Task.FromResult(cid);
            }

            public Task<Cid> PutAsync(
                Stream data,
                string contentType = Cid.DefaultContentType,
                string multiHash = MultiHash.DefaultAlgorithmName,
                string encoding = MultiBase.DefaultAlgorithmName,
                bool pin = false,
                CancellationToken cancel = default)
            {
                throw new NotImplementedException();
            }

            public Task<Cid> RemoveAsync(Cid id, bool ignoreNonexistent = false, CancellationToken cancel = default)
            {
                throw new NotImplementedException();
            }

            public Task<IDataBlock> StatAsync(Cid id, CancellationToken cancel = default)
            {
                throw new NotImplementedException();
            }
        }
    }
}