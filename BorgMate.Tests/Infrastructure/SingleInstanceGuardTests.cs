using System.Text;
using BorgMate.Infrastructure.Platform;

namespace BorgMate.Tests.Infrastructure;

public class SingleInstanceGuardTests
{
    [Theory]
    [InlineData("ACTIVATE\n", "ACTIVATE")]
    [InlineData("WAKE\r\n", "WAKE")]
    public async Task ReadMessage_ReturnsLineWithoutTerminator(string sent, string expected)
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes(sent));

        Assert.Equal(expected, await SingleInstanceGuard.ReadMessageAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task ReadMessage_EndlessLineWithoutNewline_StopsAfterMessageLength()
    {
        var stream = new EndlessStream();

        Assert.Null(await SingleInstanceGuard.ReadMessageAsync(stream, CancellationToken.None));
        Assert.True(stream.BytesRead <= 17);
    }

    private class EndlessStream : Stream
    {
        public long BytesRead { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            Array.Fill(buffer, (byte)'A', offset, count);
            BytesRead += count;
            return count;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }
}
