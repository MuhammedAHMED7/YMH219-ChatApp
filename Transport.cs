using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace ChatApp;

public sealed record Packet(string Kind, string Name, string Text, DateTimeOffset Time);

internal sealed class Peer(TcpClient socket) : IDisposable
{
    readonly StreamReader reader = new(socket.GetStream(),new UTF8Encoding(false,true));
    readonly StreamWriter writer = new(socket.GetStream(),new UTF8Encoding(false)) { AutoFlush = true };
    readonly SemaphoreSlim sending = new(1);
    public string Endpoint => socket.Client.RemoteEndPoint?.ToString() ?? "?";
    public async Task<Packet?> Read(CancellationToken ct)
    {
        var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
        if(line == null) return null;
        if(line.Length > 16384) throw new InvalidDataException("Paket çok uzun.");
        return JsonSerializer.Deserialize<Packet>(line) ?? throw new InvalidDataException("Geçersiz paket.");
    }
    public async Task Send(Packet packet,CancellationToken ct)
    {
        await sending.WaitAsync(ct).ConfigureAwait(false);
        try { await writer.WriteLineAsync(JsonSerializer.Serialize(packet).AsMemory(),ct).ConfigureAwait(false); }
        finally { sending.Release(); }
    }
    public void Dispose() => socket.Dispose();
}

public sealed class ChatServer : IDisposable
{
    readonly TcpListener listener;
    readonly CancellationTokenSource stop = new();
    readonly ConcurrentDictionary<Peer,string> peers = new();
    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
    public event Action<string[]>? Roster;
    public event Action<string>? Notice;
    public ChatServer(int port)
    {
        listener = new(IPAddress.Any,port);
        listener.Start();
        _ = Accept(stop.Token);
    }
    async Task Accept(CancellationToken ct)
    {
        try
        {
            while(!ct.IsCancellationRequested)
            {
                var socket = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                _ = Serve(new Peer(socket),ct);
            }
        }
        catch(Exception ex) { if(!ct.IsCancellationRequested) Notice?.Invoke(ex.Message); }
    }
    void ListPeers() => Roster?.Invoke(peers.Values.Order().ToArray());
    async Task Serve(Peer peer,CancellationToken ct)
    {
        try
        {
            var hello = await peer.Read(ct).ConfigureAwait(false);
            if(hello?.Kind != "join" || string.IsNullOrWhiteSpace(hello.Name) || hello.Name.Length>32)
                throw new InvalidDataException("Kullanıcı adı geçersiz.");
            string name=hello.Name.Trim();
            peers[peer] = $"{name}   ·   {peer.Endpoint}";
            await peer.Send(new("joined",name,"",DateTimeOffset.Now),ct).ConfigureAwait(false);
            ListPeers();
            while(await peer.Read(ct).ConfigureAwait(false) is {} packet)
            {
                if(packet.Kind!="message" || string.IsNullOrWhiteSpace(packet.Text) || packet.Text.Length>2000) continue;
                var outgoing=new Packet("message",name,packet.Text,DateTimeOffset.Now);
                foreach(var target in peers.Keys)
                {
                    if(target==peer) continue;
                    try {await target.Send(outgoing,ct).ConfigureAwait(false);}
                    catch(Exception) {target.Dispose();}
                }
            }
        }
        catch(Exception ex) { if(!ct.IsCancellationRequested && ex is not IOException) Notice?.Invoke(ex.Message); }
        finally {peers.TryRemove(peer,out _); peer.Dispose(); ListPeers();}
    }
    public void Dispose() {stop.Cancel();listener.Stop();foreach(var p in peers.Keys)p.Dispose();}
}

public sealed class ChatClient : IDisposable
{
    readonly TcpClient socket=new();
    readonly CancellationTokenSource stop=new();
    Peer? peer;
    string name="";
    public event Action<Packet>? Received;
    public event Action<string>? Closed;
    public async Task Connect(string host,int port,string username)
    {
        if(string.IsNullOrWhiteSpace(username) || username.Trim().Length>32) throw new ArgumentException("1–32 karakterlik bir kullanıcı adı girin.");
        name=username.Trim();
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        await socket.ConnectAsync(host,port,timeout.Token).ConfigureAwait(false);
        peer=new(socket);
        await peer.Send(new("join",name,"",DateTimeOffset.Now),timeout.Token).ConfigureAwait(false);
        if((await peer.Read(timeout.Token).ConfigureAwait(false))?.Kind!="joined")throw new IOException("Sunucu katılımı onaylamadı.");
        _=Listen(stop.Token);
    }
    async Task Listen(CancellationToken ct)
    {
        string reason="Sunucu bağlantıyı kapattı.";
        try {while(await peer!.Read(ct).ConfigureAwait(false) is {} msg)Received?.Invoke(msg);}
        catch(Exception ex) {reason=ex.Message;}
        finally {if(!ct.IsCancellationRequested)Closed?.Invoke(reason);}
    }
    public async Task<Packet> Send(string content)
    {
        if(peer==null || string.IsNullOrWhiteSpace(content) || content.Length>2000)throw new InvalidOperationException("1–2000 karakterlik bir mesaj girin.");
        var packet=new Packet("message",name,content,DateTimeOffset.Now);
        await peer.Send(packet,stop.Token).ConfigureAwait(false);
        return packet;
    }
    public void Dispose(){stop.Cancel();socket.Dispose();}
}