using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using DebianRepository.Providers;
using DebianRepository.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

namespace DebianRepository.Controllers;

[Route("debian")]
[ApiController]
public class DebRepoController(IDebRepoService repo, IConfiguration config, IGpgProvider gpgProvider) : ControllerBase
{
    [HttpPost("login")]
    public IActionResult Login([FromForm] string username, [FromForm] string password)
    {
        var expectedUser = config["AUTH_USER"] ?? "admin";
        var expectedPass = config["AUTH_PASS"] ?? "password";

        if (username != expectedUser || password != expectedPass)
            return Unauthorized("Invalid credentials");

        var claims = new[] { new Claim(ClaimTypes.Name, username) };
        var key    = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["JWT_SECRET"]));
        var creds  = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddHours(12),
            signingCredentials: creds);

        return Ok(new { token = new JwtSecurityTokenHandler().WriteToken(token) });
    }

    [HttpPost("upload")]
    [HttpPost("upload/{dist}/{pool}")]
    [Authorize]
    public async Task<IActionResult> Upload([FromForm] IFormFile file, string dist = "stable", string pool = "main")
    {
        if (file == null || !file.FileName.EndsWith(".deb"))
            return BadRequest("Must upload a .deb file");

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        await repo.AddDebPackage(dist, pool, ms.ToArray()).ConfigureAwait(false);

        return Ok("Uploaded and indexed");
    }

    [HttpGet("dists/{dist}/Release")]
    public IActionResult Release(string dist) => Content(repo.GetReleaseFile(dist), "text/plain");

    [HttpGet("dists/{dist}/Release.gpg")]
    public IActionResult ReleaseGpg(string dist) => File(repo.GetReleaseGpg(dist), "application/pgp-signature", "Release.gpg");

    [HttpGet("dists/{dist}/InRelease")]
    public async Task<IActionResult> InRelease(string dist) => File(Encoding.UTF8.GetBytes(await repo.GetInRelease(dist)), "application/octet-stream", "InRelease");

    [HttpGet("pubkey.gpg")]
    public IActionResult PublicKey() => File(gpgProvider.GetPublicKey(), "application/pgp-keys", "repo-publickey.gpg");

    [HttpGet("dists/{dist}/{pool}/binary-{arch}/Packages")]
    public IActionResult Packages(string dist, string pool, string arch) => Content(repo.GetPackagesFile(dist, pool, arch), "text/plain");

    [HttpGet("pool/{pool}/{filename}")]
    public IActionResult Deb(string pool, string filename)
    {
        var pkg = repo.GetPackageByFilename(pool, filename);
        if (pkg == null)
            return NotFound();

        return File(pkg.FileContent, "application/vnd.debian.binary-package", pkg.Filename);
    }
}