using Ipfs.CoreApi;
using Ipfs.Engine.LinkedData;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PeterO.Cbor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Ipfs.Engine.CoreApi
{
    internal class DagApi(IpfsEngine ipfs) : IDagApi
    {
        [Obsolete]
        private static readonly PODOptions podOptions = new(
            removeIsPrefix: false,
            useCamelCase: false
        );

        public async Task<JObject> GetAsync(
            Cid id,
            CancellationToken cancel = default)
        {
            IDataBlock block = await ipfs.Block.GetAsync(id, cancel).ConfigureAwait(false);
            ILinkedDataFormat format = GetDataFormat(id);
            CBORObject canonical = format.Deserialise(block.DataBytes);
            using MemoryStream ms = new();
            using StreamReader sr = new(ms);
            using JsonTextReader reader = new(sr);
            canonical.WriteJSONTo(ms);
            ms.Position = 0;
            return (JObject)JToken.ReadFrom(reader);
        }

        public async Task<JToken> GetAsync(
            string path,
            CancellationToken cancel = default)
        {
            if (path.StartsWith("/ipfs/"))
            {
                path = path[6..];
            }

            string[] parts = [.. path.Split('/').Where(p => p.Length > 0)];
            if (parts.Length == 0)
            {
                throw new ArgumentException($"Cannot resolve '{path}'.");
            }

            JToken token = await GetAsync(Cid.Decode(parts[0]), cancel).ConfigureAwait(false);
            foreach (string child in parts.Skip(1))
            {
                token = ((JObject)token)[child];
                if (token == null)
                {
                    throw new Exception($"Missing component '{child}'.");
                }
            }

            return token;
        }

        public async Task<T> GetAsync<T>(
            Cid id,
            CancellationToken cancel = default)
        {
            IDataBlock block = await ipfs.Block.GetAsync(id, cancel).ConfigureAwait(false);
            ILinkedDataFormat format = GetDataFormat(id);
            CBORObject canonical = format.Deserialise(block.DataBytes);

            // CBOR does not support serialisation to another Type see
            // https://github.com/peteroupc/CBOR/issues/12. So, convert to JSON and use Newtonsoft
            // to deserialise.
            return JObject
                .Parse(canonical.ToJSONString())
                .ToObject<T>();
        }

        public async Task<Cid> PutAsync(
            JObject data,
            string contentType = "dag-cbor",
            string multiHash = MultiHash.DefaultAlgorithmName,
            string encoding = MultiBase.DefaultAlgorithmName,
            bool pin = true,
            CancellationToken cancel = default)
        {
            using MemoryStream ms = new();
            using StreamWriter sw = new(ms);
            using JsonTextWriter writer = new(sw);
            await data.WriteToAsync(writer);
            writer.Flush();
            ms.Position = 0;
            ILinkedDataFormat format = GetDataFormat(contentType);
            byte[] block = format.Serialize(CBORObject.ReadJSON(ms));
            return await ipfs.Block.PutAsync(block, contentType, multiHash, encoding, pin, cancel).ConfigureAwait(false);
        }

        public async Task<Cid> PutAsync(Stream data,
            string contentType = "dag-cbor",
            string multiHash = MultiHash.DefaultAlgorithmName,
            string encoding = MultiBase.DefaultAlgorithmName,
            bool pin = true,
            CancellationToken cancel = default)
        {
            ILinkedDataFormat format = GetDataFormat(contentType);
            byte[] block = format.Serialize(CBORObject.Read(data));
            return await ipfs.Block.PutAsync(block, contentType, multiHash, encoding, pin, cancel).ConfigureAwait(false);
        }

        [Obsolete]
        public async Task<Cid> PutAsync(object data,
            string contentType = "dag-cbor",
            string multiHash = MultiHash.DefaultAlgorithmName,
            string encoding = MultiBase.DefaultAlgorithmName,
            bool pin = true,
            CancellationToken cancel = default)
        {
            ILinkedDataFormat format = GetDataFormat(contentType);
            byte[] block = format.Serialize(CBORObject.FromObject(data, podOptions));
            return await ipfs.Block.PutAsync(block, contentType, multiHash, encoding, pin, cancel).ConfigureAwait(false);
        }

        private static ILinkedDataFormat GetDataFormat(Cid id) => IpldRegistry.Formats.TryGetValue(id.ContentType, out ILinkedDataFormat format)
                ? format
                : throw new KeyNotFoundException($"Unknown IPLD format '{id.ContentType}'.");

        private static ILinkedDataFormat GetDataFormat(string contentType) => IpldRegistry.Formats.TryGetValue(contentType, out ILinkedDataFormat format)
                ? format
                : throw new KeyNotFoundException($"Unknown IPLD format '{contentType}'.");
    }
}