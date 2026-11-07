using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace RB4ShadPS4Installer.Services;

internal static class ParamSfoReader
{
    private const uint Magic = 0x46535000;

    public static IReadOnlyDictionary<string, string> Read(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        var header = reader.ReadBytes(20);
        if (header.Length != 20 || BinaryPrimitives.ReadUInt32LittleEndian(header) != Magic)
            throw new InvalidDataException("The selected param.sfo is not a valid PlayStation SFO file.");

        var keyTableOffset = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8));
        var dataTableOffset = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(12));
        var entryCount = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(16));
        if (entryCount > 4096 || keyTableOffset >= stream.Length || dataTableOffset >= stream.Length)
            throw new InvalidDataException("The param.sfo header is invalid.");

        var entries = new List<(ushort KeyOffset, ushort Format, uint Length, uint DataOffset)>();
        for (var i = 0; i < entryCount; i++)
        {
            var raw = reader.ReadBytes(16);
            if (raw.Length != 16)
                throw new InvalidDataException("The param.sfo entry table is incomplete.");
            entries.Add((
                BinaryPrimitives.ReadUInt16LittleEndian(raw),
                BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(2)),
                BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(4)),
                BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(12))));
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            stream.Position = keyTableOffset + entry.KeyOffset;
            var key = ReadNullTerminated(reader, 256);
            stream.Position = dataTableOffset + entry.DataOffset;
            var safeLength = (int)Math.Min(entry.Length, 1024 * 1024);
            var data = reader.ReadBytes(safeLength);
            result[key] = entry.Format == 0x0404 && data.Length >= 4
                ? BinaryPrimitives.ReadUInt32LittleEndian(data).ToString()
                : Encoding.UTF8.GetString(data).TrimEnd('\0');
        }
        return result;
    }

    private static string ReadNullTerminated(BinaryReader reader, int maxLength)
    {
        var bytes = new List<byte>();
        while (bytes.Count < maxLength)
        {
            var value = reader.ReadByte();
            if (value == 0) break;
            bytes.Add(value);
        }
        return Encoding.UTF8.GetString(bytes.ToArray());
    }
}
