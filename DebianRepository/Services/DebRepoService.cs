using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using DebianRepository.Models;

namespace DebianRepository.Services;

public class DebRepoService(IConfiguration config)
{
    private readonly List<DebPackage> _packages = [];
    private readonly string           _gpgKeyId = "120548A2CEC5EED4" ?? throw new ArgumentNullException("GPG_KEY_ID env var is required");

    public void AddDebPackage(byte[] debContent)
    {
        var metadata = DebParser.ExtractControlData(debContent);
        var deb = new DebPackage
        {
            FileContent   = debContent,
            ControlFields = metadata
        };
        _packages.RemoveAll(p => p.Filename == deb.Filename);
        _packages.Add(deb);
    }

    public string GetPackagesFile()
    {
        var sb = new StringBuilder();
        foreach (var pkg in _packages)
        {
            var f = pkg.ControlFields;
            sb.AppendLine($"Package: {f["Package"]}");
            sb.AppendLine($"Version: {f["Version"]}");
            sb.AppendLine($"Architecture: {f["Architecture"]}");
            sb.AppendLine($"Maintainer: {f["Maintainer"]}");
            sb.AppendLine($"Description: {f["Description"]}");
            sb.AppendLine($"Filename: {pkg.Path}");
            sb.AppendLine($"Size: {pkg.Size}");
            sb.AppendLine($"MD5sum: {pkg.HashMD5}");
            sb.AppendLine($"SHA1: {pkg.HashSHA1}");
            sb.AppendLine($"SHA256: {pkg.HashSHA256}");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    public async Task<string> GetSignedReleaseFileAsync()
    {
        var packagesData = Encoding.UTF8.GetBytes(GetPackagesFile());

        var sb = new StringBuilder();
        sb.AppendLine("Origin: DebianRepo");
        sb.AppendLine("Label: DebianRepo");
        sb.AppendLine("Suite: stable");
        sb.AppendLine("Codename: stable");
        sb.AppendLine("Architectures: amd64");
        sb.AppendLine("Components: main");
        sb.AppendLine("MD5Sum:");
        sb.AppendLine($" {ComputeHash(packagesData, MD5.Create())} {packagesData.Length,8} main/binary-amd64/Packages");
        sb.AppendLine("SHA256:");
        sb.AppendLine($" {ComputeHash(packagesData, SHA256.Create())} {packagesData.Length,8} main/binary-amd64/Packages");

        return await GpgSignAsciiArmored(sb.ToString());
    }

    public DebPackage? GetPackageByFilename(string filename) =>
        _packages.FirstOrDefault(p => p.Filename == filename);

    private async Task<string> GpgSignAsciiArmored(string content)
    {
        var psi = new ProcessStartInfo("gpg")
        {
            Arguments              = $"--clearsign --armor --default-key {_gpgKeyId}",
            RedirectStandardInput  = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false
        };
        var process = Process.Start(psi);
        if (process != null)
        {
            await process.StandardInput.WriteAsync(content).ConfigureAwait(false);
            process.StandardInput.Close();
            var result = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            return result;
        }

        return "";
    }

    private static string ComputeHash(byte[] content, HashAlgorithm algo) =>
        Convert.ToHexStringLower(algo.ComputeHash(content));
}