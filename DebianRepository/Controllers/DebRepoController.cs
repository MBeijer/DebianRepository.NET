using DebianRepository.Services;
using Microsoft.AspNetCore.Mvc;

namespace DebianRepository.Controllers;

[Route("repo")]
[ApiController]
public class DebRepoController(DebRepoService repo) : ControllerBase
{
	[HttpPost("upload")]
	public async Task<IActionResult> Upload([FromForm] IFormFile file)
	{
		if (!file.FileName.EndsWith(".deb"))
			return BadRequest("Must upload a .deb file");

		using var ms = new MemoryStream();
		await file.CopyToAsync(ms).ConfigureAwait(false);
		repo.AddDebPackage(ms.ToArray());

		return Ok("Uploaded and indexed");
	}

	[HttpGet("dists/stable/Release")]
	public async Task<IActionResult> Release()
	{
		var release = await repo.GetSignedReleaseFileAsync().ConfigureAwait(false);
		return Content(release, "text/plain");
	}

	[HttpGet("dists/stable/main/binary-amd64/Packages")]
	public IActionResult Packages()
	{
		var packages = repo.GetPackagesFile();
		return Content(packages, "text/plain");
	}

	[HttpGet("pool/main/{filename}")]
	public IActionResult Deb(string filename)
	{
		var pkg = repo.GetPackageByFilename(filename);
		if (pkg == null)
			return NotFound();

		return File(pkg.FileContent, "application/vnd.debian.binary-package", pkg.Filename);
	}
}