using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MetaMystia.UI;
using SgrYuki;

namespace MetaMystia.Network;

/// <summary>线层：单 IO 线程收发，应用层经 <see cref="EnqueueSend"/> 统一出站。</summary>
[AutoLog]
public static partial class MpWire
{
    public const string SyncActionCommandId = "SyncAction";

    public static MpSession Session { get; } = new();

    private static DirectTcp _tcp;
    private static Thread _ioThread;
    private static volatile bool _ioRunning;
    private static volatile bool _running;
    private static volatile bool _connecting;

    private static readonly ConcurrentQueue<Outbound> _outbox = new();
    private static readonly ConcurrentQueue<Inbound> _inbox = new();
    private static readonly ConcurrentDictionary<int, long> _pingSent = new();

    private static int _pingId;
    private static long _lastPingMs;
    private static int _currentPort = MpConstants.DefaultPort;

    private const int PingIntervalMs = 3000;
    private const int ConnectTimeoutMs = 10_000;

    private readonly record struct Outbound(byte[] Framed, int? TargetUid, int? ExceptUid, bool LowPriority);
    private readonly record struct Inbound(int FromUid, Action Action);

    public static int CurrentPort => _currentPort;
    public static bool IsRunning => _running;
    public static bool IsConnecting => _connecting;
    public static long LatencyMs { get; private set; }
    public static long NowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public static long TimeOffsetMs { get; set; }
    public static long SyncedNowMs => NowMs - TimeOffsetMs;

    public static bool IsRoomConnected => Session.TransportKind switch
    {
        TransportKind.DirectHost => _tcp?.HasClients == true,
        TransportKind.DirectClient => _tcp?.IsClientConnected == true,
        _ => false
    };

    public static bool CanSend => Session.TransportKind switch
    {
        TransportKind.DirectHost => Session.IsInRoom && IsRoomConnected,
        TransportKind.DirectClient => Session.IsInRoom && IsRoomConnected,
        TransportKind.RelayClient => Session.IsInPublicScope || Session.IsInRoom,
        _ => false
    };

    public static void FlushInbox() => ProcessInboxOnMainThread();

    // --- lifecycle ---

    public static bool StartHost(int port = -1)
    {
        if (port < 0) port = ConfigManager.DefaultPort?.Value ?? MpConstants.DefaultPort;
        if (!Plugin.AllPatched)
        {
            Log.Fatal("Cannot start multiplayer: patch failure");
            return false;
        }
        if (_running) return true;

        StopInternal();
        _currentPort = port;
        _running = true;
        PlayerManager.Local.Id = ConfigManager.GetPlayerId();
        PlayerManager.Local.Uid = MpConstants.HostUid;
        Session.EnterDirectHostRoom();
        StartIoThread(() => _tcp.StartHost(port, ConfigManager.EnableIPv6?.Value ?? false));
        Log.LogInfo($"[MpWire] Host on port {port}");
        return true;
    }

    /// <summary>客机模式：仅设会话，连接由 <see cref="ConnectAsync"/> 完成。</summary>
    public static bool StartClientMode()
    {
        if (!Plugin.AllPatched) return false;
        if (_running) return true;
        _running = true;
        PlayerManager.Local.Id = ConfigManager.GetPlayerId();
        PlayerManager.Local.Uid = MpConstants.UnassignedUid;
        Session.EnterDirectClientRoom();
        StartIoThread(null);
        Log.LogInfo("[MpWire] Client mode (not connected)");
        return true;
    }

    public static void Stop()
    {
        if (!_running) return;
        _running = false;
        StopIoThread();
        Session.Reset();
        CancelSync();
        Log.LogInfo("[MpWire] Stopped");
    }

    public static bool RestartHost(int port) { Stop(); return StartHost(port); }

