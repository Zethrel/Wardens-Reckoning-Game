// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.IO.Compression;

namespace WardensReckoning.World;

/// <summary>
/// One of the export's grids: a whole number for every map cell (height, water, biomes...).
///
/// The generator saves each grid as a grey PNG picture, 8 or 16 bits a pixel,
/// so any program can open it. <see cref="Read"/> turns the picture back into
/// numbers, the same way the generator's north/world_file.py read_grid does.
/// </summary>
public sealed class Grid
{
    public int Width { get; }
    public int Height { get; }

    /// <summary>The numbers row after row: cell (x, y) is Values[y * Width + x].</summary>
    public int[] Values { get; }

    public Grid(int width, int height, int[] values)
    {
        if (values.Length != width * height)
            throw new ArgumentException("a grid of " + width + " x " + height + " needs " + width * height + " values");
        Width = width;
        Height = height;
        Values = values;
    }

    /// <summary>The number in cell (x, y): x to the right, y down, both from 0.</summary>
    public int this[int x, int y] => Values[y * Width + x];

    // Every PNG file starts with these eight bytes.
    static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

    /// <summary>Reads a grey PNG of 8 or 16 bits a pixel; anything else is refused with a WorldFileException.</summary>
    public static Grid Read(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        if (data.Length < 8 || !data.AsSpan(0, 8).SequenceEqual(Signature))
            throw new WorldFileException(path + " is not a PNG file");

        // A PNG is a list of chunks: a length, a four-letter kind, the data, and a checksum.
        // IHDR says how big the picture is; the IDAT chunks together hold the pixels, compressed.
        int width = 0, height = 0, depth = 0, colour = -1, interlace = 0;
        var packed = new MemoryStream();
        int at = 8;
        while (at + 8 <= data.Length)
        {
            int length = BigEndian(data, at);
            string kind = System.Text.Encoding.ASCII.GetString(data, at + 4, 4);
            int start = at + 8;
            if (kind == "IEND")
                break;
            if (kind == "IHDR")
            {
                width = BigEndian(data, start);
                height = BigEndian(data, start + 4);
                depth = data[start + 8];
                colour = data[start + 9];
                interlace = data[start + 12];
            }
            else if (kind == "IDAT")
                packed.Write(data, start, length);
            at = start + length + 4;          // past the data and its four-byte checksum
        }
        if (colour != 0 || (depth != 8 && depth != 16) || interlace != 0)
            throw new WorldFileException(path + " is not a grey grid of 8 or 16 bits");

        packed.Position = 0;
        var raw = new MemoryStream();
        using (var unpack = new ZLibStream(packed, CompressionMode.Decompress))
            unpack.CopyTo(raw);
        byte[] bytes = raw.ToArray();

        int step = depth / 8;                  // bytes a pixel
        int rowBytes = width * step;
        var values = new int[width * height];
        var previous = new byte[rowBytes];
        var line = new byte[rowBytes];
        int read = 0;
        for (int y = 0; y < height; y++)
        {
            // Every row starts with one byte saying how it was "filtered" (stored as differences).
            byte filter = bytes[read];
            Array.Copy(bytes, read + 1, line, 0, rowBytes);
            read += 1 + rowBytes;
            Unfilter(filter, line, previous, step);
            for (int x = 0; x < width; x++)
                values[y * width + x] = step == 1 ? line[x] : (line[2 * x] << 8) | line[2 * x + 1];
            (previous, line) = (line, previous);
        }
        return new Grid(width, height, values);
    }

    /// <summary>
    /// Undoes a row's PNG filter, in place. The generator writes no filter (0), but
    /// a paint program or engine may save the grid again with any of the five.
    /// </summary>
    static void Unfilter(byte filter, byte[] line, byte[] previous, int step)
    {
        for (int i = 0; i < line.Length; i++)
        {
            int left = i >= step ? line[i - step] : 0;
            int up = previous[i];
            int upLeft = i >= step ? previous[i - step] : 0;
            int add = filter switch
            {
                0 => 0,
                1 => left,
                2 => up,
                3 => (left + up) / 2,
                4 => Paeth(left, up, upLeft),
                _ => throw new WorldFileException("unknown PNG row filter " + filter),
            };
            line[i] = (byte)(line[i] + add);
        }
    }

    // The Paeth filter guesses a byte from its left, upper and upper-left neighbours.
    static int Paeth(int left, int up, int upLeft)
    {
        int guess = left + up - upLeft;
        int toLeft = Math.Abs(guess - left), toUp = Math.Abs(guess - up), toUpLeft = Math.Abs(guess - upLeft);
        if (toLeft <= toUp && toLeft <= toUpLeft)
            return left;
        return toUp <= toUpLeft ? up : upLeft;
    }

    static int BigEndian(byte[] data, int at) =>
        (data[at] << 24) | (data[at + 1] << 16) | (data[at + 2] << 8) | data[at + 3];
}
