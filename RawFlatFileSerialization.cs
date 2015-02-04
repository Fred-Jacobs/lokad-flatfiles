using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;

namespace Lokad.FlatFiles
{
    public static class RawFlatFileSerialization
    {
        private const byte VersionNumber = 1;
        
        /// <summary> Writes a raw flat-file to the output. </summary>
        /// <remarks> 
        /// Assumes that <see cref="RawFlatFile.ThrowIfInconsistent"/> does not 
        /// throw on <paramref name="rff"/>.
        /// </remarks>
        public static void Write(this BinaryWriter writer, RawFlatFile rff)
        {
            // Header: version & size information
            writer.Write(VersionNumber);
            writer.Write((ushort)rff.Columns);
            writer.Write((uint)rff.Cells.Count);
            writer.Write((uint)rff.Content.Count);

            // Cell data: integer references to indices in 'content'
            foreach (var cell in rff.Cells)
                WriteInt(writer, cell);

            // Content data: byte arrays of specific sizes.
            foreach (var bytes in rff.Content)
            {
                WriteInt(writer, bytes.Length);
                writer.Write(bytes);
            }
        }

        /// <summary> Reads a raw flat-file written by <see cref="Write"/>. </summary>
        public static RawFlatFile ReadRawFlatFile(this BinaryReader reader)
        {
            // Header: version & size information
            var version = reader.ReadByte();
            if (version != VersionNumber)
                throw new SerializationException(
                    string.Format("Unknown version number {0}.", version));

            var columns = (int)reader.ReadUInt16();
            var cellCount = (int)reader.ReadUInt32();
            var contentCount = (int)reader.ReadUInt32();

            var cells = new List<int>(cellCount);
            for (var i = 0; i < cellCount; ++i)
                cells.Add(ReadInt(reader));

            var content = new byte[contentCount][];
            for (var i = 0; i < contentCount; ++i)
                content[i] = reader.ReadBytes(ReadInt(reader));

            return new RawFlatFile(columns, cells, content);
        }

        /// <summary> Writes an integer using just enough bytes. </summary>
        /// <remarks>
        /// Uses a single byte if 7 bits are enough. 
        /// Uses two bytes if 14 bytes are enough.
        /// Uses three bytes if 21 bytes are enough.
        /// Uses four bytes if 28 bytes are enough.
        /// Uses five bytes above that.
        /// </remarks>
        /// <see cref="ReadInt"/>
        private static void WriteInt(BinaryWriter writer, int value)
        {
            const int topBit = 1 << 7;
            while (value >= topBit)
            {
                writer.Write((byte)(topBit + value%topBit));
                value = value >> 7;
            }

            writer.Write((byte) value);
        }

        /// <summary> Reads an integer written with <see cref="WriteInt"/>. </summary>
        private static int ReadInt(BinaryReader reader)
        {
            const int topBit = 1 << 7;
            var b = topBit;
            var i = 0;

            for (var offset = 0; b >= topBit; offset += 7)
            {
                b = reader.ReadByte();
                i += (b%topBit) << offset;
            }

            return i;
        }
    }
}
