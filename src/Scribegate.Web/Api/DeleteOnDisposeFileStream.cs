namespace Scribegate.Web.Api;

// Results.Stream disposes the provided stream after the response finishes.
// Wrapping temp files in a deleting FileStream lets export/site endpoints build
// archives on disk without leaking temp artifacts on success or failure.
internal sealed class DeleteOnDisposeFileStream : FileStream
{
    private readonly string _path;

    private DeleteOnDisposeFileStream(string path)
        : base(
            path,
            FileMode.Create,
            FileAccess.ReadWrite,
            FileShare.Read,
            bufferSize: 64 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan)
    {
        _path = path;
    }

    public static DeleteOnDisposeFileStream CreateTemporary()
    {
        var path = Path.Combine(Path.GetTempPath(), $"scribegate-{Guid.CreateVersion7():N}.zip");
        return new DeleteOnDisposeFileStream(path);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        TryDelete();
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        TryDelete();
    }

    private void TryDelete()
    {
        try
        {
            if (File.Exists(_path))
                File.Delete(_path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
