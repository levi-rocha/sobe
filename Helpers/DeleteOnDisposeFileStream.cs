using System.IO;

public class DeleteOnDisposeFileStream : FileStream
{
    private readonly string _path;
    public DeleteOnDisposeFileStream(string path) : base(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)
    {
        _path = path;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        File.Delete(_path);
    }
}