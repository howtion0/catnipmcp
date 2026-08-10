namespace Catnip.Infrastructure.Configuration;

internal interface IAtomicFileWriter
{
    ValueTask WriteAsync(
        string destinationPath,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken);
}

internal sealed class AtomicFileWriter(Action? beforeCommit = null) : IAtomicFileWriter
{
    public async ValueTask WriteAsync(
        string destinationPath,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        string directory = Path.GetDirectoryName(destinationPath)
            ?? throw new ArgumentException("A destination directory is required.", nameof(destinationPath));
        Directory.CreateDirectory(directory);

        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");
        string backupPath = destinationPath + ".bak";

        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            beforeCommit?.Invoke();

            if (File.Exists(destinationPath))
            {
                File.Replace(temporaryPath, destinationPath, backupPath, ignoreMetadataErrors: false);
            }
            else
            {
                File.Move(temporaryPath, destinationPath);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
