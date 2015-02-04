using System;
using System.Collections.Generic;

namespace Lokad.FlatFiles
{
    /// <summary>
    /// A trie data structure for matching cell contents with unique
    /// and sequential integer identifiers.
    /// </summary>
    /// <remarks>
    /// This class is intended for maximum performance. The implementation,
    /// beyond being fine-tuned, follows a simple design constraint: 
    ///  -> The system should perform (N + 2 log N) memory allocations,
    ///     where N is the number of different byte sequences stored.
    ///
    /// The trie uses compressed sequences. That is, to store sequences
    /// ABC and ABD, only three nodes are used: [AB], [C] and [D] ; this 
    /// is different from an uncompressed version where each node would hold 
    /// exactly one byte: [A], [B], [C] and [D]. 
    /// 
    /// Each trie node contains the following fields:     
    ///  -> 'Buffer' is a pointer to a buffer where the compressed sequence
    ///     is held.
    ///  -> 'Start' and 'End' are the indices, within 'Buffer', where the 
    ///     compressed sequence is stored. This allows sharing buffers 
    ///     between nodes.
    ///  -> 'First' is the first four bytes of the compressed sequence.
    ///  -> 'Reference', if > 0, is the unique identifier of the prefix
    ///     ending at that node. 
    ///  -> 'Children' is an array of children of the node, of size
    ///     <see cref="HashSizeAtLength"/> of the prefix length at that
    ///     node. The starting byte of each child, mod the size, is the
    ///     index of that child within the array. 
    ///  -> 'NextSibling' is an implementation of a list-of-children 
    ///     in the case where several children end up in the same 
    ///     cell of their parent's array.
    /// 
    /// Those fields are all integers (or arrays of integers) and are
    /// not represented by a class/struct: instead, they are implicitely
    /// represented as cells within an array of integers. 
    /// 
    /// 'nodeI' (note the final I) is the index of the first cell in a 
    /// node. The 'End' field can be found at `_nodes[nodeI + End]`, 
    /// and so on. 
    /// 
    /// 'nodeR' is the index of a cell holding a 'nodeI'. This is used
    /// by the algorithm when inserting new nodes, because nodeR is the
    /// index of the cell referencing the node (and is the cell which 
    /// has to be changed). 
    /// 
    /// Hungarian: 
    ///   Values 'iXXX' represent the 'input' source data 
    ///   Values 'bXXX' represent the 'buffer' trie data
    /// </remarks>
    public sealed class Trie
    {
        private const int First = 0;
        private const int Buffer = 1;
        private const int Start = 2;
        private const int End = 3;
        private const int Reference = 4;
        private const int NextSibling = 5;
        private const int Children = 6;

        private readonly List<int> _nodes = new List<int>();

        /// <summary>
        /// Individual values indexed by their unique identifier, as generated
        /// by the trie.
        /// </summary>
        /// <remarks>
        /// <code>CollectionAssert.AreEqual(t.Values[t.Hash(bytes, 0, bytes.Length)], bytes)</code>
        /// </remarks>
        public readonly List<byte[]> Values = new List<byte[]>();

        public Trie()
        {
            Values.Add(new byte[0]);
            for (var i = 0; i < Children + HashSizeAtLength(0); ++i)
                _nodes.Add(0);
        }

        /// <summary>
        /// Computes the size of the "Children" hashtable based on the 
        /// length of the prefix at that node.
        /// </summary>
        /// <remarks>
        /// Hash table size decreases exponentially, reaching '1' at
        /// length 8. This means that shorter strings use up more memory
        /// to avoid 'NextSibling' traversal, while longer strings use
        /// 'NextSibling' traversal to lower memory usage.
        /// </remarks>
        private int HashSizeAtLength(int length)
        {
            if (length < 2) return 256;
            if (length < 7) return 256 >> (length - 2);
            return 1;
        }

