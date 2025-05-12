namespace DebianRepository.Models;

public abstract class BaseFile
{
	public         string HashMd5    { get; protected init; } = null!;
	public         string HashSha1   { get; protected init;}  = null!;
	public         string HashSha256 { get; protected init;}  = null!;
	public virtual string Filename   => throw new NotImplementedException();
	public         string Path       { get; set; } = null!;
}