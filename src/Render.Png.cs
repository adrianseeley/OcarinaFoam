using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

public static partial class Render
{
    static void WritePng(string path, byte[] bgr, int width)
    {
        int height = bgr.Length / (width * 3);
        using var file = File.Create(path);
        file.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        void Chunk(string type, byte[] data)
        {
            byte[] tag = Encoding.ASCII.GetBytes(type);
            Span<byte> number = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
            file.Write(number); file.Write(tag); file.Write(data);
            uint crc = 0xffffffff;
            void Update(byte value)
            {
                crc ^= value;
                for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
            }
            foreach (byte value in tag) Update(value);
            foreach (byte value in data) Update(value);
            BinaryPrimitives.WriteUInt32BigEndian(number, ~crc);
            file.Write(number);
        }
        byte[] header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; header[9] = 2;
        Chunk("IHDR", header);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            var row = new byte[width * 3 + 1];
            // PNG Sub filter: lossless byte differences from the preceding RGB pixel.
            row[0] = 1;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                    for (int c = 0; c < 3; c++)
                    {
                        int source = (y * width + x) * 3 + 2 - c;
                        row[1 + x * 3 + c] = unchecked((byte)(bgr[source] - (x > 0 ? bgr[source - 3] : 0)));
                    }
                zlib.Write(row);
            }
        }
        Chunk("IDAT", compressed.ToArray());
        Chunk("IEND", Array.Empty<byte>());
    }
}
