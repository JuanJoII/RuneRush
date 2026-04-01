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
/// GameClient — Fase 2: JSON + carga de escena de juego.
///
/// Cambios respecto a fase anterior:
///   - Al conectarse envía connect_request (JSON) en lugar de LOBBY_JOIN.
///   - Parsea connect_ack para obtener el playerId asignado.
///   - Al recibir match_start, carga la escena de juego via SceneManager.
///   - Expone métodos para que PlayerController envíe player_move,
///     collect_request y powerup_activate.
///   - Expone eventos para que GameManager reaccione a los mensajes del servidor.
///
/// Los mensajes de lobby (LOBBY_STATE) se siguen procesando igual que antes.
/// </summary>
public class GameClient : MonoBehaviour
{
    // ── Singleton ──────────────────────────────────────────────────────────────
    public static GameClient Instance { get; private set; }

    // ── UI ─────────────────────────────────────────────────────────────────────
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

    // ── Configuración ──────────────────────────────────────────────────────────
    [Header("Configuración")]
    [SerializeField] private int defaultPort = 7777;

    // ── Eventos públicos ───────────────────────────────────────────────────────
    // GameManager se suscribe a estos desde la escena de juego.
    [HideInInspector] public UnityEvent<string> OnMatchStart = new(); // JSON completo
    [HideInInspector] public UnityEvent<string> OnPlayerMove = new(); // JSON completo
    [HideInInspector] public UnityEvent<string> OnCollectConfirm = new(); // JSON completo
    [HideInInspector] public UnityEvent<string> OnCollectDeny = new(); // JSON completo
    [HideInInspector] public UnityEvent<string> OnPowerupConfirm = new(); // JSON completo
    [HideInInspector] public UnityEvent<string> OnMatchEnd = new(); // JSON completo
    [HideInInspector] public UnityEvent<string> OnError = new();

    // ── Estado público ─────────────────────────────────────────────────────────
    public string PlayerId { get; private set; } = "";   // "P0", "P1", etc.
    public string PlayerName { get; private set; } = "";
    public bool Connected { get; private set; } = false;
    public bool InGame { get; private set; } = false;

    // ── Estado interno ─────────────────────────────────────────────────────────
    private TcpClient _client;
    private Thread _readThread;
    private volatile bool _active = false;

    private readonly ConcurrentQueue<Action> _mainQueue = new();
    private readonly StringBuilder _recvBuffer = new();

