using System.Security.Cryptography;

namespace DebianRepository.Models;

public class DebPackage
{
	public Dictionary<string, string> ControlFields { get; set; } = new();
	public byte[]                     FileContent   { get; set; }

	public string Filename => $"{ControlFields["Package"]}_{ControlFields["Version"]}_{ControlFields["Architecture"]}.deb";
	public string Path => $"pool/main/{Filename}";
	public string HashMD5 => ComputeHash(FileContent, MD5.Create());
	public string HashSHA1 => ComputeHash(FileContent, SHA1.Create());
	public string HashSHA256 => ComputeHash(FileContent, SHA256.Create());
	public long Size => FileContent.Length;

	private static string ComputeHash(byte[] content, HashAlgorithm algo)
	{
		var hash = algo.ComputeHash(content);
		return Convert.ToHexStringLower(hash);
	}
}