    public static async Task<bool> ConnectAsync(string host, int port = -1, bool switchFromHost = true)
    {
        if (port < 0) port = ConfigManager.DefaultPort?.Value ?? MpConstants.DefaultPort;
        if (!_running && !StartClientMode()) return false;
        if (IsRoomConnected)
        {
            Log.LogWarning("[C] Already connected");
            return false;
        }
        if (_connecting) return false;

        try
        {
            _connecting = true;
            if (switchFromHost && Session.IsRoomHost)
            {
                StopIoThread();
                Session.EnterDirectClientRoom();
                PlayerManager.Local.Uid = MpConstants.UnassignedUid;
                StartIoThread(null);
            }
            Session.EnterDirectClientRoom();
            await Task.Run(() => _tcp.ConnectClient(host, port, ConnectTimeoutMs));
            HelloAction.SendHello();
            Log.LogMessage($"[C] Connected to {host}:{port}");
            return true;
        }
        catch (Exception e)
        {
            Log.LogError($"[C] Connect failed: {e.Message}");
            return false;
        }
        finally
        {
            _connecting = false;
        }
    }

    public static void DisconnectPeer()
    {
        if (!Session.IsOnline) return;
        if (Session.TransportKind == TransportKind.DirectHost)
        {
            _tcp?.DisconnectAll();
            PlayerManager.ClearPeers();
            CancelSync();
        }
        else if (Session.TransportKind == TransportKind.DirectClient)
        {
            Stop();
        }
        else
        {
            PlayerManager.ClearPeers();
            Session.Reset();
            _running = false;
        }
        Log.LogMessage("[MpWire] Disconnected");
    }

    public static void DisconnectClient(int uid)
    {
        if (!Session.IsRoomHost) return;
        _tcp?.DisconnectClient(uid);
        if (PlayerManager.Peers.ContainsKey(uid))
            OnHostClientLeft(uid);
    }

    // --- app send ---

    public static void EnqueueSend(Action action, bool lowPriority = false)
    {
        if (!CanSend) return;

        var packet = NetPacket.FromSingleAction(action);
        var framed = packet.ToBytesWithLength();
        int? target = action.WireTargetUid;
        int? except = action.WireExceptUid;

        if (Session.TransportKind == TransportKind.RelayClient)
        {
            Log.LogWarning("[MpWire] Relay send not implemented");
            return;
        }

        if (Session.IsRoomHost)
            _outbox.Enqueue(new Outbound(framed, target, except, lowPriority));
        else if (Session.IsRoomClient)
            _outbox.Enqueue(new Outbound(framed, null, null, lowPriority));
    }

    public static void UpdateLatency(int id)
    {
        if (!_pingSent.TryRemove(id, out long t)) return;
        LatencyMs = (NowMs - t) / 2;
    }

    // --- session callbacks (from Actions / handshake) ---

    public static void OnHandshakeComplete(string hostId)
    {
        SceneTransitAction.Send(MpManager.LocalScene);
        CommandScheduler.EnqueueInterval(SyncActionCommandId, 0.5f, MoveSyncAction.SendSync);
        InGameConsole.ShowPassiveFromAnyThread(TextId.MultiplayerConnected.Get());
    }

    public static void OnPeerHandshakeComplete(int uid) =>
        CommandScheduler.EnqueueInterval(SyncActionCommandId, 2f, MoveSyncAction.SendSync);

    // --- IO thread ---

    private static void StartIoThread(System.Action setup)
    {
        StopIoThread();
        _tcp = new DirectTcp(OnWirePacket, OnWirePeerLeft);
        setup?.Invoke();
        _ioRunning = true;
        _ioThread = new Thread(IoLoop) { IsBackground = true, Name = "MpWire-IO" };
        _ioThread.Start();
    }

    private static void StopIoThread()
    {
        _ioRunning = false;
        try { _ioThread?.Join(2000); } catch { }
        _ioThread = null;
        _tcp?.Stop();
        _tcp = null;
        while (_outbox.TryDequeue(out _)) { }
        while (_inbox.TryDequeue(out _)) { }
    }

    private static void StopInternal()
    {
        _running = false;
        StopIoThread();
    }

