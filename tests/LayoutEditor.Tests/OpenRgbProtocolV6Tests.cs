using System.IO;
using System.Text;
using OpenRGB.NET;
using OpenRGB.NET.Utils;
using Xunit;

namespace LayoutEditor.Tests;

public class OpenRgbProtocolV6Tests
{
    [Fact]
    public void ControllerListReadsStableV6Ids()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(2u);
            writer.Write(42u);
            writer.Write(9001u);
        }

        var reader = new SpanReader(stream.ToArray());
        var ids = ControllerIdsReader.ReadFrom(ref reader, ProtocolVersion.V6);

        Assert.Equal(new uint[] { 42, 9001 }, ids);
    }

    [Fact]
    public void DeviceReaderConsumesV6TailFields()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(0u); // data size; ignored by the model reader
            writer.Write((int)DeviceType.Mouse);
            WriteString(writer, "ROCCAT Burst Pro");
            WriteString(writer, "ROCCAT");
            WriteString(writer, "Mouse");
            WriteString(writer, "1.0");
            WriteString(writer, "serial");
            WriteString(writer, "usb");
            writer.Write((ushort)0); // modes
            writer.Write(-1);        // active mode
            writer.Write((ushort)0); // zones
            writer.Write((ushort)0); // LEDs
            writer.Write((ushort)0); // colors
            writer.Write((ushort)0); // alternate LED names (v5)
            writer.Write(0u);        // controller flags (v5)
            WriteString(writer, "ROCCAT Burst Pro");
            WriteUIntString(writer, "{}");
        }

        var reader = new SpanReader(stream.ToArray());
        var device = DeviceReader.ReadFrom(ref reader, ProtocolVersion.V6, 42);

        Assert.Equal(DeviceType.Mouse, device.Type);
        Assert.Equal("ROCCAT Burst Pro", device.Name);
        Assert.Empty(device.Leds);
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        writer.Write((ushort)(bytes.Length + 1));
        writer.Write(bytes);
        writer.Write((byte)0);
    }

    private static void WriteUIntString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        writer.Write((uint)(bytes.Length + 1));
        writer.Write(bytes);
        writer.Write((byte)0);
    }
}
