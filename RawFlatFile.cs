using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Lokad.FlatFiles
{
    /// <summary>
    /// Reads a flat file into a compact in-memory representation.
    /// </summary>
    public sealed class RawFlatFile
    {
        /// <summary>
        /// The number of columns in this file.
        /// </summary>
        public readonly int Columns;

        /// <summary>
        /// A matrix of cells. Line X, column Y can be found at index (X * Columns + Y).
        /// The actual contents of a cell are found in <see cref="Content"/>.
        /// </summary>        
        public IReadOnlyList<int> Cells { get { return _cells; } }
        
        /// <see cref="Cells"/>
        /// <remarks> Only mutable during parsing. </remarks>
        private readonly List<int> _cells = new List<int>();

        /// <summary>
        /// A list of non-empty cells that were beyond the last column on a line.
        /// </summary>
        public IReadOnlyList<TsvCell> UnexpectedCells { get { return _unexpectedCells; } }
        
        /// <see cref="UnexpectedCells"/>
        /// <remarks> Only mutable during parsing. </remarks>
        private readonly List<TsvCell> _unexpectedCells = new List<TsvCell>();

        /// <summary>
        /// The byte contents of the cells referenced by <see cref="Cells"/>.
        /// </summary>
        public readonly IReadOnlyList<byte[]> Content;

        /// <summary>
        /// The separator used for parsing the input file. 
        /// </summary>
        public readonly byte Separator;

        /// <summary> Was the file truncated ? </summary>
        /// <remarks> 
        /// Occurs if <see cref="ParserOptions.MaxCellCount"/> or
        /// <see cref="ParserOptions.MaxLineCount"/> caused data to be discarded.
        /// </remarks>
        public readonly bool IsTruncated;

        /// <summary>
        /// The encoding that was actually found in the file. Not all encodings can be
        /// detected by this class, so this value may be null.
        /// </summary>
        /// <remarks>
        /// If a file encoding was detected, the <see cref="Content"/> cells will have 
        /// been re-encoded to UTF-8.
        /// </remarks>
        public readonly Encoding FileEncoding;

        /// <summary> The trie used to compute the int-to-byte[] mapping. </summary>
        /// <remarks> Only used during parsing, nulled afterwards. </remarks>
        private readonly Trie _trie;

        /// <summary>
        /// Attempt to guess the separator by reading the first line of the buffer.
        /// </summary>
        /// <remarks> Called during parsing only. </remarks>
        private static byte GuessSeparator(InputBuffer buffer, out int columns)
        {
            const byte lf = 0x0A; // \n
            const byte cr = 0x0D; // \r
            const byte space = 0x20; // whitespace

            // Skip to the first non-whitespace, non-newline character (if any)

            for (var i = buffer.Start; i < buffer.End; ++i)
            {
                var b = buffer.Bytes[i];
                if (b == lf || b == cr || b == space) continue;

                buffer.Start = i;
                break;
            }

            // Count the number of occurences of each candidate on the first line

            var candidates = new byte[] {
                0x09, // \t
                0x3B, // ;
                0x2C, // ,
                0x7C, // |
                0x20  // whitespace
            };

            var counts = new int[candidates.Length];

            for (var i = buffer.Start; i < buffer.End; ++i)
            {
                var b = buffer.Bytes[i];

                if (b == lf || b == cr) break;

                for (var c = 0; c < candidates.Length; ++c)
                {
                    if (candidates[c] == b) ++counts[c];
                }
            }

            // Determine the first candidate (in order of priority defined above)
            // that appeared

            for (var c = 0; c < candidates.Length; ++c)
            {
                if (counts[c] > 0)
                {
                    columns = counts[c] + 1;
                    return candidates[c];
                }
            }

            // If no candidate is found, assume 'tab'... warnings will be issued later.

            columns = 1;
            return 0x09;
        }

        /// <summary> Create a raw flat file from external values. </summary>
        /// <remarks>
        /// 
        /// No consistency checks are performed. 
        /// You may call <see cref="ThrowIfInconsistent"/> yourself.
        /// 
        /// For performance reasons, <paramref name="cells"/> and <paramref name="content"/>
        /// are not copied.
        /// 
        /// </remarks>
        public RawFlatFile(
            int columns,
            List<int> cells,
            IReadOnlyList<byte[]> content)
        {
            Columns = columns;
            _cells = cells;
            Content = content;
            
            // Use default values for diagnosis fields
            FileEncoding = null;
            _trie = null;
            Separator = 0x09;
            IsTruncated = false;
        }

        /// <summary> Throws if the internal state is inconsistent. </summary>        
        /// <remarks>
        /// The parsing constructor will never lead to an inconsistent state.
        /// Manually calling the <see cref="RawFlatFile(int, List{int}, IReadOnlyList{byte[]})"/>
        /// constructor, however, may cause inconsistency unless special care is taken
        /// to ensure the following invariants: 
        /// 
        ///  - all values in <see name="Cells"/> are valid indices into <see name="Content"/>
        ///  - cell 0 in <see name="Content"/> is <c>new byte[0]</c>
        ///  - length of <see name="Cells"/> is a multiple of <see name="Columns"/>
        ///  - a value X > 0 may only appear in <see cref="Cells"/> at a higher index than value X-1.
        /// </remarks>
        public void ThrowIfInconsistent()
        {
            if (Content[0].Length != 0)
                throw new Exception("Content[0] should be a new byte[0].");

            if (Columns == 0)
            {
                if (Cells.Count > 0)
                    throw new Exception("No cells allowed if Columns = 0");

                if (Content.Count > 1) // '1' here because of Content[0] == new byte[0]
                    throw new Exception("No content allowed if Columns == 0");

                return;
            }

            if (Cells.Count % Columns != 0)
                throw new Exception(
                    string.Format("Cells.Count = {0} should be a multiple of Columns = {1}.",
                        Cells.Count, Columns));

            var nextNew = 1;
            for (var i = 0; i < Cells.Count; ++i)
            {
                var cell = Cells[i];

                if (cell < 0)
                    throw new Exception(
                        string.Format("Cells[{0}] = {1} < 0.",
                        i, cell));

                if (cell > nextNew)
                    throw new Exception(
                        string.Format("Cells[{0}] = {1} when {2} has not appeared yet.",
                        i, cell, nextNew));

                if (cell == nextNew)
                {
                    nextNew++;
                    if (cell >= Content.Count)
                        throw new Exception(
                            string.Format("Cells[{0}] = {1} >= Content.Count = {2}.",
                            i, cell, Content.Count));
                }
            }
        }

        /// <summary> Parses the input file with the provided options. </summary>
        public RawFlatFile(Stream file, ParserOptions options = null)
        {
            options = options ?? new ParserOptions();

            _trie = new Trie();

            var bufferSize = options.ReadBufferSize;

            const byte quote = 0x22; // "
            const byte lf = 0x0A; // \n
            const byte cr = 0x0D; // \r

            // Load a bunch of source data into a large buffer

            var buffer = new InputBuffer(bufferSize, file);
            var bytes = buffer.Bytes;

            FileEncoding = buffer.FileEncoding;

            // If separator is space, the actual separator should be a tab
            // (it means the data provided used whitespace for headers mistakenly).
            // In that case, use the guessed separator for the first line, then
            // revert to tabs.

            var separator = GuessSeparator(buffer, out Columns);
            SpaceSeparatedHeaders = separator == 0x20;
            Separator = SpaceSeparatedHeaders ? (byte)0x09 : separator;

            var maxCellCountFromLines = options.MaxLineCount >= Int32.MaxValue/Columns - 1
                ? Int32.MaxValue
                : Columns*(options.MaxLineCount + 1); // Include header line

            var maxCellCount = options.MaxCellCount >= Int32.MaxValue - Columns
                ? Int32.MaxValue
                : options.MaxCellCount + Columns; //Include header line

            maxCellCount = Math.Min(maxCellCount, maxCellCountFromLines);

            // Each iteration of this loop attempts to read one cell starting at buffer.Start            
            // It ends when there is no more data available in the buffer or the max cell 
            // count is reached.       
            while ((!buffer.AtEndOfStream || buffer.Length > 0) && _cells.Count < maxCellCount)
            {
                var inQuote = false;
                var nQuotes = 0; // The number of opening quotes in the cell

                // This loop scans the stream forward, looking for a cell terminator.
                for (var i = buffer.Start; ; ++i)
                {
                    if (i >= buffer.End)
                    {
                        // We have reached the end of the buffer. 
                        // The typical behaviour is to abort reading this token and refill the buffer.
                        // BUT if the buffer is already filled, we are dealing with a token that is
                        // way too long: read it. 
                        if (buffer.IsFull)
                        {
                            ExtractCell(bytes, buffer.Start, buffer.End, nQuotes);

                            buffer.Start = buffer.End;
                        }

                        buffer.Refill();

                        break;
                    }

                    var b = bytes[i];

                    // Quote management
                    if (b == quote)
                    {
                        if (i == buffer.Start)
                        {
                            ++nQuotes;
                            inQuote = true;
                        }
                        else if (inQuote)
                        {
                            if (i < buffer.End && bytes[i + 1] == quote)
                            {
                                ++i;
                                ++nQuotes;
                            }
                            else
                            {
                                inQuote = false;
                            }
                        }
                    }

                    if (inQuote) continue;

                    // End of line
                    if (b == cr || b == lf)
                    {
                        ExtractCell(bytes, buffer.Start, i, nQuotes);
                        EndLine();

                        separator = Separator;

                        buffer.Start = i + 1;
                        break;
                    }

                    // Separators
                    if (b == separator)
                    {
                        ExtractCell(bytes, buffer.Start, i, nQuotes);
                        buffer.Start = i + 1;
                        break;
                    }
                }
            }

            // Just in case no "endline" was found, end the line
            EndLine();

            // If the file was empty, fix the number of columns
            if (_cells.Count == 0) Columns = 0;

            IsTruncated = (_cells.Count >= maxCellCount);
            
            Content = _trie.Values;

            // Drop the trie: we don't want to keep the memory contents around
            // so we let the GC take care of it.
            _trie = null;
        }

        /// <summary>
        /// Extracts a cell reference, inserts it into the cell matrix
        /// while keeping track of line sizes and end-of-lines.
        /// </summary>
        /// <remarks> Called during parsing only. </remarks>
        private void ExtractCell(byte[] source, int start, int end, int nQuotes)
        {
            const byte space = 0x20; // whitespace
            const byte quote = 0x22; // "

            if (nQuotes > 0)
            {
                // Only treat a cell as quoted if the last character is the 
                // closing quote. Otherwise, it's an ill-formatted quote that
                // should be treated as non-quoted.
                if (source[end - 1] == quote)
                {
                    start++;
                    end--;

                    // If inner quotes are present, the trie will choke on the buffer
                    // (because double quotes must turn to single quotes), so we 
                    // rewrite the cell in-memory.
                    if (nQuotes > 1)
                    {
                        var j = start;

                        // Skip to after the first double-quote...
                        while (source[j] != quote) j++;
                        j++;

                        // ... and start copying
                        for (var i = j + 1; i < end; ++i, ++j)
                        {
                            source[j] = source[i];
                            if (source[i] == quote) ++i;
                        }

                        end = j;
                    }
                }
            }

            while (start < end && source[start] == space) ++start;
            while (start < end && source[end - 1] == space) --end;

            var cell = _trie.Hash(source, start, end);

            if (cell == 0)
            {
                if (_lineSize == 0)
                {
                    ++_emptyCellsSinceLineStart;
                }
                else 
                {
                    if (_lineSize < Columns) _cells.Add(0);
                    ++_lineSize;                
                }
            }
            else
            {
                while (_emptyCellsSinceLineStart-- > 0)
                {
                    if (_lineSize < Columns) _cells.Add(0);
                    ++_lineSize;
                }

                if (_lineSize < Columns)
                {
                    _cells.Add(cell);
                }
                else
                {
                    _unexpectedCells.Add(
                        new TsvCell((_cells.Count/Columns) - 1, _lineSize, _trie.Values[cell], null));
                }

                ++_lineSize;
            }
        }

        /// <summary>
        /// End the current line. If not all columns have values,
        /// adds empty cells to fill the line. If the line only contains
        /// empty cells, it is discarded.
        /// </summary>
        /// <remarks> Called during parsing only. </remarks>
        private void EndLine()
        {
            if (_lineSize > 0)
            {
                while (_lineSize++ < Columns)
                {
                    _cells.Add(0);
                }
            }

            _lineSize = 0;
            _emptyCellsSinceLineStart = 0;
        }

        /// <summary>
        /// The length of the *unbroken* empty cell streak since
        /// the last call to <see cref="EndLine"/> (or the start of processing).
        /// Zero if a non-empty cell was encountered.
        /// </summary>
        /// <remarks> Used during parsing only. </remarks>
        private int _emptyCellsSinceLineStart;

        /// <summary>
        /// The number of cells on the current line, assuming that there is at least
        /// one non-empty cell found so far.
        /// </summary>
        /// <remarks> Used during parsing only. </remarks>
        private int _lineSize;

        /// <summary> The number of lines, including the header. </summary>
        public int Lines { get { return Columns == 0 ? 0 : _cells.Count/Columns; } }

        /// <summary> The number of lines, not counting the header. </summary>
        public int ContentLines { get { return Math.Max(0, Lines - 1); }}

        /// <summary> Returns the bytes in the specified cell. </summary>
        public byte[] this[int line, int column]
        {
            get { return Content[_cells[line*Columns + column]]; }
        }

        /// <summary>
        /// Were headers separated by whitespace (0x20) instead of <see cref="Separator"/> ? 
        /// </summary>
        public readonly bool SpaceSeparatedHeaders;

        /// <summary> The maximum number of bytes allowed in a cell. </summary>
        public const int MaximalValueLength = 4096;
    }
}