    // ── Ciclo Unity ────────────────────────────────────────────────────────────
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
        while (_mainQueue.TryDequeue(out Action action))
            action?.Invoke();
    }

    private void OnApplicationQuit() => Disconnect();
    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        Disconnect();
    }

    // ── Botones UI ─────────────────────────────────────────────────────────────
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

    // ── Conexión (hilo) ────────────────────────────────────────────────────────
    private void ConnectThread(string ip, int port, string name)
    {
        try
        {
            _client = new TcpClient();
            _client.Connect(ip, port);
            _client.NoDelay = true;
            _active = true;
            Connected = true;

            RunOnMain(() => SetStatus($"Conectado a {ip}:{port}"));

            _readThread = new Thread(ReadLoop) { IsBackground = true };
            _readThread.Start();

            // Enviar connect_request con nombre
            long ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            SendRaw($"{{\"type\":\"connect_request\",\"playerName\":\"{name}\",\"timestamp\":{ts}}}");
        }
        catch (Exception ex)
        {
            RunOnMain(() => { SetStatus($"No se pudo conectar: {ex.Message}"); OnError.Invoke(ex.Message); });
            Connected = false;
            try { _client?.Close(); } catch { }
        }
    }

    // ── Hilo de lectura ────────────────────────────────────────────────────────
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
                string raw = _recvBuffer.ToString();
                int nl;
                while ((nl = raw.IndexOf('\n')) >= 0)
                {
                    string line = raw.Substring(0, nl).Trim();
                    raw = raw.Substring(nl + 1);
                    if (!string.IsNullOrEmpty(line))
                    {
                        string captured = line;
                        RunOnMain(() => ParseMessage(captured));
                    }
                }
                _recvBuffer.Clear();
                _recvBuffer.Append(raw);
            }
        }
        catch { }
        finally
        {
            Connected = false;
            InGame = false;
            RunOnMain(() => { SetStatus("Desconectado del servidor."); ClearLobbyUI(); });
        }
    }

    // ── Parseo de mensajes ─────────────────────────────────────────────────────
    private void ParseMessage(string msg)
    {
        // ── Mensajes de lobby (texto plano, fase anterior) ─────────────────────
        if (msg.StartsWith("LOBBY_STATE:"))
        {
            ParseLobbyState(msg.Substring("LOBBY_STATE:".Length));
            return;
        }

        // ── Mensajes JSON ──────────────────────────────────────────────────────
        if (!msg.StartsWith("{")) return;

        string type = GameServer.ExtractString(msg, "type");

        switch (type)
        {
            case "connect_ack":
                // { "type":"connect_ack", "playerId":"P0", "timestamp":... }
                PlayerId = GameServer.ExtractString(msg, "playerId");
                SetStatus($"En el lobby. Eres {PlayerId} ({PlayerName})");
                break;

            case "match_start":
                // El servidor inicia la partida
                InGame = true;
                SetStatus("¡Partida iniciando!");
                string matchJson = msg;  // guardar antes de capturar en lambda
                RunOnMain(() =>
                {
                    // Disparar el evento ANTES de cargar la escena para que
                    // GameManager (si ya existe) pueda recibirlo.
                    // Tras LoadScene, GameManager se suscribirá en OnEnable.
                    // Guardamos el JSON para que GameManager lo lea al despertar.
                    PendingMatchStart = matchJson;
                    SceneManager.LoadScene(gameSceneName);
                });
                break;

            case "player_move":
                OnPlayerMove.Invoke(msg);
                break;

            case "collect_confirm":
                OnCollectConfirm.Invoke(msg);
                break;

            case "collect_deny":
                OnCollectDeny.Invoke(msg);
                break;

            case "powerup_confirm":
                OnPowerupConfirm.Invoke(msg);
                break;

            case "match_end":
                InGame = false;
                OnMatchEnd.Invoke(msg);
                break;

            case "error":
                string errMsg = GameServer.ExtractString(msg, "message");
                SetStatus($"Error: {errMsg}");
                OnError.Invoke(errMsg);
                break;

            default:
                Debug.Log($"[Client] Tipo no manejado: {type}");
                break;
        }
    }

    // ── JSON pendiente para GameManager ───────────────────────────────────────
    /// <summary>
    /// El JSON de match_start se guarda aquí mientras se carga la escena.
    /// GameManager lo lee en su Start() para inicializar el estado.
    /// </summary>
    public string PendingMatchStart { get; private set; } = "";

    public void ClearPendingMatchStart() => PendingMatchStart = "";

    // ── API pública para PlayerController ────────────────────────────────────

    /// <summary>Envía la posición del jugador al servidor.</summary>
    public void SendMove(float x, float z, string state = "moviendose")
    {
        if (!InGame) return;
        long ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string json =
            $"{{\"type\":\"player_move\",\"playerId\":\"{PlayerId}\"," +
            $"\"position\":{{\"x\":{x:F2},\"y\":0.0,\"z\":{z:F2}}}," +
            $"\"state\":\"{state}\",\"timestamp\":{ts}}}";
        SendRaw(json);
    }

    /// <summary>Solicita recoger una runa al servidor.</summary>
    public void SendCollectRequest(string objectId, string objectType = "runa_comun")
    {
        if (!InGame) return;
        long ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string json =
            $"{{\"type\":\"collect_request\",\"playerId\":\"{PlayerId}\"," +
            $"\"objectId\":\"{objectId}\",\"objectType\":\"{objectType}\"," +
            $"\"timestamp\":{ts}}}";
        SendRaw(json);
    }

    /// <summary>Solicita activar viento propio.</summary>
    public void SendPowerupActivate(string powerupType = "viento_propio")
    {
        if (!InGame) return;
        long ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string json =
            $"{{\"type\":\"powerup_activate\",\"playerId\":\"{PlayerId}\"," +
            $"\"powerupType\":\"{powerupType}\",\"timestamp\":{ts}}}";
        SendRaw(json);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────
    private void SendRaw(string msg)
    {
        if (_client == null || !_client.Connected) return;
        try
        {
            byte[] data = Encoding.UTF8.GetBytes(msg + "\n");
            _client.GetStream().Write(data, 0, data.Length);
        }
        catch (Exception ex) { RunOnMain(() => SetStatus($"Error al enviar: {ex.Message}")); }
    }

    private void Disconnect()
    {
        if (!_active && !Connected) return;
        _active = false;
        Connected = false;
        InGame = false;
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
                int colon = entry.IndexOf(':');
                if (colon < 0) continue;
                string pid = entry.Substring(0, colon);
                string pname = entry.Substring(colon + 1);
                bool isMe = $"P{pid}" == PlayerId;
                sb.AppendLine(isMe ? $"• {pname} (tú)" : $"• {pname}");
            }
            lobbyListLabel.text = sb.ToString().TrimEnd();
        }
    }

    private void RunOnMain(Action action) => _mainQueue.Enqueue(action);

    private void SetStatus(string text) { if (statusLabel) statusLabel.text = text; }

    private void ClearLobbyUI()
    {
        if (lobbyCountLabel) lobbyCountLabel.text = "Jugadores: 0/?";
        if (lobbyListLabel) lobbyListLabel.text = "Sin jugadores aún...";
    }

    private int GetPort()
    {
        if (portField && int.TryParse(portField.text, out int p) && p > 0 && p <= 65535)
            return p;
        return defaultPort;
    }
}