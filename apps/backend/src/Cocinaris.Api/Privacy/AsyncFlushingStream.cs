namespace Cocinaris.Api.Privacy;

/// <summary>
/// A write-only stream for a response body that accepts the few synchronous writes
/// <see cref="System.IO.Compression.ZipArchive"/> still makes for headers and its directory, and sends
/// them on the next asynchronous write or flush. Kestrel refuses synchronous writes, and turning that
/// check off would block a thread per export.
/// </summary>
/// <remarks>Only what was written synchronously waits in memory; entry data passes straight through.</remarks>
internal sealed class AsyncFlushingStream(Stream inner) : Stream
{
    private readonly MemoryStream pending = new();
    private long written;

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => written;
        set => throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        pending.Write(buffer);
        written += buffer.Length;
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await SendPendingAsync(cancellationToken);
        await inner.WriteAsync(buffer, cancellationToken);
        written += buffer.Length;
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    // A synchronous flush keeps the bytes; FlushAsync sends them.
    public override void Flush()
    {
    }

    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
        await SendPendingAsync(cancellationToken);
        await inner.FlushAsync(cancellationToken);
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override async ValueTask DisposeAsync()
    {
        await SendPendingAsync(CancellationToken.None);
        await pending.DisposeAsync();
        await base.DisposeAsync();
    }

    private async Task SendPendingAsync(CancellationToken cancellationToken)
    {
        if (pending.Length == 0)
        {
            return;
        }

        pending.Position = 0;
        await pending.CopyToAsync(inner, cancellationToken);
        pending.SetLength(0);
    }
}
