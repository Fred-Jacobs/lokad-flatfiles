using System;

namespace Lokad.FlatFiles
{
    /// <summary> Options passed to the <see cref="RawFlatFile"/> parser. </summary>
    public sealed class ParserOptions
    {
        private int _maxLineCount = Int32.MaxValue;
        private int _maxCellCount = Int32.MaxValue;

        /// <summary> The maximum number of lines to be read from the input. </summary>
        /// <remarks> Does not include the header. </remarks>
        public int MaxLineCount
        {
            get { return _maxLineCount; }
            set
            {
                if (value < 0) 
                    throw new ArgumentOutOfRangeException("value","MaxLineCount should be >= 0");
                _maxLineCount = value;
            }
        }

        /// <summary> The maximum number of cells to be read from the input. </summary>
        /// <remarks> Does not include the header. </remarks>
        public int MaxCellCount
        {
            get { return _maxCellCount; }
            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException("value", "MaxCellCount should be >= 0");
                _maxCellCount = value;
            }
        }

        private int _readBufferSize = 100*1024*1024;

        /// <summary>
        /// The size of the buffer used for reading. Default is 100MB. If <see cref="MaxLineCount"/>
        /// is set, a recommended value is 2KB + 1KB per line.
        /// </summary>
        public int ReadBufferSize
        {
            get { return _readBufferSize; }
            set
            {
                if (value < 4096)
                    throw new ArgumentOutOfRangeException("value", "ReadBufferSize should be >= 4096");
                _readBufferSize = value;
            }
        }
    }
}
