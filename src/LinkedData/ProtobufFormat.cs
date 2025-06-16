using PeterO.Cbor;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Ipfs.Engine.LinkedData
{
    /// <summary>
    /// Linked data as a protobuf message.
    /// </summary>
    /// <remarks>This is the original legacy format used by the IPFS <see cref="DagNode"/>.</remarks>
    public class ProtobufFormat : ILinkedDataFormat
    {
        /// <inheritdoc/>
        public CBORObject Deserialise(byte[] data)
        {
            using MemoryStream ms = new(data, false);
            DagNode node = new(ms);
            CBORObject[] links = node.Links
                .Select(link => CBORObject.NewMap()
                    .Add("Cid", CBORObject.NewMap()
                        .Add("/", link.Id.Encode()))
                    .Add("Name", link.Name)
                    .Add("Size", link.Size))
                .ToArray();
            CBORObject cbor = CBORObject.NewMap()
                .Add("data", node.DataBytes)
                .Add("links", links);
            return cbor;
        }

        /// <inheritdoc/>
        public byte[] Serialize(CBORObject data)
        {
            IEnumerable<DagLink> links = data["links"].Values
                .Select(link => new DagLink(
                    link["Name"].AsString(),
                    Cid.Decode(link["Cid"]["/"].AsString()),
                    link["Size"].AsNumber().ToInt64Checked()));
            DagNode node = new(data["data"].GetByteString(), links);
            using MemoryStream ms = new();
            node.Write(ms);
            return ms.ToArray();
        }
    }
}