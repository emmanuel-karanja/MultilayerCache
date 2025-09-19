using Google.Protobuf;
using Google.Protobuf.Reflection;
using System;

public class SampleMessage : IMessage<SampleMessage>
{
    public static MessageParser<SampleMessage> Parser { get; } = new(() => new SampleMessage());

    // For demo purposes, we’ll leave Descriptor null
    public MessageDescriptor Descriptor => null!;

    public string Data { get; set; } = string.Empty;

    public void MergeFrom(SampleMessage message)
    {
        if (message == null) return;
        Data = message.Data;
    }

    public void MergeFrom(CodedInputStream input)
    {
        Data = input.ReadString();
    }

    public void WriteTo(CodedOutputStream output)
    {
        output.WriteString(Data);
    }

    public int CalculateSize()
    {
        return CodedOutputStream.ComputeStringSize(Data);
    }

    public SampleMessage Clone()
    {
        return new SampleMessage { Data = this.Data };
    }

    public bool Equals(SampleMessage? other)
    {
        if (other is null) return false;
        return Data == other.Data;
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as SampleMessage);
    }

    public override int GetHashCode()
    {
        return Data.GetHashCode();
    }

    public override string ToString()
    {
        return $"SampleMessage: {Data}";
    }
}
