using System.Text;
using Org.BouncyCastle.Bcpg;
using Org.BouncyCastle.Bcpg.OpenPgp;

namespace DebianRepository.Providers;

public class GpgProvider : IGpgProvider
{
	private readonly string _privateKeyPath;
	private readonly string _passphrase;

	public GpgProvider(ILogger<GpgProvider> logger, IConfiguration config)
	{
		_privateKeyPath = config["GPG_PRIVATE_KEY"] ?? "/var/lib/debrepo/private.asc";
		_passphrase     = config["GPG_PASSPHRASE"] ?? throw new("GPG_PASSPHRASE not set");
	}

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

	public async Task<string> SignAsciiArmored(string content)
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

	public byte[] CreateDetachedSignature(byte[] content)
	{
		var key = GetSigningSecretKey();

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

	private PgpSecretKey GetSigningSecretKey()
	{
		using var keyIn         = File.OpenRead(_privateKeyPath);
		var       keyRingBundle = new PgpSecretKeyRingBundle(PgpUtilities.GetDecoderStream(keyIn));

		return keyRingBundle.GetKeyRings()
		                    .SelectMany(kr => kr.GetSecretKeys())
		                    .FirstOrDefault(k => k.IsSigningKey)
		       ?? throw new("No signing key found in keyring.");
	}
}