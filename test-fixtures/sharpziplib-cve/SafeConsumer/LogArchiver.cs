using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Zip;

namespace SafeConsumer;

/// <summary>
/// Bundles and compresses local log files for shipping to storage.
///
/// This consumer pins the vulnerable SharpZipLib 1.3.1, but it only ever *creates* archives and
/// *compresses* streams -- it never extracts an untrusted archive. It therefore does not exercise
/// the CVE-2021-32842 (ZIP) / CVE-2021-32840/-32841 (TAR) zip-slip extraction path, and is the
/// negative control for "references a vulnerable version but does not call the vulnerable API."
///
/// Deliberately references none of the vulnerable types (FastZip, ZipInputStream, TarInputStream,
/// TarArchive) so the negative result holds at both type and member targeting granularity.
/// </summary>
public sealed class LogArchiver
{
    /// <summary>GZip-compress a single log file. Stream compression, not archive extraction.</summary>
    public void CompressLog(string logPath, string gzipPath)
    {
        using var input = File.OpenRead(logPath);
        using var output = new GZipOutputStream(File.Create(gzipPath));
        output.SetLevel(6);
        input.CopyTo(output);
    }

    /// <summary>Bundle several files into a new .zip. Archive *creation*, never extraction.</summary>
    public void BundleLogs(string zipPath, IEnumerable<string> files)
    {
        using var zip = new ZipOutputStream(File.Create(zipPath));
        zip.SetLevel(9);

        var buffer = new byte[4096];
        foreach (var file in files)
        {
            var entry = new ZipEntry(Path.GetFileName(file))
            {
                DateTime = File.GetLastWriteTimeUtc(file),
            };
            zip.PutNextEntry(entry);

            using var source = File.OpenRead(file);
            int count;
            while ((count = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                zip.Write(buffer, 0, count);
            }

            zip.CloseEntry();
        }

        zip.Finish();
    }
}
