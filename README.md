Compresses a [flat data file](https://en.wikipedia.org/wiki/Comma-separated_values) (TSV, CSV) 
using [Perfect Hash Function](https://en.wikipedia.org/wiki/Perfect_hash_function). See also the [Java port](https://github.com/Lokad/lokad-flatfiles-java).

# Purpose

The vast majority of real-life flat data files are very redundant, as the same identifiers,
dates or numeric values tend to appear on multiple lines. 

However, the purpose of this software is not _storage_ compression (if you need to keep 
flat files in a compressed format, GZip is a better choice).

Rather, the objective of perfect hashing is to improve the speed of parsing operations. 
Consider a file which contains 1000 lines, with the date `2015-01-01` appearing in 100 cells
in column 3.

In a "traditional" CSV-parsing application, the sequence of steps would be as follows:

 1. A tokenizer reads the file and allocates a string matrix representing the file
    (some libraries will instead allow to enumerate string arrays representing lines).
 2. A date parser reads the values in column 3. 

Parsing date strings is a costly operation. It is faster to parse that date _once_ than to 
parse it 100 times, even taking into account the overhead involved in caching the value and 
recognizing that it has already been parsed. This leads to the "optimized" architecture:

 1. As before, a tokenizer produces strings representing cells.
 2. A memoization layer compares the string in column 3 against a hash table of all 
    strings it has seen so far.  
 3. If the string is not yet in the hash table, the date is parsed and the result 
    is added to the hash table.

This is significantly better in terms of performance, but still wasteful. `Lokad.FlatFiles`
moves the cache from step 2 to step 1:

 - The tokenizer produces only unique strings (avoiding 99 unnecessary memory 
   allocations and text encoding operations for `2015-01-01` alone).
 - Subsequent layers (date-parsing, memoization, etc.) use integer identifiers
   rather than strings: comparison is faster and hash tables become simple arrays.

# Behaviour

The file is split into two data sets: 

## Content

The `Content` is a list of all **distinct** byte sequences found in the file cells. 

 - The cell separator (`\t` or `,`) and the line separator (any combination of `\n` and `\r`)
   are not included in the cell. Note that the parser attempts to auto-detect the
   separator character use for the entire file.
 - Cell contents are trimmed (initial and final spaces are not kept).
 - Cells may be quoted (start with `"`, end with `"`, any interior quotes are escaped
   as `""`), in which case the unquoted contents are used. 
 - If the file is encoded as UTF-16, cells are re-encoded a UTF-8. Otherwise, the original
   encoding is kept.   
 
## Cells

The `Cells` is a matrix that contains, for each cell in the file, the index of that cell's
content in the `Content` list. 

This is a line-based matrix, meaning that the cell at line `L`, column `C` is found at 
index `L * Columns + C`. 

Thus, to retrieve the content of the original file: 

    Content[Cells[L * Columns + C]]

# Layout

 - `Program.cs` is the entry point.
 - `RawFlatFile.cs` represents the splitting of a data file into `Cells` and `Content`.
 - `RawFlatFileSerialization.cs` allows serialization of the `RawFlatFile`.
 - `InputBuffer.cs` is used internally by `RawFlatFile.cs` for buffered reading.
 - `Trie.cs` implements the perfect hash function using a [trie](https://en.wikipedia.org/wiki/Trie)
   with an optimized memory allocation strategy.
 - `Example/` contains example files in source (`.tsv`) and parsed (`.rff`) formats.
