using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace Shua.Zip.ReadAt;

public sealed class HttpReadAt : IReadAt
{
    private readonly HttpClient _client;
    private readonly string _url;

    public HttpReadAt(string url, HttpClient? client = null)
    {
        _url = url;
        _client = client ?? new HttpClient();

        var req = new HttpRequestMessage(HttpMethod.Head, _url);
        var resp = _client.SendAsync(req).GetAwaiter().GetResult();

        resp.EnsureSuccessStatusCode();

        if (resp.Content.Headers.ContentLength is null)
            throw new NotSupportedException("Missing Content-Length");

        if (resp.Headers.AcceptRanges == null ||
            !resp.Headers.AcceptRanges.Contains("bytes"))
            throw new NotSupportedException("Server does not support Range requests");

        Size = resp.Content.Headers.ContentLength.Value;
    }

    public long Size { get; }

    public Stream OpenRead(long offset, int length)
    {
        if (offset < 0 || length <= 0 || offset + length > Size)
            throw new ArgumentOutOfRangeException(nameof(offset));

        var req = new HttpRequestMessage(HttpMethod.Get, _url);
        req.Headers.Range = new RangeHeaderValue(offset, offset + length - 1);

        var resp = _client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead)
            .GetAwaiter().GetResult();

        if (resp.StatusCode != HttpStatusCode.PartialContent)
        {
            resp.Dispose();
            throw new NotSupportedException("Expected 206 Partial Content");
        }

        resp.EnsureSuccessStatusCode();

        var stream = resp.Content.ReadAsStreamAsync()
            .GetAwaiter()
            .GetResult();

        return new HttpReadAtStream(resp, stream, length);
    }

    public void Dispose()
    {
        _client.Dispose();
    }

    private sealed class HttpReadAtStream : Stream
    {
        private readonly Stream _innerStream;
        private readonly HttpResponseMessage _response;
        private long _position;

        public HttpReadAtStream(HttpResponseMessage response, Stream stream, int length)
        {
            _response = response;
            _innerStream = stream;
            Length = length;
            _position = 0;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length { get; }

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = _innerStream.Read(buffer, offset, count);
            _position += read;
            return read;
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

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _innerStream.Dispose();
                _response.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}