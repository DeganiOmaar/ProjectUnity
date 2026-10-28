using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Echoes
{
    // ponytail: tiny LAN UDP link for same-room co-op (host broadcasts, client joins by discovery or IP).
    // Only positions, flashlight, monster state and radio voice are synced; sensors stay local (GDD 6).
    // Upgrade path for remote play: Netcode for GameObjects + Unity Relay.
    public class Net : MonoBehaviour
    {
        public const int Port = 47777, DiscoveryPort = 47778;
        public const byte Hello = 1, Welcome = 2, State = 3, Monsters = 4, Voice = 5, Event = 6, Spawn = 7, Bye = 8;
        static readonly byte[] BeaconBytes = Encoding.ASCII.GetBytes("ECHOES");

        public bool IsHost { get; private set; }
        public bool Connected { get; private set; }
        public string Status { get; private set; } = "";
        public event Action PeerJoined, PeerLost;
        public event Action<byte, BinaryReader> Message;

        IPEndPoint peer;
        UdpClient sock, disco;
        volatile bool running;
        float lastHeard, nextHello, nextBeacon;
        readonly ConcurrentQueue<(byte[] data, IPEndPoint from)> inbox = new();
        readonly MemoryStream ms = new();
        BinaryWriter bw;

        public void Host()
        {
            IsHost = true;
            try
            {
                sock = new UdpClient(Port) { EnableBroadcast = true };
                Listen(sock);
                Status = "Waiting for your partner…  Your IP: " + LocalIP();
            }
            catch (Exception e) { Status = "Could not open port " + Port + ": " + e.Message; }
        }

        public void Join(string ip)
        {
            sock = new UdpClient(0) { EnableBroadcast = true };
            Listen(sock);
            if (!string.IsNullOrWhiteSpace(ip) && IPAddress.TryParse(ip.Trim(), out var a))
            {
                peer = new IPEndPoint(a, Port);
                Status = "Connecting to " + a + "…";
                return;
            }
            try
            {
                disco = new UdpClient();
                disco.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                disco.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
                Listen(disco);
            }
            catch (Exception e) { Debug.LogWarning("Discovery unavailable: " + e.Message); }
            Status = "Searching for a host on this Wi-Fi…";
        }

        void Listen(UdpClient c)
        {
            running = true;
            new Thread(() =>
            {
                while (running)
                {
                    try
                    {
                        var ep = new IPEndPoint(IPAddress.Any, 0);
                        var d = c.Receive(ref ep);
                        inbox.Enqueue((d, ep));
                    }
                    catch (SocketException) { if (!running) return; }
                    catch (ObjectDisposedException) { return; }
                }
            }) { IsBackground = true }.Start();
        }

        void Update()
        {
            if (sock == null) return;
            float now = Time.unscaledTime;
            if (IsHost && !Connected && now >= nextBeacon)
            {
                nextBeacon = now + 1f;
                try { sock.Send(BeaconBytes, BeaconBytes.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort)); } catch { /* no broadcast route */ }
            }
            if (!IsHost && !Connected && now >= nextHello)
            {
                nextHello = now + 0.5f;
                // until discovery finds the host, also knock on this machine (two copies on one computer)
                SendTo(peer ?? new IPEndPoint(IPAddress.Loopback, Port), Hello, null);
            }
            while (inbox.TryDequeue(out var m)) Handle(m.data, m.from);
            if (Connected && now - lastHeard > 6f)
            {
                Connected = false;
                Status = "Partner lost";
                PeerLost?.Invoke();
            }
        }

        void Handle(byte[] d, IPEndPoint from)
        {
            if (d.Length == BeaconBytes.Length && Encoding.ASCII.GetString(d) == "ECHOES")
            {
                if (!IsHost && peer == null) { peer = new IPEndPoint(from.Address, Port); Status = "Found host " + from.Address + "…"; }
                return;
            }
            if (d.Length == 0) return;
            byte type = d[0];
            if (IsHost && type == Hello)
            {
                if (peer == null || peer.Equals(from))
                {
                    peer = from;
                    Connected = true;
                    lastHeard = Time.unscaledTime;
                    Status = "Partner connected";
                    PeerJoined?.Invoke(); // idempotent: re-sends the welcome if it got lost
                }
                return;
            }
            if (!IsHost && type == Welcome && !Connected)
            {
                peer = from;
                Connected = true;
                Status = "Connected";
            }
            if (peer == null || !peer.Equals(from)) return;
            lastHeard = Time.unscaledTime;
            if (type == Bye) { lastHeard = -99f; return; }
            using var r = new BinaryReader(new MemoryStream(d, 1, d.Length - 1));
            try { Message?.Invoke(type, r); }
            catch (Exception e) { Debug.LogWarning("Bad packet " + type + ": " + e.Message); }
        }

        public void Send(byte type, Action<BinaryWriter> write)
        {
            if (peer != null) SendTo(peer, type, write);
        }

        void SendTo(IPEndPoint to, byte type, Action<BinaryWriter> write)
        {
            ms.SetLength(0);
            bw ??= new BinaryWriter(ms);
            bw.Write(type);
            write?.Invoke(bw);
            bw.Flush();
            try { sock.Send(ms.GetBuffer(), (int)ms.Length, to); }
            catch (Exception e) { Debug.LogWarning("Send failed: " + e.Message); }
        }

        public static string LocalIP()
        {
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                        if (ua.Address.AddressFamily == AddressFamily.InterNetwork) return ua.Address.ToString();
                }
            }
            catch { /* some platforms don't expose interfaces */ }
            return "127.0.0.1";
        }

        void OnDestroy()
        {
            if (Connected) Send(Bye, null);
            running = false;
            sock?.Close();
            disco?.Close();
        }
    }
}