        /// <summary>
        /// Reads bytes iBytes[iStart .. iEnd] and returns an unique identifier k
        /// for that sequence, such that <code>trie.Values[k]</code> is the 
        /// corresponding sequence.
        /// </summary>
        public int Hash(byte[] iBytes, int iStart, int iEnd)
        {
            if (iEnd == iStart) return 0;

            // The initial values match the contents of the root node
            // (no point in reading them again: all zeros, never change)
            var bEnd = 0;
            var bStart = 0;
            var bPos = 0;
            var bFirstBytes = 0;

            // Only filled when a byte is needed and not found in 
            // the (more performant) bFirstBytes
            byte[] bBytes = null;

            var nodeI = 0;
            var nodeR = 0;

            for (var iPos = iStart; iPos < iEnd; ++iPos)
            {
                var iByte = iBytes[iPos];

                if (bPos == bEnd)
                {
                    var hashSize = HashSizeAtLength(iPos - iStart);
                    var childR = nodeI + Children + (iByte % hashSize);
                    var childI = _nodes[childR];

                    // Traverse the list of siblings looking for the one with the right
                    // initial byte.
                    while (childI != 0)
                    {
                        bFirstBytes = _nodes[childI + First];
                        if (bFirstBytes % 256 == iByte) break;
                        childR = childI + NextSibling;
                        childI = _nodes[childR];
                    }

                    // This node does not have a child starting with the next byte: 
                    // add a new child.
                    if (childI == 0) return AddNewChild(childR, iBytes, iStart, iEnd, iPos);

                    nodeI = childI;
                    nodeR = childR;
                    bStart = _nodes[nodeI + Start];
                    bEnd = _nodes[nodeI + End];

                    // The sibling search has already ensured that the first byte of 
                    // the buffer matches: continue search from next position.
                    bPos = bStart + 1;

                    continue;
                }

                // Read the next character from bFirstBytes if possible, 
                // and otherwise from the buffer.

                int bByte;
                var bOffset = bPos - bStart;
                if (bOffset < 4)
                {
                    bByte = (bFirstBytes >> (bOffset * 8)) % 256;
                }
                else
                {
                    if (bOffset == 4)
                    {
                        bBytes = Values[_nodes[nodeI + Buffer]];
                    }

                    // ReSharper disable once PossibleNullReferenceException
                    //   bBytes will be initialized at offset = 4 and is not used before
                    bByte = bBytes[bPos];
                }

                // Continue reading through the buffer while we match
                if (bByte == iByte)
                {
                    bPos++;
                    continue;
                }

                // A mismatch: we need to create a new node and insert it here as a
                // child of the current node.

                return AddNewNode(nodeI, nodeR, iBytes, iStart, iEnd, iPos, bPos);
            }

            // We reached the end of the input bytes without a conflict with the trie
            // structure: all we need to do is extract the reference from the current 
            // node, or insert the reference if it isn't already present.

            if (bEnd > bPos)
            {
                return AddNewEnd(nodeI, nodeR, iBytes, iStart, iEnd, bPos);
            }

            var reference = _nodes[nodeI + Reference];

            if (reference == 0)
            {
                return _nodes[nodeI + Reference] = AddNewReference(iBytes, iStart, iEnd);
            }

            return reference;
        }

        /// <summary>
        /// Returns the first 4 bytes encoded as an integer.
        /// </summary>
        private int GetFirst(byte[] bytes, int pos)
        {
            int result = bytes[pos++];

            for (var i = 1; i < 4 && pos < bytes.Length; ++i)
            {
                result = result + (bytes[pos++] << (i * 8));
            }

            return result;
        }

