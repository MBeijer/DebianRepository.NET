using System.Diagnostics.CodeAnalysis;

namespace DebianRepository.Models;

public class DefaultFile : BaseFile
{
	[method: SetsRequiredMembers]
	public DefaultFile(string fullPath)
	{
		Filename = System.IO.Path.GetFileName(fullPath);
	}

	public override string Filename { get; }
}