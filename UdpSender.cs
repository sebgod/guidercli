using System.Net.Sockets;
using System.Text.Json;

namespace PHD2;

public interface IUdpSender : IDisposable {
    void BroadcastAsJsonUtf8<T>(T msg);
}

internal class UdpSender : UdpBase, IUdpSender {

    public UdpSender() : base(new UdpClient { EnableBroadcast = true }) {
        // calls base
    }

    public void BroadcastAsJsonUtf8<T>(T msg) => _client.Send(JsonSerializer.SerializeToUtf8Bytes(msg), Broadcast, Port);
}