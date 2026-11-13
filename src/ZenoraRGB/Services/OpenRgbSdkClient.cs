using System.Buffers.Binary;
using System.IO;
using System.Net.Sockets;
using System.Text;
using ZenoraRGB.Models;

namespace ZenoraRGB.Services;

/// <summary>
/// Minimal OpenRGB SDK v6 client. It communicates with the local loopback-only
/// OpenRGB server and never exposes the SDK port to the LAN.
/// </summary>
public sealed class OpenRgbSdkClient : IDisposable
{
    private const uint ProtocolVersion = 6;
    private const int DefaultPort = 6742;
    private const int MaximumPacketBytes = 64 * 1024 * 1024;

    private TcpClient? _tcp;
    private NetworkStream? _stream;

    public bool IsConnected => _tcp?.Connected == true && _stream is not null;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        Dispose();

        var tcp = new TcpClient { NoDelay = true };
        await tcp.ConnectAsync("127.0.0.1", DefaultPort, cancellationToken);
        _tcp = tcp;
        _stream = tcp.GetStream();

        try
        {
            await SendPacketAsync(0, 40, UInt32Payload(ProtocolVersion), cancellationToken);
            var versionPacket = await ReadMatchingPacketAsync(40, cancellationToken);
            var serverVersion = ReadUInt32(versionPacket.Payload, 0);

            if (serverVersion < ProtocolVersion)
                throw new InvalidOperationException(
                    $"OpenRGB SDK protocol {serverVersion} is too old. Zenora RGB requires protocol {ProtocolVersion}.");

            await SendPacketAsync(0, 50, Encoding.UTF8.GetBytes("Zenora RGB\0"), cancellationToken);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public async Task<IReadOnlyList<OpenRgbDevice>> GetDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        await SendPacketAsync(0, 0, Array.Empty<byte>(), cancellationToken);
        var packet = await ReadMatchingPacketAsync(0, cancellationToken);

        if (packet.Payload.Length < 4)
            throw new InvalidDataException("OpenRGB returned an invalid controller count packet.");

        var count = ReadUInt32(packet.Payload, 0);
        if (count > 512)
            throw new InvalidDataException("OpenRGB reported an unexpected number of RGB controllers.");

        var controllerCount = checked((int)count);
        var expectedLength = checked(4 + controllerCount * sizeof(uint));
        if (packet.Payload.Length != expectedLength)
            throw new InvalidDataException(
                $"OpenRGB protocol v6 returned {packet.Payload.Length} bytes for {controllerCount} controllers; expected {expectedLength}.");

        // Protocol v6 uses stable unique controller IDs, not zero-based list indexes.
        var controllerIds = new uint[controllerCount];
        for (var i = 0; i < controllerCount; i++)
            controllerIds[i] = ReadUInt32(packet.Payload, 4 + (i * sizeof(uint)));

        var devices = new List<OpenRgbDevice>(controllerCount);
        foreach (var controllerId in controllerIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await SendPacketAsync(controllerId, 1, UInt32Payload(ProtocolVersion), cancellationToken);
            var dataPacket = await ReadMatchingPacketAsync(1, cancellationToken);
            devices.Add(ParseController(controllerId, dataPacket.Payload));
        }

        return devices;
    }

    public async Task ApplyColorAsync(
        OpenRgbDevice device,
        byte red,
        byte green,
        byte blue,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        if (device.LedCount is < 1 or > ushort.MaxValue)
            throw new InvalidOperationException(
                $"OpenRGB returned an unsupported LED count for {device.Name}.");

        using var payload = new MemoryStream();
        using (var writer = new BinaryWriter(payload, Encoding.UTF8, leaveOpen: true))
        {
            var dataSize = checked(2 + device.LedCount * 4);
            writer.Write((uint)dataSize);
            writer.Write((ushort)device.LedCount);

            for (var i = 0; i < device.LedCount; i++)
            {
                writer.Write(red);
                writer.Write(green);
                writer.Write(blue);
                writer.Write((byte)0);
            }
        }

        await SendPacketAsync(device.Id, 1050, payload.ToArray(), cancellationToken);
    }

