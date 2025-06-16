using System.IO;
using System.Runtime.Serialization;

namespace Ipfs.Engine.CoreApi
{
    [DataContract]
    internal class DataBlock : IDataBlock
    {
        [DataMember]
        public byte[] DataBytes { get; set; }

        public Stream DataStream => new MemoryStream(DataBytes, false);

        [DataMember]
        public Cid Id { get; set; }

        [DataMember]
        public long Size { get; set; }
    }
}