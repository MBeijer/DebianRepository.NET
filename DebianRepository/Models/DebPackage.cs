using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using DebianRepository.Extensions;

namespace DebianRepository.Models;

public class DebPackage
{
	[method: SetsRequiredMembers]
	public DebPackage(byte[] fileContent, Dictionary<string, string> controlFields)
	{
		ControlFields = controlFields;
		FileContent   = fileContent;

		HashMd5    = FileContent.ComputeHash(MD5.Create());
		HashSha1   = FileContent.ComputeHash(SHA1.Create());
		HashSha256 = FileContent.ComputeHash(SHA256.Create());
	}

	public          Dictionary<string, string> ControlFields { get; }
	public required byte[]                     FileContent   { get; init; }

	public string PackageName  => ControlFields["Package"];
	public string Version      => ControlFields["Version"];
	public string Architecture => ControlFields["Architecture"];
	public string Maintainer   => ControlFields["Maintainer"];
	public string Description  => ControlFields["Description"];
	public string HashMd5      { get; }
	public string HashSha1     { get; }
	public string HashSha256   { get; }
	public long   Size         => FileContent.Length;

	public int InstalledSize
	{
		get
		{
			if (ControlFields.TryGetValue("Installed-Size", out var sizeStr) &&
			    int.TryParse(sizeStr, out var size))
				return size;
			return 0; // fallback
		}
	}

	public string Filename =>
		$"{ControlFields["Package"]}_{ControlFields["Version"]}_{ControlFields["Architecture"]}.deb";
}