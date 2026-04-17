#if !NET7_0_OR_GREATER
using System.IO;

namespace Shua.Zip;

public static class Extensions
{
    internal static void ReadExactly(this Stream stream, byte[] buffer, int offset, int count)
    {
        var totalRead = 0;
        while (totalRead < count)
        {
            var read = stream.Read(buffer, offset + totalRead, count - totalRead);
            if (read == 0)
                throw new EndOfStreamException();

            totalRead += read;
        }
    }
}
#else
#endif