using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

/// <summary>
/// GameClient — Fase 3.
/// Nuevos eventos: OnMeteorSpawn, OnZoneBlocked, OnZoneExpired.
/// Portal propio se activa con SendPowerupActivate("portal_propio").
/// </summary>
public class GameClient : MonoBehaviour
{
    public static GameClient Instance { get; private set; }

    [Header("UI Conexión")]
    [SerializeField] private TMP_InputField ipField;
    [SerializeField] private TMP_InputField portField;
    [SerializeField] private TMP_InputField nameField;
    [SerializeField] private TMP_Text statusLabel;

    [Header("UI Lobby")]
    [SerializeField] private TMP_Text lobbyListLabel;
    [SerializeField] private TMP_Text lobbyCountLabel;

    [Header("Escena de juego")]
    [SerializeField] private string gameSceneName = "GameScene";

    [Header("Configuración")]
    [SerializeField] private int defaultPort = 7777;

    [HideInInspector] public UnityEvent<string> OnPlayerMove = new();
    [HideInInspector] public UnityEvent<string> OnCollectConfirm = new();
    [HideInInspector] public UnityEvent<string> OnCollectDeny = new();
    [HideInInspector] public UnityEvent<string> OnPowerupConfirm = new();
    [HideInInspector] public UnityEvent<string> OnMeteorSpawn = new();
    [HideInInspector] public UnityEvent<string> OnZoneBlocked = new();
    [HideInInspector] public UnityEvent<string> OnZoneExpired = new();
    [HideInInspector] public UnityEvent<string> OnMatchEnd = new();
    [HideInInspector] public UnityEvent<string> OnError = new();

    public string PlayerId { get; private set; } = "";
    public string PlayerName { get; private set; } = "";
    public bool Connected { get; private set; } = false;
    public bool InGame { get; private set; } = false;

    public string PendingMatchStart { get; private set; } = "";
    public void ClearPendingMatchStart() => PendingMatchStart = "";

    private TcpClient _client;
    private Thread _readThread;
    private volatile bool _active = false;

