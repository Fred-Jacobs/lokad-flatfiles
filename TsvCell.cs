using System;
using System.Text;

namespace Lokad.FlatFiles
{
    /// <summary>
    /// Represents a cell in a TSV file; used for error reporting.
    /// </summary>
    /// <remarks>
    /// Being part of the error reporting subsystem, this class is
    /// NOT optimized for performance.
    /// </remarks>
    public sealed class TsvCell
    {
        /// <summary>
        /// The line on which the cell appears (zero-indexed).
        /// </summary>
        public readonly int Line;

        /// <summary>
        /// The column on which the cell appears (zero-indexed).
        /// </summary>
        public readonly int Column;

        /// <summary>
        /// If available, the name of the column. May be null.
        /// </summary>
        public readonly string ColumnName;

        /// <summary>
        /// The contents of the cell, attempted as UTF8.
        /// </summary>
        public readonly string Contents;

        public TsvCell(int line, int column, byte[] contents, string columnName)
        {
            Line = line;
            Column = column;
            Contents = Encoding.UTF8.GetString(contents);
            ColumnName = columnName;
        }

        public override string ToString() { return ToString(true); }

        public string ToString(bool withValue)
        {
            if (ColumnName != null)
            {
                if (withValue)
                    return String.Format("'{2}' (column '{1}', line {0})",
                        Line + 1, ColumnName, Contents);

                return String.Format("Column '{1}', line {0}",
                    Line + 1, ColumnName);
            }

            if (withValue)
                return String.Format("'{2}' (column {1}, line {0})",
                        Line + 1, Column, Contents);

            return String.Format("Column {1}, line {0}",
                Line + 1, Column);
        }
    }
}
