using System;
using OpenRGB.NET.Utils;

namespace OpenRGB.NET;

internal readonly struct ControllerIdsReader : ISpanReader<uint[]>
{
    public static uint[] ReadFrom(ref SpanReader reader, ProtocolVersion? protocolVersion = default,
        int? index = default, int? outerCount = default)
    {
        if (protocolVersion is not { } protocol)
            throw new ArgumentNullException(nameof(protocolVersion));

        var count = reader.Read<uint>();
        if (count > int.MaxValue)
            throw new InvalidOperationException("OpenRGB returned too many controllers.");

        var ids = new uint[(int)count];
        for (var i = 0; i < ids.Length; i++)
            ids[i] = protocol.Number >= 6 ? reader.Read<uint>() : (uint)i;

        return ids;
    }
}
