using System;
using System.Buffers.Binary;
using System.Text;

namespace LocalClip.Core.Rules;

public static class NotificationWaveRules
{
    public const int MaximumBytes = 1_048_576;
    public const double MaximumSeconds = 3;

    public static string DurationText(double seconds) =>
        $"{Math.Round(double.IsFinite(seconds) ? Math.Clamp(seconds, 0, 86400) : 0, MidpointRounding.AwayFromZero):0} sec";

    // Validate the actual RIFF payload, not just a filename or codec header.
    public static bool IsValid(ReadOnlySpan<byte> wave) => TryGetDuration(wave, out _);

    public static bool TryGetDuration(ReadOnlySpan<byte> wave, out double seconds)
    {
        seconds = 0;
        if (wave.Length < 44 || wave.Length > MaximumBytes ||
            Encoding.ASCII.GetString(wave[..4]) != "RIFF" ||
            Encoding.ASCII.GetString(wave.Slice(8, 4)) != "WAVE" ||
            BinaryPrimitives.ReadUInt32LittleEndian(wave.Slice(4, 4)) != wave.Length - 8) return false;
        int offset = 12;
        uint rate = 0, bytesPerSecond = 0;
        ushort alignment = 0;
        bool format = false, data = false;
        while (offset + 8 <= wave.Length)
        {
            string name = Encoding.ASCII.GetString(wave.Slice(offset, 4));
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(wave.Slice(offset + 4, 4));
            offset += 8;
            if (length > wave.Length - offset) return false;
            if (name == "fmt ")
            {
                if (format || length != 16) return false;
                var fmt = wave.Slice(offset, 16);
                ushort codec = BinaryPrimitives.ReadUInt16LittleEndian(fmt);
                ushort channels = BinaryPrimitives.ReadUInt16LittleEndian(fmt.Slice(2));
                rate = BinaryPrimitives.ReadUInt32LittleEndian(fmt.Slice(4));
                bytesPerSecond = BinaryPrimitives.ReadUInt32LittleEndian(fmt.Slice(8));
                alignment = BinaryPrimitives.ReadUInt16LittleEndian(fmt.Slice(12));
                ushort bits = BinaryPrimitives.ReadUInt16LittleEndian(fmt.Slice(14));
                if (codec != 1 || channels is < 1 or > 2 || bits is not (8 or 16) ||
                    rate is < 8000 or > 48000 || alignment != channels * bits / 8 ||
                    bytesPerSecond != rate * alignment) return false;
                format = true;
            }
            if (name == "data")
            {
                if (!format || data || length == 0 || length % alignment != 0 ||
                    length / (double)bytesPerSecond > MaximumSeconds) return false;
                seconds = length / (double)bytesPerSecond;
                data = true;
            }
            offset += (int)length + (int)(length % 2);
        }
        return format && data && offset == wave.Length;
    }
}