    private readonly ConcurrentQueue<Action> _mainQueue = new();
    private readonly StringBuilder _recvBuffer = new();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        if (portField && string.IsNullOrWhiteSpace(portField.text))
            portField.text = defaultPort.ToString();
        SetStatus("Listo para conectar.");
    }

    private void Update()
    {
        while (_mainQueue.TryDequeue(out Action action)) action?.Invoke();
    }

    private void OnApplicationQuit() => Disconnect();
    private void OnDestroy() { if (Instance == this) Instance = null; Disconnect(); }

    public void OnConnect()
    {
        if (Connected) { SetStatus("Ya estás conectado."); return; }
        string ip = ipField ? ipField.text.Trim() : "";
        string name = nameField ? nameField.text.Trim() : "";
        if (string.IsNullOrEmpty(ip)) { SetStatus("Escribe la IP del servidor."); return; }
        if (string.IsNullOrEmpty(name)) { SetStatus("Escribe tu nombre."); return; }
        PlayerName = name;
        int port = GetPort();
        SetStatus($"Conectando a {ip}:{port}...");
        new Thread(() => ConnectThread(ip, port, name)) { IsBackground = true }.Start();
    }

    public void OnDisconnect() { Disconnect(); SetStatus("Desconectado."); }

    private void ConnectThread(string ip, int port, string name)
    {
        try
        {
            _client = new TcpClient();
            _client.Connect(ip, port);
            _client.NoDelay = true;
            _active = true; Connected = true;
            RunOnMain(() => SetStatus($"Conectado a {ip}:{port}"));
            _readThread = new Thread(ReadLoop) { IsBackground = true };
            _readThread.Start();
            long ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            SendRaw($"{{\"type\":\"connect_request\",\"playerName\":\"{name}\",\"timestamp\":{ts}}}");
        }
        catch (Exception ex)
        {
            RunOnMain(() => { SetStatus($"Error: {ex.Message}"); OnError.Invoke(ex.Message); });
            Connected = false;
            try { _client?.Close(); } catch { }
        }
    }

    private void ReadLoop()
    {
        var buf = new byte[8192];
        try
        {
            NetworkStream stream = _client.GetStream();
            while (_active && _client.Connected)
            {
                int read = stream.Read(buf, 0, buf.Length);
                if (read == 0) break;
                _recvBuffer.Append(Encoding.UTF8.GetString(buf, 0, read).Replace("\r", ""));
                string raw = _recvBuffer.ToString(); int nl;
                while ((nl = raw.IndexOf('\n')) >= 0)
                {
                    string line = raw.Substring(0, nl).Trim();
                    raw = raw.Substring(nl + 1);

                    if (!string.IsNullOrEmpty(line) && !line.StartsWith("LOBBY_STATE:"))
                    {
                        string c = line;
                        RunOnMain(() => ParseMessage(c));
                    }
                }
                _recvBuffer.Clear(); _recvBuffer.Append(raw);
            }
        }
        catch { }
        finally
        {
            Connected = false; InGame = false;
            RunOnMain(() => { SetStatus("Desconectado."); ClearLobbyUI(); });
        }
    }

    private void ParseMessage(string msg)
    {
        if (msg.StartsWith("LOBBY_STATE:"))
        { ParseLobbyState(msg.Substring("LOBBY_STATE:".Length)); return; }

        if (!msg.StartsWith("{")) return;
        string type = GameServer.ExtractString(msg, "type");

        switch (type)
        {
            case "connect_ack":
                PlayerId = GameServer.ExtractString(msg, "playerId");
                SetStatus($"En el lobby. Eres {PlayerId} ({PlayerName})");
                break;

            case "match_start":
                InGame = true;
                PendingMatchStart = msg;
                SetStatus("¡Partida iniciando!");
                RunOnMain(() => SceneManager.LoadScene(gameSceneName));
                break;

            case "player_move": OnPlayerMove.Invoke(msg); break;
            case "collect_confirm": OnCollectConfirm.Invoke(msg); break;
            case "collect_deny": OnCollectDeny.Invoke(msg); break;
            case "powerup_confirm": OnPowerupConfirm.Invoke(msg); break;
            case "meteor_spawn": OnMeteorSpawn.Invoke(msg); break;
            case "zone_blocked": OnZoneBlocked.Invoke(msg); break;
            case "zone_expired": OnZoneExpired.Invoke(msg); break;

            case "match_end":
                InGame = false;
                OnMatchEnd.Invoke(msg);
                break;

            case "error":
                SetStatus($"Error: {GameServer.ExtractString(msg, "message")}");
                OnError.Invoke(msg);
                break;

            default:
                break;
        }
    }

    // API para gameplay
    public void SendMove(float x, float z, string state = "moviendose")
    {
        if (!InGame) return;

        long ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Versión SEGURA sin :F2 dentro del interpolado
        string msg = "{" +
            $"\"type\":\"player_move\"," +
            $"\"playerId\":\"{PlayerId}\"," +
            $"\"position\":{{" +
                $"\"x\":{x}," +           // sin :F2
                $"\"y\":0," +
                $"\"z\":{z}" +            // sin :F2
            "}}," +
            $"\"state\":\"{state}\"," +
            $"\"timestamp\":{ts}" +
        "}";

        SendRaw(msg);
    }

    public void SendCollectRequest(string objectId, string objectType)
    {
        if (!InGame) return;
        long ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        string msg = "{" +
            $"\"type\":\"collect_request\"," +
            $"\"playerId\":\"{PlayerId}\"," +
            $"\"objectId\":\"{objectId}\"," +
            $"\"objectType\":\"{objectType}\"," +
            $"\"timestamp\":{ts}" +
        "}";

        SendRaw(msg);
    }

    public void SendPowerupActivate(string powerupType)
    {
        if (!InGame) return;
        long ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        string msg = "{" +
            $"\"type\":\"powerup_activate\"," +
            $"\"playerId\":\"{PlayerId}\"," +
            $"\"powerupType\":\"{powerupType}\"," +
            $"\"timestamp\":{ts}" +
        "}";

        SendRaw(msg);
    }

    private void SendRaw(string msg)
    {
        if (_client == null || !_client.Connected) return;
        try { byte[] d = Encoding.UTF8.GetBytes(msg + "\n"); _client.GetStream().Write(d, 0, d.Length); }
        catch (Exception ex) { RunOnMain(() => SetStatus($"Error al enviar: {ex.Message}")); }
    }

    private void Disconnect()
    {
        if (!_active && !Connected) return;
        _active = false; Connected = false; InGame = false;
        try { _client?.Close(); } catch { }
        try { _readThread?.Join(200); } catch { }
    }

    private void ParseLobbyState(string raw)
    {
        int sep = raw.IndexOf('|');
        string countPart = sep >= 0 ? raw.Substring(0, sep) : raw;
        string playersPart = sep >= 0 ? raw.Substring(sep + 1) : "";
        if (lobbyCountLabel) lobbyCountLabel.text = $"Jugadores: {countPart}";
        if (lobbyListLabel)
        {
            if (string.IsNullOrEmpty(playersPart)) { lobbyListLabel.text = "Sin jugadores aún..."; return; }
            var sb = new StringBuilder();
            foreach (string entry in playersPart.Split(','))
            {
                int colon = entry.IndexOf(':'); if (colon < 0) continue;
                string pid = entry.Substring(0, colon); string pname = entry.Substring(colon + 1);
                sb.AppendLine($"P{pid}" == PlayerId ? $"• {pname} (tú)" : $"• {pname}");
            }
            lobbyListLabel.text = sb.ToString().TrimEnd();
        }
    }

    private void RunOnMain(Action a) => _mainQueue.Enqueue(a);
    private void SetStatus(string t) { if (statusLabel) statusLabel.text = t; }
    private void ClearLobbyUI()
    {
        if (lobbyCountLabel) lobbyCountLabel.text = "Jugadores: 0/?";
        if (lobbyListLabel) lobbyListLabel.text = "Sin jugadores aún...";
    }
    private int GetPort()
    {
        if (portField && int.TryParse(portField.text, out int p) && p > 0 && p <= 65535) return p;
        return defaultPort;
    }
}