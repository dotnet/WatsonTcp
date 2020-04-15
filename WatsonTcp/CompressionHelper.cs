using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace WatsonTcp
{
    internal static class CompressionHelper
    {
        internal static byte[] DeflateCompress(byte[] input)
        {
            if (input == null || input.Length < 1) return input;

            using (MemoryStream ms = new MemoryStream())
            {
                using (DeflateStream deflate = new DeflateStream(ms, CompressionMode.Compress, true))
                {
                    deflate.Write(input, 0, input.Length);
                }
                return ms.ToArray();
            }
        }

        internal static void DeflateCompressFile(string sourceFile, string targetFile)
        {
            if (String.IsNullOrEmpty(sourceFile)) throw new ArgumentNullException(nameof(sourceFile));
            if (String.IsNullOrEmpty(targetFile)) throw new ArgumentNullException(nameof(targetFile));
            if (sourceFile.Equals(targetFile)) throw new ArgumentException("Must use different files for source and target file.");
            if (!File.Exists(sourceFile)) throw new IOException("Source file does not exist.");
            if (File.Exists(targetFile)) throw new IOException("Target file already exists.");

            using (FileStream fsSource = new FileStream(sourceFile, FileMode.Open, FileAccess.Read))
            {
                using (FileStream fsTarget = new FileStream(targetFile, FileMode.Create, FileAccess.ReadWrite))
                {
                    using (DeflateStream gzs = new DeflateStream(fsTarget, CompressionMode.Compress))
                    {
                        byte[] buffer = new byte[65536];
                        int read;
                        while ((read = fsSource.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            gzs.Write(buffer, 0, read);
                        }
                    }
                }
            }
        }

        internal static byte[] DeflateDecompress(byte[] input)
        {
            if (input == null || input.Length < 1) return input;

            using (DeflateStream deflate = new DeflateStream(new MemoryStream(input), CompressionMode.Decompress))
            {
                const int size = 4096;
                byte[] buffer = new byte[size];
                using (MemoryStream ms = new MemoryStream())
                {
                    int count = 0;
                    do
                    {
                        count = deflate.Read(buffer, 0, size);
                        if (count > 0)
                        {
                            ms.Write(buffer, 0, count);
                        }
                    }
                    while (count > 0);
                    return ms.ToArray();
                }
            }
        }

        internal static void DeflateDecompressFile(string sourceFile, string targetFile)
        {
            if (String.IsNullOrEmpty(sourceFile)) throw new ArgumentNullException(nameof(sourceFile));
            if (String.IsNullOrEmpty(targetFile)) throw new ArgumentNullException(nameof(targetFile));
            if (sourceFile.Equals(targetFile)) throw new ArgumentException("Must use different files for source and target file.");
            if (!File.Exists(sourceFile)) throw new IOException("Source file does not exist.");
            if (File.Exists(targetFile)) throw new IOException("Target file already exists.");

            using (FileStream fsSource = new FileStream(sourceFile, FileMode.Open, FileAccess.Read))
            {
                using (FileStream fsTarget = new FileStream(targetFile, FileMode.Create, FileAccess.ReadWrite))
                {
                    using (DeflateStream gzs = new DeflateStream(fsSource, CompressionMode.Decompress))
                    {
                        byte[] buffer = new byte[65536];
                        int read;
                        while ((read = gzs.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            fsTarget.Write(buffer, 0, read);
                        }
                    }
                }
            }
        }

        internal static byte[] GzipCompress(byte[] input)
        {
            if (input == null || input.Length < 1) return input;

            using (MemoryStream ms = new MemoryStream())
            {
                using (GZipStream gzip = new GZipStream(ms, CompressionMode.Compress))
                {
                    gzip.Write(input, 0, input.Length);
                }

                return ms.ToArray();
            }
        }

        internal static void GzipCompressFile(string sourceFile, string targetFile)
        {
            if (String.IsNullOrEmpty(sourceFile)) throw new ArgumentNullException(nameof(sourceFile));
            if (String.IsNullOrEmpty(targetFile)) throw new ArgumentNullException(nameof(targetFile));
            if (sourceFile.Equals(targetFile)) throw new ArgumentException("Must use different files for source and target file.");
            if (!File.Exists(sourceFile)) throw new IOException("Source file does not exist.");
            if (File.Exists(targetFile)) throw new IOException("Target file already exists.");

            using (FileStream fsSource = new FileStream(sourceFile, FileMode.Open, FileAccess.Read))
            {
                using (FileStream fsTarget = new FileStream(targetFile, FileMode.Create, FileAccess.ReadWrite))
                {
                    using (GZipStream gzs = new GZipStream(fsTarget, CompressionMode.Compress))
                    {
                        byte[] buffer = new byte[65536];
                        int read;
                        while ((read = fsSource.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            gzs.Write(buffer, 0, read);
                        }
                    }
                }
            }
        }

        internal static byte[] GzipDecompress(byte[] input)
        {
            if (input == null || input.Length < 1) return input;

            using (GZipStream gzip = new GZipStream(new MemoryStream(input), CompressionMode.Decompress))
            {
                const int size = 4096;
                byte[] buffer = new byte[size];
                using (MemoryStream ms = new MemoryStream())
                {
                    int count = 0;
                    do
                    {
                        count = gzip.Read(buffer, 0, size);
                        if (count > 0)
                        {
                            ms.Write(buffer, 0, count);
                        }
                    }
                    while (count > 0);
                    return ms.ToArray();
                }
            }
        }

        internal static void GzipDecompressFile(string sourceFile, string targetFile)
        {
            if (String.IsNullOrEmpty(sourceFile)) throw new ArgumentNullException(nameof(sourceFile));
            if (String.IsNullOrEmpty(targetFile)) throw new ArgumentNullException(nameof(targetFile));
            if (sourceFile.Equals(targetFile)) throw new ArgumentException("Must use different files for source and target file.");
            if (!File.Exists(sourceFile)) throw new IOException("Source file does not exist.");
            if (File.Exists(targetFile)) throw new IOException("Target file already exists.");

            using (FileStream fsSource = new FileStream(sourceFile, FileMode.Open, FileAccess.Read))
            {
                using (FileStream fsTarget = new FileStream(targetFile, FileMode.Create, FileAccess.ReadWrite))
                {
                    using (GZipStream gzs = new GZipStream(fsSource, CompressionMode.Decompress))
                    {
                        byte[] buffer = new byte[65536];
                        int read;
                        while ((read = gzs.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            fsTarget.Write(buffer, 0, read);
                        }
                    }
                }
            }
        } 
    }
}
