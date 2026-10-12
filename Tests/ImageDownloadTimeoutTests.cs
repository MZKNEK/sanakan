using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Sanakan.Services;
using Xunit;

namespace Artifacts
{
    public class ImageDownloadTimeoutTests
    {
        private sealed class Handler : HttpMessageHandler
        {
            private readonly Stream _body;
            public Handler(Stream body) => _body = body;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
                => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(_body), RequestMessage = request });
        }

        private sealed class SlowBody : Stream
        {
            private readonly bool _trickle;
            private readonly TaskCompletionSource _release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource Started { get; } = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            public bool Disposed { get; private set; }
            public CancellationToken ReadToken { get; private set; }
            public int Reads { get; private set; }
            public SlowBody(bool trickle) => _trickle = trickle;
            public void Release() => _release.TrySetResult();
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token)
            {
                ReadToken = token;
                Reads++;
                Started.TrySetResult();
                if (_trickle)
                {
                    await Task.WhenAny(Task.Delay(25, token), _release.Task);
                    token.ThrowIfCancellationRequested();
                    buffer[offset] = 1;
                    return _release.Task.IsCompleted ? 0 : 1;
                }
                await _release.Task.WaitAsync(token);
                return 0;
            }
            protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override void Flush() => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        private static ImageProcessing Images(HttpClient client)
        {
            var images = new ImageProcessing(null, null);
            var field = typeof(ImageProcessing).GetField("_httpClient", BindingFlags.Instance | BindingFlags.NonPublic);
            ((HttpClient)field.GetValue(images)).Dispose();
            field.SetValue(images, client);
            return images;
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task BodyDeadline_CancelsStalledAndTricklingResponsesAndDisposesThem(bool trickle)
        {
            using var body = new SlowBody(trickle);
            using var client = new HttpClient(new Handler(body)) { Timeout = TimeSpan.FromMilliseconds(200) };
            var download = Images(client).TryGetImageStreamAsync("https://example.com/slow.png");
            try
            {
                await body.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
                Assert.Same(Stream.Null, await download.WaitAsync(TimeSpan.FromSeconds(3)));
                Assert.True(body.ReadToken.IsCancellationRequested);
                Assert.True(body.Disposed);
                if (trickle) Assert.True(body.Reads > 1);
            }
            finally
            {
                body.Release();
                using var result = await download.WaitAsync(TimeSpan.FromSeconds(3));
            }
        }
    }
}
