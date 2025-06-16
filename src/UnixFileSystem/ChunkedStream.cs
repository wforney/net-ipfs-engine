using Ipfs.CoreApi;
using Ipfs.Engine.Cryptography;
using ProtoBuf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Ipfs.Engine.UnixFileSystem
{
    /// <summary>
    ///   Provides read-only access to a chunked file.
    /// </summary>
    /// <remarks>
    ///   Internal class to support <see cref="FileSystem"/>.
    /// </remarks>
    public class ChunkedStream : Stream
    {
        private class BlockInfo
        {
            public Cid Id;
            public long Position;
        }

        private readonly List<BlockInfo> blocks = [];
        private readonly long fileSize;

        /// <summary>
        ///   Creates a new instance of the <see cref="ChunkedStream"/> class with
        ///   the specified <see cref="IBlockApi"/> and <see cref="DagNode"/>.
        /// </summary>
        /// <param name="blockService"></param>
        /// <param name="keyChain"></param>
        /// <param name="dag"></param>
        public ChunkedStream (IBlockApi blockService, KeyChain keyChain, DagNode dag)
        {
            BlockService = blockService;
            KeyChain = keyChain;
            var links = dag.Links.ToArray();
            var dm = Serializer.Deserialize<DataMessage>(dag.DataStream);
            fileSize = (long)dm.FileSize;
            ulong position = 0;
            for (int i = 0; i < dm.BlockSizes.Length; ++i)
            {
                blocks.Add(new BlockInfo
                {
                    Id = links[i].Id,
                    Position = (long) position
                });
                position += dm.BlockSizes[i];
            }
        }

        private IBlockApi BlockService { get; set; }
        private KeyChain KeyChain { get; set; }

        /// <inheritdoc />
        public override long Length => fileSize;

        /// <inheritdoc />
        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public override bool CanRead => true;

        /// <inheritdoc />
        public override bool CanSeek => true;

        /// <inheritdoc />
        public override bool CanWrite => false;

        /// <inheritdoc />
        public override void Flush() { }

        /// <inheritdoc />
        public override long Position { get; set; }

        /// <inheritdoc />
        public override long Seek(long offset, SeekOrigin origin)
        {
            switch (origin)
            {
                case SeekOrigin.Begin: Position = offset; break;
                case SeekOrigin.Current: Position += offset; break;
                case SeekOrigin.End: Position = Length - offset; break;
            }
            return Position;
        }

        /// <inheritdoc />
        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD002:Avoid problematic synchronous waits", Justification = "<Pending>")]
        public override int Read(byte[] buffer, int offset, int count)
        {
            return ReadAsync(buffer, offset, count).GetAwaiter().GetResult();
        }

        /// <inheritdoc />
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var block = await GetBlockAsync(Position, cancellationToken).ConfigureAwait(false);
            var k = Math.Min(buffer.Length, block.Count);
            if (k > 0)
            {
                block.AsMemory()[..k].CopyTo(buffer);
                Position += k;
            }

            return k;
        }

        private BlockInfo currentBlock;
        private byte[] currentData;
        private async Task<ArraySegment<byte>> GetBlockAsync (long position, CancellationToken cancel)
        {
            if (position >= Length)
            {
                return new ArraySegment<byte>();
            }

            var need = blocks.Last(b => b.Position <= position);
            if (need != currentBlock)
            {
                var stream = await FileSystem.CreateReadStreamAsync(need.Id, BlockService, KeyChain, cancel).ConfigureAwait(false);
                currentBlock = need;
                currentData = new byte[stream.Length];
                for (int i = 0, n; i < stream.Length; i += n)
                {
                    n = await stream.ReadAsync(currentData.AsMemory(i, (int)stream.Length - i), cancel);
                }
            }

            int offset = (int)(position - currentBlock.Position);
            return new ArraySegment<byte>(currentData, offset, currentData.Length - offset);
        }
    }
}
