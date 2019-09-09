using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Extensions;
using Nito.AsyncEx;

namespace DotNetCore3EventLog
{
    public sealed class EventQueue : IAsyncDisposable
    {
        private readonly Dictionary<long, (long, long)> _index = new Dictionary<long, (long, long)>();
        private readonly Stream _indexFile;
        private readonly Stream _logFile;
        private readonly AsyncLock _syncObj = new AsyncLock();
        private long _currentSegment = 0;
        private long _nextOffset = 0;
        private bool _isDisposed = false;

        public EventQueue(string queueName)
        {
            var dataFolder = Path.Combine(
                Path.GetDirectoryName(typeof(EventQueue).Assembly.Location),
                "data");

            if (!Directory.Exists(dataFolder))
            {
                Directory.CreateDirectory(dataFolder);
            }

            _indexFile = File.Open(Path.Combine(dataFolder, $"{_currentSegment:d9}.idx"), FileMode.Create);
            _logFile = File.Open(Path.Combine(dataFolder, $"{_currentSegment:d9}.dat"), FileMode.Create);
        }

        public async ValueTask<long> Append(Stream source, CancellationToken cancellationToken)
        {
            using (await _syncObj.LockAsync(cancellationToken))
            {
                if (_isDisposed)
                {
                    throw new ObjectDisposedException(nameof(EventQueue));
                }

                var offset = _nextOffset++;
                var position = _logFile.Position;
                await source.CopyToAsync(_logFile, 4* 1024*1024, cancellationToken);
                var length = _logFile.Position - position;

                var buffer = ArrayPool<byte>.Shared.Rent(sizeof(long) * 3);
                BinaryPrimitives.WriteInt64BigEndian(buffer, offset);
                BinaryPrimitives.WriteInt64BigEndian(buffer.AsSpan(sizeof(long)), position);
                BinaryPrimitives.WriteInt64BigEndian(buffer.AsSpan(sizeof(long) * 2), length);
                await _indexFile.WriteAsync(buffer, 0, sizeof(long) * 3, cancellationToken);
                ArrayPool<byte>.Shared.Return(buffer);

                _index.Add(offset, (position, length));

                return offset;
            }
        }

        public async ValueTask<bool> TryRead(long offset, Stream destination, CancellationToken cancellationToken)
        {
            using (await _syncObj.LockAsync(cancellationToken))
            {
                if (_isDisposed)
                {
                    throw new ObjectDisposedException(nameof(EventQueue));
                }

                if (!_index.ContainsKey(offset))
                {
                    return false;
                }

                (var position, var length) = _index[offset];
                var eof = _logFile.Position;
                _logFile.Position = position;
                await StreamCopyOperation.CopyToAsync(_logFile, destination, length, 4 * 1024 * 1024, cancellationToken);
                _logFile.Position = eof;

                return true;
            }
        }

        public async ValueTask DisposeAsync()
        {
            using (await _syncObj.LockAsync())
            {
                if (_isDisposed)
                {
                    return;
                }

                await _logFile.FlushAsync();
                await _indexFile.FlushAsync();
                await _logFile.DisposeAsync();
                await _indexFile.DisposeAsync();

                _isDisposed = true;
            }
        }
    }
}
