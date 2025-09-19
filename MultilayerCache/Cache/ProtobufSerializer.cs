using Google.Protobuf;

namespace MultilayerCache.Cache
{
    public static class ProtobufSerializer
    {
        public static byte[] Serialize<T>(T obj) where T : IMessage<T>
        {
            return obj.ToByteArray();
        }

        public static T Deserialize<T>(byte[] data, MessageParser<T> parser) where T : IMessage<T>
        {
            return parser.ParseFrom(data);
        }
    }
}
