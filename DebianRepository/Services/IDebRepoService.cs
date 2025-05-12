using DebianRepository.Models;

namespace DebianRepository.Services;

public interface IDebRepoService
{
	public Task AddDebPackage(string dist, string pool, byte[] debContent, bool saveToDisk = true, string oldPath = "");

	public string GetPackagesFile(string dist, string pool, string arch);

	public string GetReleaseFile(string dist);

	public Task<string> GetInRelease(string dist);

	public byte[] GetReleaseGpg(string dist);

	public DebPackage? GetPackageByFilename(string pool, string filename);
}