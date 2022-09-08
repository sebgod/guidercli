using System.Net.Sockets;
using System.Text.Json;

namespace PHD2;

public interface IUdpReceiver : IDisposable {
    ValueTask<T?> ReceiveAsync<T>(CancellationToken token);
}

internal class UdpReceiver : UdpBase, IUdpReceiver {

    public UdpReceiver() : base(new UdpClient(Port)) {
        // calls base
    }

    public async ValueTask<T?> ReceiveAsync<T>(CancellationToken token = default)
    {
        var receiveResult = await _client.ReceiveAsync(token);
        if (receiveResult.Buffer is byte[] buffer) {
            using var memoryStream = new MemoryStream(buffer);
            return await JsonSerializer.DeserializeAsync<T>(memoryStream);
        } else {
            return default;
        }
    }
}