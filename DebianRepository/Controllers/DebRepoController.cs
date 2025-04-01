using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using DebianRepository.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

namespace DebianRepository.Controllers;

[Route("debian")]
[ApiController]
public class DebRepoController(DebRepoService repo, IConfiguration config) : ControllerBase
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
    [Authorize]
    public async Task<IActionResult> Upload([FromForm] IFormFile file)
    {
        if (file == null || !file.FileName.EndsWith(".deb"))
            return BadRequest("Must upload a .deb file");

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        repo.AddDebPackage(ms.ToArray());

        return Ok("Uploaded and indexed");
    }

    [HttpGet("dists/stable/Release")]
    public IActionResult Release() => Content(repo.GetReleaseFile(), "text/plain");

    [HttpGet("dists/stable/Release.gpg")]
    public IActionResult ReleaseGpg() => File(repo.GetReleaseGpg(), "application/pgp-signature", "Release.gpg");

    [HttpGet("dists/stable/InRelease")]
    public async Task<IActionResult> InRelease() => File(Encoding.UTF8.GetBytes(await repo.GetInReleaseAsync()), "application/octet-stream", "InRelease");

    [HttpGet("pubkey.gpg")]
    public IActionResult PublicKey() => File(repo.GetPublicKey(), "application/pgp-keys", "repo-publickey.gpg");

    [HttpGet("dists/stable/main/binary-{arch}/Packages")]
    public IActionResult Packages(string arch) => Content(repo.GetPackagesFile(arch), "text/plain");

    [HttpGet("pool/main/{filename}")]
    public IActionResult Deb(string filename)
    {
        var pkg = repo.GetPackageByFilename(filename);
        if (pkg == null)
            return NotFound();

        return File(pkg.FileContent, "application/vnd.debian.binary-package", pkg.Filename);
    }
}