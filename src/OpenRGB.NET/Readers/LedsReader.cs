using OpenRGB.NET.Utils;

namespace OpenRGB.NET;

internal readonly struct LedsReader : ISpanReader<Led[]>
{
    public static Led[] ReadFrom(ref SpanReader reader, ProtocolVersion? protocolVersion = default, int? index = default, int? outerCount = default)
    {
        if (protocolVersion is not { } protocol)
            throw new System.ArgumentNullException(nameof(protocolVersion));

        var ledCount = reader.Read<ushort>();

        var leds = new Led[ledCount];

        for (var i = 0; i < ledCount; i++)
        {
            var name = reader.ReadLengthAndString();
            var value = protocol.Number < 6 ? reader.Read<uint>() : 0;

            leds[i] = new Led(i, name, value);
        }

        return leds;
    }
}
