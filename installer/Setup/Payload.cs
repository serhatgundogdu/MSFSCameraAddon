using System;
using System.IO;
using System.Text;

namespace CinematicCam.Setup
{
    /// <summary>
    /// Setup.exe'nin sonuna eklenmiş uygulama zip'i. Düzen:
    /// [Setup.exe][payload.zip][8 bayt: zip başlangıcı (Int64)][8 bayt: "CCSETUP1"]
    /// Yük yoksa (ör. kurulu Uninstall.exe) exe kaldırma modunda çalışır.
    /// </summary>
    internal sealed class Payload
    {
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("CCSETUP1");
        private const int FooterSize = 16;

        private Payload(string exePath, long offset, long length)
        {
            ExePath = exePath;
            Offset = offset;
            Length = length;
        }

        public string ExePath { get; }
        /// <summary>Zip'in başladığı bayt; aynı zamanda yüksüz exe'nin boyutu.</summary>
        public long Offset { get; }
        public long Length { get; }

        public static Payload TryOpen(string exePath)
        {
            using (var fs = File.OpenRead(exePath))
            {
                if (fs.Length < FooterSize) return null;
                fs.Seek(-FooterSize, SeekOrigin.End);
                var footer = new byte[FooterSize];
                if (fs.Read(footer, 0, FooterSize) != FooterSize) return null;
                for (int i = 0; i < Magic.Length; i++)
                    if (footer[8 + i] != Magic[i]) return null;
                long offset = BitConverter.ToInt64(footer, 0);
                long length = fs.Length - FooterSize - offset;
                if (offset <= 0 || length <= 0) return null;
                return new Payload(exePath, offset, length);
            }
        }

        public Stream OpenZip() => new OffsetStream(File.OpenRead(ExePath), Offset, Length);

        /// <summary>Kendi exe'sinin yüksüz kopyasını yazar (kaldırıcı olarak kullanılır).</summary>
        public void WriteBareExe(string target)
        {
            using (var src = File.OpenRead(ExePath))
            using (var dst = File.Create(target))
            {
                var buffer = new byte[81920];
                long left = Offset;
                while (left > 0)
                {
                    int n = src.Read(buffer, 0, (int)Math.Min(buffer.Length, left));
                    if (n <= 0) throw new EndOfStreamException();
                    dst.Write(buffer, 0, n);
                    left -= n;
                }
            }
        }

        /// <summary>Bir akışın belirli bir bölümünü salt okunur, aranabilir akış olarak gösterir.</summary>
        private sealed class OffsetStream : Stream
        {
            private readonly Stream _inner;
            private readonly long _start;
            private readonly long _length;
            private long _position;

            public OffsetStream(Stream inner, long start, long length)
            {
                _inner = inner;
                _start = start;
                _length = length;
                _inner.Seek(start, SeekOrigin.Begin);
            }

            public override bool CanRead => true;
            public override bool CanSeek => true;
            public override bool CanWrite => false;
            public override long Length => _length;

            public override long Position
            {
                get => _position;
                set => Seek(value, SeekOrigin.Begin);
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                long left = _length - _position;
                if (left <= 0) return 0;
                if (count > left) count = (int)left;
                _inner.Seek(_start + _position, SeekOrigin.Begin);
                int n = _inner.Read(buffer, offset, count);
                _position += n;
                return n;
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                long target = origin == SeekOrigin.Begin ? offset : origin == SeekOrigin.Current ? _position + offset : _length + offset;
                if (target < 0 || target > _length) throw new IOException("Geçersiz konum.");
                _position = target;
                return _position;
            }

            public override void Flush() { }
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing) _inner.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
