using System.Security.Cryptography;
using System.Text;
using DebianRepository.Extensions;
using DebianRepository.Models;
using DebianRepository.Providers;
using DebianRepository.Systems;

namespace DebianRepository.Services;

public class DebRepoService : IDebRepoService
{
	private readonly string _storagePath;
	private IReadOnlyCollection<BaseFile> Packages => _fileSystem.ListFiles();

	private readonly ILogger<DebRepoService> _logger;
	private readonly IGpgProvider            _gpgProvider;
	private readonly IFileSystem             _fileSystem;

	public DebRepoService(ILogger<DebRepoService> logger, IConfiguration config, IGpgProvider gpgProvider, IFileSystem fileSystem)
	{
		_logger      = logger;
		_storagePath = config["DEB_STORAGE_PATH"] ?? "/var/lib/debrepo/packages";
		_gpgProvider = gpgProvider;
		_fileSystem  = fileSystem;

		fileSystem.SetFilter("*.deb");
		fileSystem.SetOnAddFileCallback(OnAddFile);
		fileSystem.SetRootDirectory(_storagePath).ConfigureAwait(false).GetAwaiter().GetResult();
	}

	private async Task<BaseFile> OnAddFile(string fullPath)
	{
		var data = await _fileSystem.GetFileData(fullPath).ConfigureAwait(false);
		var dist = "stable";
		var pool = "main";

		var path = fullPath.Replace(_storagePath, "").Split('/');
		if (path.Length >= 3)
		{
			dist = path[0];
			pool = path[1];
		}
		
		var deb  = new DebPackage(fileContent: data, controlFields: data.ExtractControlData(), dist, pool);
		
		var dir = Path.Combine(_storagePath, dist, pool, deb.PackageName);
		Directory.CreateDirectory(dir);
		var filename = Path.Combine(dir, deb.Filename);
		
		if (!fullPath.Equals(filename))
			_fileSystem.MoveFile(fullPath, filename);

		return deb;
	}


	public async Task AddDebPackage(string dist, string pool, byte[] debContent, bool saveToDisk = true, string oldPath = "")
	{
		var deb      = new DebPackage(fileContent: debContent, controlFields: debContent.ExtractControlData(), dist, pool);

		_logger.LogInformation("Adding package - Dist: {Dist}, Pool: {Pool}, Arch: {DebArchitecture}, Package: {DebPackageName}, Version: {DebVersion}", dist, pool, deb.Architecture, deb.PackageName, deb.Version);

		if (saveToDisk)
		{
			var dir = Path.Combine(_storagePath, dist, pool, deb.PackageName);
			Directory.CreateDirectory(dir);
			var filename = Path.Combine(dir, deb.Filename);
			_logger.LogInformation("Saving package file to \"{FileName}\"", filename);
			if (!string.IsNullOrEmpty(oldPath))
				_fileSystem.MoveFile(oldPath, filename);
			else
				await _fileSystem.SaveFile(filename, debContent).ConfigureAwait(false);
		}
	}
	

	public string GetPackagesFile(string dist, string pool, string arch)
	{
		var sb = new StringBuilder();
		foreach (var pkg in Packages.OfType<DebPackage>().Where(p=> p.Architecture.Equals(arch) && p.Pool.Equals(pool) && p.Dist.Equals(dist))
		                             .OrderBy(p => p?.PackageName)
		                             .ThenByDescending(p => p?.Version))
		{
			foreach (var (key, value) in pkg.ControlFields)
			{
				if (!string.IsNullOrWhiteSpace(value))
					sb.AppendLine($"{key}: {value}");
			}

			sb.AppendLine($"Filename: pool/{pool}/{pkg.Filename}");
			sb.AppendLine($"Size: {pkg.Size}");
			sb.AppendLine($"MD5sum: {pkg.HashMd5}");
			sb.AppendLine($"SHA1: {pkg.HashSha1}");
			sb.AppendLine($"SHA256: {pkg.HashSha256}");

			sb.AppendLine();
		}

		return sb.ToString();
	}

	private IEnumerable<string> GetArchitectures(string dist) => Packages.OfType<DebPackage>().Where(pkg=> pkg.Dist.Equals(dist)).Select(pkg => pkg.Architecture).Distinct();
	private IEnumerable<string> GetPools(string dist)         => Packages.OfType<DebPackage>().Where(pkg=> pkg.Dist.Equals(dist)).Select(pkg => pkg.Pool).Distinct();

	public string GetReleaseFile(string dist)
	{
		var architectures = GetArchitectures(dist).ToArray();
		var pools         = GetPools(dist).ToArray();

		var sb            = new StringBuilder();
		sb.AppendLine("Origin: DebianRepo");
		sb.AppendLine("Label: DebianRepo");
		sb.AppendLine($"Suite: {dist}");
		sb.AppendLine($"Codename: {dist}");
		sb.AppendLine("Date: " + DateTime.UtcNow.ToString("r")); // RFC1123 format
		sb.AppendLine($"Architectures: {string.Join(' ', architectures)}");
		sb.AppendLine($"Components: {string.Join(' ', pools)}");

		var md5     = new StringBuilder();
		var md52    = MD5.Create();
		var sha256  = new StringBuilder();
		var sha2562 = SHA256.Create();

		foreach (var pool in pools)
		{
			foreach (var architecture in architectures)
			{
				var packages      = Encoding.UTF8.GetBytes(GetPackagesFile(dist, pool, architecture));
				md5.AppendLine($" {packages.ComputeHash(md52)} {packages.Length,8} {pool}/binary-{architecture}/Packages");
				sha256.AppendLine($" {packages.ComputeHash(sha2562)} {packages.Length,8} {pool}/binary-{architecture}/Packages");
			}
		}
		sb.AppendLine("MD5Sum:");
		sb.Append(md5);
		sb.AppendLine("SHA256:");
		sb.Append(sha256);


		return sb.ToString();
	}

	public Task<string> GetInRelease(string dist) => _gpgProvider.SignAsciiArmored(GetReleaseFile(dist));

	public byte[] GetReleaseGpg(string dist) => _gpgProvider.CreateDetachedSignature(Encoding.UTF8.GetBytes(GetReleaseFile(dist)));

	public DebPackage? GetPackageByFilename(string pool, string filename) => Packages.OfType<DebPackage>().FirstOrDefault(p => p.Filename == filename);
}