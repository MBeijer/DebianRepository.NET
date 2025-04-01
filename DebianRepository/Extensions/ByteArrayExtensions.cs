using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Utilities.Zlib;
using SharpCompress.Compressors.Xz;
using SharpCompress.Readers;
using ZstdSharp;

namespace DebianRepository.Extensions;

public static class ByteArrayExtensions
{
    public static string ComputeHash(this byte[] content, HashAlgorithm algo) =>
        Convert.ToHexStringLower(algo.ComputeHash(content));

    public static Dictionary<string, string> ExtractControlData(this byte[] debContent)
    {
        using var ms     = new MemoryStream(debContent);
        using var reader = new BinaryReader(ms);

        // Validate .deb magic
        var magic = Encoding.ASCII.GetString(reader.ReadBytes(8));
        if (magic != "!<arch>\n")
            throw new InvalidDataException("Not a valid .deb archive");

        // Read ar entries
        while (ms.Position < ms.Length)
        {
            var header      = Encoding.ASCII.GetString(reader.ReadBytes(60));
            var fileName    = header[..16].Trim();
            var fileSizeStr = header[48..58].Trim();
            if (!int.TryParse(fileSizeStr, out var fileSize))
                throw new InvalidDataException("Invalid file size in ar header");

            var fileData = reader.ReadBytes(fileSize);
            if (fileSize % 2 != 0) reader.ReadByte(); // skip padding

            if (fileName.StartsWith("control.tar"))
            {
                return ExtractControlFieldsFromTar(fileData);
            }
        }

        throw new InvalidDataException("No control.tar.* file found in .deb");
    }

    private static Dictionary<string, string> ExtractControlFieldsFromTar(this byte[] controlTarData)
    {
        using var compressedStream = new MemoryStream(controlTarData);

        // Detect compression format
        Stream decompressedStream = controlTarData[0] switch
        {
            // xz
            0xFD when controlTarData[1] == '7' => new XZStream(compressedStream),
            // gzip
            0x1F when controlTarData[1] == 0x8B => new GZipStream(compressedStream, CompressionMode.Decompress),
            0x28 when controlTarData[1] == 0xB5 => new DecompressionStream(compressedStream),
            _ => throw new InvalidDataException("Unknown compression format in control.tar"),
        };

        using var reader = ReaderFactory.Open(decompressedStream);
        while (reader.MoveToNextEntry())
        {
            if (reader.Entry.Key == null || reader.Entry.IsDirectory || !reader.Entry.Key.EndsWith("control")) continue;

            using var entryStream = reader.OpenEntryStream();
            using var sr          = new StreamReader(entryStream);
            return ParseControlFile(sr.ReadToEnd());
        }

        throw new InvalidDataException("No control file found inside control.tar.*");
    }

    private static Dictionary<string, string> ParseControlFile(string content)
    {
        var dict  = new Dictionary<string, string>();
        var lines = content.Split('\n');

        foreach (var line in lines)
        {
            var idx = line.IndexOf(':');
            if (idx <= 0) continue;

            var key   = line[..idx];
            var value = line[(idx + 1)..].Trim();
            dict[key] = value;
        }

        return dict;
    }
}