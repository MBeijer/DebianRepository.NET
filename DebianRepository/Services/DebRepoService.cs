using System.Security.Cryptography;
using System.Text;
using DebianRepository.Extensions;
using DebianRepository.Models;
using Org.BouncyCastle.Bcpg;
using Org.BouncyCastle.Bcpg.OpenPgp;

namespace DebianRepository.Services;

public class DebRepoService
{
	private readonly string           _storagePath;
	private readonly string           _privateKeyPath;
	private readonly string           _passphrase;
	private readonly List<DebPackage> _packages = [];

	public DebRepoService(IConfiguration config)
	{
		_storagePath    = config["DEB_STORAGE_PATH"] ?? "/var/lib/debrepo/packages";
		_privateKeyPath = config["GPG_PRIVATE_KEY"] ?? "/var/lib/debrepo/private.asc";
		_passphrase     = config["GPG_PASSPHRASE"] ?? throw new("GPG_PASSPHRASE not set");
		Directory.CreateDirectory(_storagePath);
		LoadFromDisk();
	}

	private void LoadFromDisk()
	{
		var files = Directory.GetFiles(_storagePath, "*.deb", SearchOption.AllDirectories);
		foreach (var file in files) AddDebPackage(File.ReadAllBytes(file), false);
	}

	public void AddDebPackage(byte[] debContent, bool saveToDisk = true)
	{
		var metadata = debContent.ExtractControlData();
		var deb      = new DebPackage(fileContent: debContent, controlFields: metadata);

		if (saveToDisk)
		{
			var dir = Path.Combine(_storagePath, deb.PackageName);
			Directory.CreateDirectory(dir);
			var filename = Path.Combine(dir, deb.Filename);
			File.WriteAllBytes(filename, debContent);
		}

		_packages.Add(deb);
	}

	public string GetPackagesFile(string arch)
	{
		var sb = new StringBuilder();
		foreach (var pkg in _packages.Where(p=>p.Architecture.Equals(arch)).OrderBy(p => p.PackageName)
		                             .ThenByDescending(p => p.Version))
		{
			foreach (var (key, value) in pkg.ControlFields)
			{
				if (!string.IsNullOrWhiteSpace(value))
					sb.AppendLine($"{key}: {value}");
			}

			sb.AppendLine($"Filename: pool/main/{pkg.Filename}");
			sb.AppendLine($"Size: {pkg.Size}");
			sb.AppendLine($"MD5sum: {pkg.HashMd5}");
			sb.AppendLine($"SHA1: {pkg.HashSha1}");
			sb.AppendLine($"SHA256: {pkg.HashSha256}");
			sb.AppendLine();
		}

		return sb.ToString();
	}

	public string GetReleaseFile()
	{
		var packages = Encoding.UTF8.GetBytes(GetPackagesFile("amd64"));
		var sb       = new StringBuilder();
		sb.AppendLine("Origin: DebianRepo");
		sb.AppendLine("Label: DebianRepo");
		sb.AppendLine("Suite: stable");
		sb.AppendLine("Codename: stable");
		sb.AppendLine("Date: " + DateTime.UtcNow.ToString("r")); // RFC1123 format
		sb.AppendLine("Architectures: amd64");
		sb.AppendLine("Components: main");
		sb.AppendLine("MD5Sum:");
		sb.AppendLine($" {packages.ComputeHash(MD5.Create())} {packages.Length,8} main/binary-amd64/Packages");
		sb.AppendLine("SHA256:");
		sb.AppendLine($" {packages.ComputeHash(SHA256.Create())} {packages.Length,8} main/binary-amd64/Packages");
		return sb.ToString();
	}

	public async Task<string> GetInReleaseAsync()
	{
		var content = GetReleaseFile();
		return await SignAsciiArmoredAsync(content);
	}

	public byte[] GetReleaseGpg() => CreateDetachedSignature(Encoding.UTF8.GetBytes(GetReleaseFile()));

	public DebPackage? GetPackageByFilename(string filename) => _packages.FirstOrDefault(p => p.Filename == filename);

	public byte[] GetPublicKey()
	{
		using var keyIn         = File.OpenRead(_privateKeyPath);
		var       keyRingBundle = new PgpSecretKeyRingBundle(PgpUtilities.GetDecoderStream(keyIn));
		var       publicOut     = new MemoryStream();
		var       armoredOut    = new ArmoredOutputStream(publicOut);

		foreach (var secretKeyRing in keyRingBundle.GetKeyRings())
			secretKeyRing.GetPublicKey().Encode(armoredOut);

		armoredOut.Close();
		return publicOut.ToArray();
	}

	private async Task<string> SignAsciiArmoredAsync(string content)
	{
		var secretKey  = GetSigningSecretKey();
		var privateKey = secretKey.ExtractPrivateKey(_passphrase.ToCharArray());

		var sigGen = new PgpSignatureGenerator(secretKey.PublicKey.Algorithm, HashAlgorithmTag.Sha256);
		sigGen.InitSign(PgpSignature.CanonicalTextDocument, privateKey);

		using var       output     = new MemoryStream();
		await using var armoredOut = new ArmoredOutputStream(output);
		armoredOut.SetHeader("Version", null);
		armoredOut.BeginClearText(HashAlgorithmTag.Sha256);

		var canonicalLines = content.Replace("\r", "").Split('\n');
		foreach (var line in canonicalLines)
		{
			var canonicalLine = line.StartsWith('-') ? "- " + line : line;
			var signBytes     = Encoding.ASCII.GetBytes(canonicalLine + "\r\n"); // canonical
			sigGen.Update(signBytes);

			var displayLine = canonicalLine + "\n"; // visible
			var writeBytes  = Encoding.ASCII.GetBytes(displayLine);
			await armoredOut.WriteAsync(writeBytes);
		}

		// ✅ Add final newline after the last content line
		await armoredOut.WriteAsync(Encoding.ASCII.GetBytes("\n"));

		armoredOut.EndClearText();
		sigGen.Generate().Encode(armoredOut);
		armoredOut.Close();

		return Encoding.ASCII.GetString(output.ToArray());
	}


	private PgpSecretKey GetSigningSecretKey()
	{
		using var keyIn         = File.OpenRead(_privateKeyPath);
		var       keyRingBundle = new PgpSecretKeyRingBundle(PgpUtilities.GetDecoderStream(keyIn));

		return keyRingBundle.GetKeyRings()
		                    .SelectMany(kr => kr.GetSecretKeys())
		                    .FirstOrDefault(k => k.IsSigningKey)
		       ?? throw new("No signing key found in keyring.");
	}

	private byte[] CreateDetachedSignature(byte[] content)
	{
		using var keyIn   = File.OpenRead(_privateKeyPath);
		var       keyRing = new PgpSecretKeyRingBundle(PgpUtilities.GetDecoderStream(keyIn));
		var key = keyRing.GetKeyRings()
		                 .SelectMany(r => r.GetSecretKeys())
		                 .FirstOrDefault(k => k.IsSigningKey);

		if (key == null) throw new("No signing key found");
		var privateKey = key.ExtractPrivateKey(_passphrase.ToCharArray());

		var sigGen = new PgpSignatureGenerator(key.PublicKey.Algorithm, HashAlgorithmTag.Sha256);
		sigGen.InitSign(PgpSignature.BinaryDocument, privateKey);
		sigGen.Update(content);

		using var sigStream  = new MemoryStream();
		var       armoredOut = new ArmoredOutputStream(sigStream);
		sigGen.Generate().Encode(armoredOut);
		armoredOut.Close();

		return sigStream.ToArray();
	}
}