    private static OpenRgbDevice ParseController(uint id, byte[] packetPayload)
    {
        if (packetPayload.Length < 4)
            throw new InvalidDataException("OpenRGB returned an empty controller description.");

        var dataSize = ReadUInt32(packetPayload, 0);
        if (dataSize > MaximumPacketBytes || dataSize != packetPayload.Length - 4)
            throw new InvalidDataException("OpenRGB returned a truncated or invalid controller description.");

        using var memory = new MemoryStream(packetPayload, 4, (int)dataSize, writable: false);
        using var reader = new BinaryReader(memory, Encoding.UTF8);

        var deviceType = reader.ReadInt32();
        var name = ReadString16(reader);
        var vendor = ReadString16(reader);
        _ = ReadString16(reader); // description
        _ = ReadString16(reader); // version
        _ = ReadString16(reader); // serial
        _ = ReadString16(reader); // location

        var modeCount = reader.ReadUInt16();
        if (modeCount > 2048)
            throw new InvalidDataException("OpenRGB returned an invalid controller mode count.");
        _ = reader.ReadInt32(); // active mode
        for (var i = 0; i < modeCount; i++)
            SkipMode(reader);

        var zoneCount = reader.ReadUInt16();
        if (zoneCount > 2048)
            throw new InvalidDataException("OpenRGB returned an invalid zone count.");
        for (var i = 0; i < zoneCount; i++)
            SkipZone(reader);

        var ledCount = reader.ReadUInt16();
        for (var i = 0; i < ledCount; i++)
            _ = ReadString16(reader); // protocol 6 removed the internal led_value field

        var colorCount = reader.ReadUInt16();
        SkipBytes(reader, checked(colorCount * 4));

        var alternateNameCount = reader.ReadUInt16(); // protocol 5
        if (alternateNameCount > 4096)
            throw new InvalidDataException("OpenRGB returned an invalid LED display-name count.");
        for (var i = 0; i < alternateNameCount; i++)
            _ = ReadString16(reader);

        _ = reader.ReadUInt32(); // controller flags, protocol 5
        var displayName = ReadString16(reader); // protocol 6
        var configurationLength = reader.ReadUInt32();
        if (configurationLength > 1024 * 1024)
            throw new InvalidDataException("OpenRGB returned an oversized controller configuration.");
        SkipBytes(reader, checked((int)configurationLength));

        if (memory.Position != memory.Length)
            throw new InvalidDataException("OpenRGB controller description contains unparsed fields.");

        if (!string.IsNullOrWhiteSpace(displayName))
            name = displayName;

        return new OpenRgbDevice(id, name, vendor, ledCount, zoneCount, deviceType);
    }

    private static void SkipMode(BinaryReader reader)
    {
        _ = ReadString16(reader);
        // SDK v6 removed mode_value and therefore has eleven 32-bit mode fields.
        SkipBytes(reader, 11 * 4);
        var colorCount = reader.ReadUInt16();
        SkipBytes(reader, checked(colorCount * 4));
    }

    private static void SkipZone(BinaryReader reader)
    {
        _ = ReadString16(reader);
        _ = reader.ReadInt32();  // zone type
        _ = reader.ReadUInt32(); // minimum LEDs
        _ = reader.ReadUInt32(); // maximum LEDs
        _ = reader.ReadUInt32(); // active LED count

        var matrixLength = reader.ReadUInt16();
        SkipBytes(reader, matrixLength);

        var segmentCount = reader.ReadUInt16();
        if (segmentCount > 4096)
            throw new InvalidDataException("OpenRGB returned an invalid zone segment count.");

        for (var i = 0; i < segmentCount; i++)
        {
            _ = ReadString16(reader);
            _ = reader.ReadInt32();  // segment type
            _ = reader.ReadUInt32(); // start index
            _ = reader.ReadUInt32(); // LED count
            var segmentMatrixLength = reader.ReadUInt16();
            SkipBytes(reader, segmentMatrixLength);
            _ = reader.ReadUInt32(); // segment flags, protocol 6
        }

        _ = reader.ReadUInt32(); // zone flags, protocol 5
        _ = reader.ReadInt32();  // zone active mode, protocol 6

        var zoneModeCount = reader.ReadUInt16();
        if (zoneModeCount > 2048)
            throw new InvalidDataException("OpenRGB returned an invalid zone mode count.");
        for (var i = 0; i < zoneModeCount; i++)
            SkipMode(reader);

        _ = ReadString16(reader); // zone display name, protocol 6
    }