        /// <summary>
        /// Insert a new node into the specified node based on the provided position
        /// within both input and node buffer.
        /// </summary>
        private int AddNewNode(int nodeI, int nodeR, byte[] iBytes, int iStart, int iEnd, int iPos, int bPos)
        {
            var bBytesI = _nodes[nodeI + Buffer];
            var bBytes = Values[bBytesI];

            // Create the middle node
            // ======================

            var midI = _nodes.Count;
            var midLength = iPos - iStart;
            var midHashSize = HashSizeAtLength(midLength);

            _nodes.Add(_nodes[nodeI + First]);       // First
            _nodes.Add(bBytesI);                     // Buffer
            _nodes.Add(_nodes[nodeI + Start]);       // Start
            _nodes.Add(bPos);                        // End
            _nodes.Add(0);                           // Reference
            _nodes.Add(_nodes[nodeI + NextSibling]); // NextSibling  

            for (var u = 0; u < midHashSize; ++u)
                _nodes.Add(0);                       // Children

            _nodes[midI + Children + (bBytes[bPos] % midHashSize)] = nodeI;

            // Replace the old node with the middle node
            // =========================================

            _nodes[nodeR] = midI;

            // Update the old node
            // ===================

            _nodes[nodeI + First] = GetFirst(bBytes, bPos);
            _nodes[nodeI + Start] = bPos;
            _nodes[nodeI + NextSibling] = 0;

            // Insert the new child
            // ====================

            var childR = midI + Children + (iBytes[iPos] % midHashSize);

            return AddNewChild(childR, iBytes, iStart, iEnd, iPos);
        }

        /// <summary>
        /// Insert a new end into the specified node based on the provided position. 
        /// </summary>
        private int AddNewEnd(int nodeI, int nodeR, byte[] iBytes, int iStart, int iEnd, int bPos)
        {
            var reference = AddNewReference(iBytes, iStart, iEnd);

            var length = iEnd - iStart;
            var midHashSize = HashSizeAtLength(length);

            var bBytesI = _nodes[nodeI + Buffer];
            var bBytes = Values[bBytesI];

            // Create the middle node
            // ======================

            var midI = _nodes.Count;

            _nodes.Add(_nodes[nodeI + First]);       // First
            _nodes.Add(bBytesI);                     // Buffer
            _nodes.Add(_nodes[nodeI + Start]);       // Start
            _nodes.Add(bPos);                        // End
            _nodes.Add(reference);                   // Reference
            _nodes.Add(_nodes[nodeI + NextSibling]); // NextSibling

            for (var u = 0; u < midHashSize; ++u)    // Children
                _nodes.Add(0);

            _nodes[midI + Children + bBytes[bPos] % midHashSize] = nodeI;

            // Replace the old node with the middle node
            // =========================================

            _nodes[nodeR] = midI;

            // Update the old node
            // ===================

            _nodes[nodeI + First] = GetFirst(bBytes, bPos);
            _nodes[nodeI + Start] = bPos;
            _nodes[nodeI + NextSibling] = 0;

            return reference;
        }

        /// <summary>
        /// Create a new node as a child of 'nodeI' and containing 'iBytes[iPos..iEnd)' 
        /// and pointing to the appropriate new buffer.
        /// </summary>
        /// <remarks>
        /// The new child has _nodes[childR] as its next sibling, and becomes the first 
        /// child of its parent in cell childR.
        /// </remarks>
        private int AddNewChild(int childR, byte[] iBytes, int iStart, int iEnd, int iPos)
        {
            var reference = AddNewReference(iBytes, iStart, iEnd);
            var hashSize = HashSizeAtLength(iEnd - iStart);

            var childI = _nodes.Count;

            _nodes.Add(GetFirst(iBytes, iPos)); // First
            _nodes.Add(reference);              // Buffer
            _nodes.Add(iPos - iStart);          // Start
            _nodes.Add(iEnd - iStart);          // End
            _nodes.Add(reference);              // Reference
            _nodes.Add(_nodes[childR]);         // NextSibling

            for (var u = 0; u < hashSize; ++u)  // Children
                _nodes.Add(0);

            _nodes[childR] = childI;

            return reference;
        }

        /// <summary>
        /// Extracts the referenced bytes and gives them an integer identifier.
        /// </summary>
        private int AddNewReference(byte[] iBytes, int iStart, int iEnd)
        {
            var length = iEnd - iStart;
            var bBytes = new byte[length];
            Array.Copy(iBytes, iStart, bBytes, 0, length);

            var buffer = Values.Count;
            Values.Add(bBytes);

            return buffer;
        }
    }
}
