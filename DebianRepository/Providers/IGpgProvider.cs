namespace DebianRepository.Providers;

public interface IGpgProvider
{
	public byte[]       GetPublicKey();
	public Task<string> SignAsciiArmored(string content);
	public byte[]       CreateDetachedSignature(byte[] content);
}