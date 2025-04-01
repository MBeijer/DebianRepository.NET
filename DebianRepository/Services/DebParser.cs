using System.IO.Compression;
using System.Text;
using SharpCompress.Compressors.Xz;
using SharpCompress.Readers;

namespace DebianRepository.Services;

public static class DebParser
{
    public static Dictionary<string, string> ExtractControlData(byte[] debContent)
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
            if (!int.TryParse(fileSizeStr, out int fileSize))
                throw new InvalidDataException("Invalid file size in ar header");

            byte[] fileData = reader.ReadBytes(fileSize);
            if (fileSize % 2 != 0) reader.ReadByte(); // skip padding

            if (fileName.StartsWith("control.tar"))
            {
                return ExtractControlFieldsFromTar(fileData);
            }
        }

        throw new InvalidDataException("No control.tar.* file found in .deb");
    }

    private static Dictionary<string, string> ExtractControlFieldsFromTar(byte[] controlTarData)
    {
        using var compressedStream = new MemoryStream(controlTarData);

        // Detect compression format
        Stream decompressedStream;
        if (controlTarData[0] == 0xFD && controlTarData[1] == '7') // xz
        {
            decompressedStream = new XZStream(compressedStream);
        }
        else if (controlTarData[0] == 0x1F && controlTarData[1] == 0x8B) // gzip
        {
            decompressedStream = new GZipStream(compressedStream, CompressionMode.Decompress);
        }
        else
        {
            throw new InvalidDataException("Unknown compression format in control.tar");
        }

        using var reader = ReaderFactory.Open(decompressedStream);
        while (reader.MoveToNextEntry())
        {
            if (!reader.Entry.IsDirectory && reader.Entry.Key.EndsWith("control"))
            {
                using var entryStream = reader.OpenEntryStream();
                using var sr          = new StreamReader(entryStream);
                return ParseControlFile(sr.ReadToEnd());
            }
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
            if (idx > 0)
            {
                var key   = line[..idx];
                var value = line[(idx + 1)..].Trim();
                dict[key] = value;
            }
        }

        return dict;
    }
}