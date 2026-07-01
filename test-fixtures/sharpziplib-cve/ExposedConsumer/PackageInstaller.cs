using System.Text;
using ICSharpCode.SharpZipLib.Tar;
using ICSharpCode.SharpZipLib.Zip;

namespace ExposedConsumer;

/// <summary>
/// Downloads and unpacks plugin packages into a local plugins directory.
///
/// This consumer pins the vulnerable SharpZipLib 1.3.1 and *extracts untrusted archives* with it,
/// so it is squarely on the CVE-2021-32842 (ZIP) / CVE-2021-32840/-32841 (TAR) zip-slip path.
/// It is the positive control for reachability detection: the vulnerable symbols are actually
/// called here.
/// </summary>
public sealed class PackageInstaller
{
    /// <summary>
    /// Vulnerable: <c>FastZip.ExtractZip</c> on an untrusted .zip. A crafted entry name containing
    /// <c>../</c> escapes <paramref name="targetDir"/> (zip-slip / CVE-2021-32842).
    /// </summary>
    public void InstallZip(string packagePath, string targetDir)
    {
        var fastZip = new FastZip();
        fastZip.ExtractZip(packagePath, targetDir, null);
    }

    /// <summary>
    /// Vulnerable: <c>TarArchive.ExtractContents</c> on an untrusted .tar
    /// (CVE-2021-32840 / -32841).
    /// </summary>
    public void InstallTar(string packagePath, string targetDir)
    {
        using var stream = File.OpenRead(packagePath);
        using var tar = TarArchive.CreateInputTarArchive(stream, Encoding.UTF8);
        tar.ExtractContents(targetDir);
    }

    /// <summary>
    /// Vulnerable: hand-rolled extraction that writes entry names straight to disk via
    /// <c>ZipInputStream</c> with no traversal check.
    /// </summary>
    public void InstallZipManually(string packagePath, string targetDir)
    {
        using var zis = new ZipInputStream(File.OpenRead(packagePath));
        while (zis.GetNextEntry() is { } entry)
        {
            var outPath = Path.Combine(targetDir, entry.Name);
            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

            using var outFile = File.Create(outPath);
            zis.CopyTo(outFile);
        }
    }

    /// <summary>
    /// Safe method on the *same* <c>FastZip</c> type. Present so the fixture can demonstrate that
    /// member-level symbol targeting discriminates the vulnerable <c>ExtractZip</c> from the benign
    /// <c>CreateZip</c> even though both live on <c>FastZip</c>.
    /// </summary>
    public void Repackage(string zipPath, string sourceDir)
    {
        var fastZip = new FastZip();
        fastZip.CreateZip(zipPath, sourceDir, recurse: true, fileFilter: null);
    }
}
