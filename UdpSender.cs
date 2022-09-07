using System.Net.Sockets;
using System.Text.Json;

namespace PHD2;

public interface IUdpSender : IDisposable {
    ValueTask<int> BroadcastAsJsonUtf8Async<T>(T msg);
}

internal class UdpSender : UdpBase, IUdpSender {

    public UdpSender() : base(new UdpClient { EnableBroadcast = true }) {
        // calls base
    }

    public ValueTask<int> BroadcastAsJsonUtf8Async<T>(T msg) => _client.SendAsync(JsonSerializer.SerializeToUtf8Bytes(msg), Broadcast, Port);
}