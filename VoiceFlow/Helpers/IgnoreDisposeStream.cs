using System;
using System.IO;

namespace VoiceFlow.Helpers;

/// <summary>
/// Stream wrapper that forwards all operations to an underlying stream but ignores Dispose() and Close().
/// Useful when working with writers (like WaveFileWriter) that automatically dispose their destination stream.
/// </summary>
public class IgnoreDisposeStream : Stream
{
    public Stream InnerStream { get; }

    public IgnoreDisposeStream(Stream innerStream)
    {
        InnerStream = innerStream ?? throw new ArgumentNullException(nameof(innerStream));
    }

    public override bool CanRead => InnerStream.CanRead;
    public override bool CanSeek => InnerStream.CanSeek;
    public override bool CanWrite => InnerStream.CanWrite;
    public override long Length => InnerStream.Length;

    public override long Position
    {
        get => InnerStream.Position;
        set => InnerStream.Position = value;
    }

    public override void Flush() => InnerStream.Flush();

    public override int Read(byte[] buffer, int offset, int count) =>
        InnerStream.Read(buffer, offset, count);

    public override long Seek(long offset, SeekOrigin origin) =>
        InnerStream.Seek(offset, origin);

    public override void SetLength(long value) =>
        InnerStream.SetLength(value);

    public override void Write(byte[] buffer, int offset, int count) =>
        InnerStream.Write(buffer, offset, count);

    protected override void Dispose(bool disposing)
    {
        // Explicitly DO NOT close or dispose the underlying stream.
    }
}