    private static void IoLoop()
    {
        while (_ioRunning)
        {
            try
            {
                while (_outbox.TryDequeue(out var msg))
                    _tcp?.Enqueue(msg.TargetUid, msg.ExceptUid, msg.Framed, msg.LowPriority);

                _tcp?.Pump();

                if (_running && CanSend)
                {
                    long now = NowMs;
                    if (now - _lastPingMs >= PingIntervalMs)
                    {
                        _lastPingMs = now;
                        SendPingIo();
                    }
                }
            }
            catch (Exception e)
            {
                Log.LogWarning($"[MpWire] IO loop: {e.Message}");
            }
            Thread.Sleep(1);
        }
    }

    private static void SendPingIo()
    {
        int id = Interlocked.Increment(ref _pingId);
        _pingSent[id] = NowMs;
        var action = new PingAction { Id = id };
        var framed = NetPacket.FromSingleAction(action).ToBytesWithLength();
        if (Session.IsRoomHost)
            _tcp?.Enqueue(null, null, framed, false);
        else
            _tcp?.Enqueue(null, null, framed, false);
    }

    // 反序列化已在 PacketBuffer（IO 线程）。主机转发与出站共用 ToBytesWithLength，避免维护第二套组帧逻辑。
    private static void OnWirePacket(int fromUid, NetPacket packet)
    {
        var actions = packet.Actions;
        if (actions.Length == 0) return;

        if (Session.IsRoomHost && fromUid != MpConstants.HostUid && ShouldRelay(actions[0]))
        {
            actions[0].SenderUid = fromUid;
            _outbox.Enqueue(new Outbound(
                NetPacket.FromSingleAction(actions[0]).ToBytesWithLength(), null, fromUid, false));
        }

        foreach (var action in actions)
            _inbox.Enqueue(new Inbound(fromUid, action));
    }

    private static void OnWirePeerLeft(int uid)
    {
        if (Session.IsRoomHost && uid != MpConstants.HostUid)
            PluginManager.Instance?.RunOnMainThread(() => OnHostClientLeft(uid));
        else if (Session.IsRoomClient)
            PluginManager.Instance?.RunOnMainThread(OnClientDisconnected);
    }

    // 仅执行已反序列化 Action 的 OnReceived（Unity / PlayerManager）；转发已在 OnWirePacket（IO 线程）完成。
    private static void ProcessInboxOnMainThread()
    {
        while (_inbox.TryDequeue(out var item))
        {
            // 主机：每条 TCP 连接对应真实 uid，可覆盖包体以防伪造。
            // 客机：线层 fromUid 恒为 HostUid，真实发送者已在主机转发时写入包体 SenderUid。
            if (Session.IsRoomHost)
                item.Action.SenderUid = item.FromUid;
            item.Action.OnReceived();
        }
    }

    // 仅 DirectHost 且来自客机时由 OnWirePacket 调用；不必再判 RoomRole。
    private static bool ShouldRelay(Action action)
    {
        var t = action.GetType();
        return t.GetCustomAttribute<Action.RoomRelayAttribute>() != null
               || t.GetCustomAttribute<Action.PublicRelayAttribute>() != null;
    }

    private static void OnHostClientLeft(int uid)
    {
        if (PlayerManager.Peers.TryGetValue(uid, out var peer))
        {
            var displayName = LiveModeManager.GetDisplayName(uid, peer.Id);
            InGameConsole.ShowPassiveFromAnyThread(TextId.PeerLeft.Get(displayName));
            PeerLeaveAction.BroadcastPeerLeave(uid);
            PlayerManager.RemovePeer(uid);
            MpManager.CheckContinueAfterDisconnect(uid, displayName);
        }
        else
        {
            MpManager.CheckContinueAfterDisconnect(uid, null);
        }
        if (PlayerManager.Peers.IsEmpty) CancelSync();
    }

    private static void OnClientDisconnected()
    {
        while (_outbox.TryDequeue(out _)) { }
        PlayerManager.ClearPeers();
        PlayerManager.Local.Uid = MpConstants.UnassignedUid;
        CancelSync();
        InGameConsole.ShowPassiveFromAnyThread(TextId.MultiplayerDisconnected.Get());
    }

    private static void CancelSync()
    {
        CommandScheduler.RemoveKeyFromKeyQueue(MpManager.PeerGetCharacterUnitNotNullCommand);
        CommandScheduler.CancelInterval(SyncActionCommandId);
    }
}
