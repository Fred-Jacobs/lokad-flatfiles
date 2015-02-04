using System.IO;

namespace Lokad.FlatFiles
{
    public static class Program
    {
        static void Main(string[] args)
        {
            var inputFile = args[0];
            var outputFile = args[1];

            // Parse the input file
            RawFlatFile rff;
            using (var stream = new FileStream(inputFile, FileMode.Open))
                rff = new RawFlatFile(stream);

            // Write the compressed output file
            using (var stream = new FileStream(outputFile, FileMode.Create))
            using (var writer = new BinaryWriter(stream))
                writer.Write(rff);
        }
    }
}
