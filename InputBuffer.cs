using System;
using System.IO;
using System.Text;

namespace Lokad.FlatFiles
{
    /// <summary>
    /// Stores a buffer of bytes read from the provided stream. 
    /// </summary>
    /// <remarks>
    /// Enables high-throughput operations on the data by reducing
    /// the frequency of context switches required to read more
    /// data from the stream. 
    /// </remarks>
    public sealed class InputBuffer
    {
        /// <summary>
        /// The bytes read from the stream. Only bytes between <see cref="Start"/>
        /// and <see cref="End"/> are valid.
        /// </summary>
        public readonly byte[] Bytes;

        /// <summary>
        /// The index of the first valid byte inside <see cref="Bytes"/>.
        /// The user of this class is explicitly allowed to increment this value
        /// as more bytes are read.
        /// </summary>
        public int Start { get; set; }

        /// <summary>
        /// The first invalid byte after <see cref="Start"/>.
        /// </summary>
        public int End { get; private set; }

        /// <summary>
        /// The number of valid bytes in <see cref="Bytes"/>.
        /// </summary>
        public int Length { get { return End - Start; } }

        /// <summary>
        /// True if the end of the input stream was reached and <see cref="Refill"/>
        /// will not be able to read more bytes.
        /// </summary>
        public bool AtEndOfStream { get; private set; }

        /// <summary>
        /// True if <see cref="Refill"/> has no effect, either because there is no
        /// more data left in the stream or because there is no room in the buffer.
        /// </summary>
        public bool IsFull { get { return Length == Bytes.Length || AtEndOfStream; } }

        /// <summary>
        /// If a file encoding could be determined by reading the BOM (if any),
        /// then the found encoding is stored here.
        /// </summary>
        /// <remarks>
        /// If set, then the buffer will always be encoded as UTF8 (this class
        /// takes care of decoding).
        /// </remarks>
        public readonly Encoding FileEncoding;

        /// <summary>
        /// The stream from which data will be read.
        /// </summary>
        private readonly Stream _source;
        
        public InputBuffer(int size, Stream input)
        {
            if (size < 4)
            {
                throw new ArgumentException("InputBuffer size '" + size + "' is too small.", "size");
            }

            Bytes = new byte[size];

            // Detect UTF-16 encodings or an UTF-8 BOM.
            // ========================================

            End = input.Read(Bytes, 0, 2);

            if (End == 2)
            {
                // Note: if a reencoding stream is used, the output data will be guaranteed to 
                // be UTF-8, but a performance hit will be incurred because of the translation
                // that takes place on every read. 

                if (Bytes[0] == 0xFF && Bytes[1] == 0xFE)
                {
                    // little-endian UTF-16 encoding
                    input = new ReencodingStream(input, 
                        FileEncoding = Encoding.GetEncoding("UTF-16LE"));

                    End = 0;
                }
                else if (Bytes[0] == 0xFE && Bytes[1] == 0xFF)
                {
                    // big-endian UTF-16 encoding
                    input = new ReencodingStream(input, 
                        FileEncoding = Encoding.GetEncoding("UTF-16BE"));

                    End = 0;
                }
                else if (Bytes[0] == 0xEF && Bytes[1] == 0xBB)
                {
                    End += input.Read(Bytes, 2, 1);

                    // Drop the UTF-8 BOM sequence "EF BB BF"
                    if (End == 3 && Bytes[2] == 0xBF)
                    {
                        End = 0;
                        FileEncoding = Encoding.UTF8;
                    }
                }
            }

            _source = input;

            Refill();
        }

        /// <summary>
        /// Sets <see cref="Start"/> to 0, preserving both <see cref="Length"/>
        /// and the segment of <see cref="Bytes"/> between <see cref="Start"/>
        /// and <see cref="End"/>.
        /// </summary>
        private void MoveDataToFront()
        {
            if (Start == 0) return;
            if (Start == End)
            {
                Start = 0;
                End = 0;
                return;
            }

            Array.Copy(Bytes, Start, Bytes, 0, Length);

            End -= Start;
            Start = 0;
        }

        /// <summary>
        /// Read enough data to fill the entire buffer, without
        /// discarding bytes between <see cref="Start"/> and <see cref="End"/>.
        /// </summary>
        public void Refill()
        {
            MoveDataToFront();
            while (Bytes.Length > End && !AtEndOfStream)
            {
                var length = Bytes.Length - End;
                var count = _source.Read(Bytes, End, length);
                End += count;

                AtEndOfStream = (count == 0);
            }
        }

        /// <summary>
        /// A read-only stream used to convert from an UTF-16 text encoding to UTF-8.
        /// Does not support writing or seeking. 
        /// </summary>
        private sealed class ReencodingStream : Stream
        {
            /// <summary> The underlying stream from which data is read. </summary>
            private readonly Stream _stream;

            /// <summary> The number of bytes to be read on each iteration. </summary>
            private const int ReadSize = 4096;

            /// <summary> A buffer used for translation. </summary>
            private readonly byte[] _buffer = new byte[2 * ReadSize];

            private int _bufferEnd;
            private int _bufferStart;

            /// <summary> The encoding from which data is read. </summary>
            private readonly Encoding _encoding;

            public ReencodingStream(Stream input, Encoding source)
            {
                _stream = input;
                _encoding = source;
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                var oldCount = count;

                // First, send any data remaining in the buffer.
                if (_bufferEnd > _bufferStart)
                {
                    var length = Math.Min(count, _bufferEnd - _bufferStart);
                    Array.Copy(_buffer, _bufferStart, buffer, offset, length);
                    _bufferStart += length;
                    offset += length;
                    count -= length;
                }

                if (count == 0) return oldCount;

                // If we are still here, then the buffer is empty. It will be empty
                // every time this loop starts. 
                while (true)
                {
                    // Read data in the input encoding into the buffer
                    // ===============================================

                    _bufferEnd = _stream.Read(_buffer, 0, ReadSize);

                    if (_bufferEnd == 0) return oldCount - count;

                    // Decode and re-encode into the buffer
                    // ====================================

                    var decoded = _encoding.GetString(_buffer, 0, _bufferEnd);

                    _bufferEnd = Encoding.UTF8.GetBytes(decoded, 0, decoded.Length, _buffer, 0);

                    // Copy a portion to the output
                    // ============================

                    var length = Math.Min(count, _bufferEnd);
                    Array.Copy(_buffer, 0, buffer, offset, length);

                    _bufferStart = length;
                    offset += length;
                    count -= length;

                    if (count == 0) return oldCount;
                }
            }

            public override bool CanRead { get { return true; } }
            public override bool CanSeek { get { return false; } }
            public override bool CanWrite { get { return false; } }

            #region Unimplemented Stream members

            // These members of Stream are not implemented, because they
            // are not needed.

            public override void Flush() { throw new InvalidOperationException(); }
            public override long Seek(long offset, SeekOrigin origin) { throw new InvalidOperationException(); }
            public override void SetLength(long value) { throw new InvalidOperationException(); }

            public override void Write(byte[] buffer, int offset, int count) { throw new InvalidOperationException(); }

            public override long Length { get { throw new InvalidOperationException(); } }

            public override long Position
            {
                get { throw new InvalidOperationException(); }
                set { throw new InvalidOperationException(); }
            }

            #endregion
        }
    }
}
