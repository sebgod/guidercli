using System.Net.Sockets;

namespace PHD2;

internal class UdpBase {
    internal const int Port = 16551;

    internal const string Broadcast = "255.255.255.255";

    protected readonly UdpClient _client;

    public UdpBase(UdpClient client) {
        _client = client;
    }

    public void Dispose() =>  _client.Dispose();
}