    private static string ReadString16(BinaryReader reader)
    {
        var length = reader.ReadUInt16();
        if (length == 0)
            return string.Empty;
        if (length > 32768)
            throw new InvalidDataException("OpenRGB returned an oversized text field.");

        var bytes = reader.ReadBytes(length);
        if (bytes.Length != length)
            throw new EndOfStreamException("OpenRGB returned a truncated text field.");

        return Encoding.UTF8.GetString(bytes).TrimEnd('\0');
    }

    private static void SkipBytes(BinaryReader reader, int count)
    {
        if (count < 0 || count > MaximumPacketBytes)
            throw new InvalidDataException("OpenRGB returned an invalid field length.");

        var skipped = reader.ReadBytes(count);
        if (skipped.Length != count)
            throw new EndOfStreamException("OpenRGB returned a truncated controller field.");
    }

    private async Task SendPacketAsync(
        uint deviceId,
        uint packetId,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        EnsureConnected();

        var header = new byte[16];
        Encoding.ASCII.GetBytes("ORGB").CopyTo(header, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4, 4), deviceId);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8, 4), packetId);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12, 4), checked((uint)payload.Length));

        await _stream!.WriteAsync(header, cancellationToken);
        if (payload.Length > 0)
            await _stream.WriteAsync(payload, cancellationToken);
        await _stream.FlushAsync(cancellationToken);
    }

    private async Task<Packet> ReadMatchingPacketAsync(
        uint expectedPacketId,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 64; attempt++)
        {
            var packet = await ReadPacketAsync(cancellationToken);
            if (packet.PacketId == expectedPacketId)
                return packet;

            if (packet.PacketId is 10 or 51 or 53 or 100 or 101 or 102 or 103)
                continue;

            throw new InvalidDataException(
                $"Unexpected OpenRGB SDK packet {packet.PacketId}; expected {expectedPacketId}.");
        }

        throw new InvalidDataException("OpenRGB sent too many unrelated SDK packets.");
    }

    private async Task<Packet> ReadPacketAsync(CancellationToken cancellationToken)
    {
        EnsureConnected();
        var header = await ReadExactlyAsync(16, cancellationToken);
        if (Encoding.ASCII.GetString(header, 0, 4) != "ORGB")
            throw new InvalidDataException("OpenRGB returned an invalid SDK packet header.");

        var packetId = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8, 4));
        var length = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(12, 4));
        if (length > MaximumPacketBytes)
            throw new InvalidDataException("OpenRGB returned an oversized SDK packet.");

        var payload = length == 0
            ? Array.Empty<byte>()
            : await ReadExactlyAsync((int)length, cancellationToken);

        return new Packet(packetId, payload);
    }

    private async Task<byte[]> ReadExactlyAsync(int length, CancellationToken cancellationToken)
    {
        var bytes = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            var read = await _stream!.ReadAsync(bytes.AsMemory(offset, length - offset), cancellationToken);
            if (read == 0)
                throw new EndOfStreamException("OpenRGB closed the SDK connection.");
            offset += read;
        }
        return bytes;
    }

    private static byte[] UInt32Payload(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        return bytes;
    }

    private static uint ReadUInt32(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));

    private void EnsureConnected()
    {
        if (!IsConnected)
            throw new InvalidOperationException("OpenRGB SDK is not connected.");
    }

    public void Dispose()
    {
        try { _stream?.Dispose(); } catch { /* best-effort cleanup */ }
        try { _tcp?.Dispose(); } catch { /* best-effort cleanup */ }
        _stream = null;
        _tcp = null;
    }

    private sealed record Packet(uint PacketId, byte[] Payload);
}