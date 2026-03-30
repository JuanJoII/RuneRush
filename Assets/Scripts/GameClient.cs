using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// GameClient — Fase 1: Conexión y Lobby.
///
/// Qué hace este script:
///   - El jugador escribe IP, puerto y nombre, pulsa "Conectar".
///   - Se conecta al GameServer y envía su nombre (LOBBY_JOIN).
///   - Escucha mensajes del servidor en un hilo separado.
///   - Cuando recibe LOBBY_STATE, actualiza la UI del lobby.
///   - Cuando recibe GAME_START, dispara el evento OnGameStart
///     (en la siguiente fase esto cargará la escena de juego).
///
/// El host también usa este script para conectarse a su propio servidor
/// (localhost / 127.0.0.1). GameServer y GameClient coexisten en el mismo GameObject.
///
/// PROTOCOLO (solo esta fase):
///
///   Cliente → Servidor:
///     LOBBY_JOIN:<nombre>
///
///   Servidor → Este cliente:
///     ASSIGNED_ID:<id>
///     LOBBY_STATE:<n>/<max>|<id>:<nombre>,...
///     GAME_START
///     SERVER_FULL
/// </summary>
public class GameClient : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────────
    public static GameClient Instance { get; private set; }

    // ── Referencias UI ────────────────────────────────────────────────────────
    [Header("UI Conexión")]
    [SerializeField] private TMP_InputField ipField;
    [SerializeField] private TMP_InputField portField;
    [SerializeField] private TMP_InputField nameField;
    [SerializeField] private TMP_Text statusLabel;

    [Header("UI Lobby")]
    [SerializeField] private TMP_Text lobbyListLabel;   // Lista de jugadores conectados
    [SerializeField] private TMP_Text lobbyCountLabel;  // "2/4 jugadores"

    // ── Configuración ─────────────────────────────────────────────────────────
    [Header("Configuración")]
    [SerializeField] private int defaultPort = 7777;

    // ── Eventos públicos ──────────────────────────────────────────────────────
    // Otros scripts (o la UI) se suscriben a estos eventos en el Inspector
    // o por código. Por ahora solo usamos OnGameStart.

    [HideInInspector] public UnityEvent<int> OnAssignedId = new(); // Mi ID asignado
    [HideInInspector] public UnityEvent<string> OnLobbyState = new(); // Estado del lobby raw
    [HideInInspector] public UnityEvent OnGameStart = new(); // Señal de inicio
    [HideInInspector] public UnityEvent<string> OnError = new(); // Mensaje de error

    // ── Estado público ────────────────────────────────────────────────────────
    public int MyId { get; private set; } = -1;
    public bool Connected { get; private set; } = false;

    // ── Estado interno ────────────────────────────────────────────────────────
    private TcpClient _client;
    private Thread _readThread;
    private volatile bool _active = false;

    // Los hilos de red depositan acciones aquí; Update() las ejecuta en el main thread.
    private readonly ConcurrentQueue<Action> _mainThreadQueue = new();
    private readonly StringBuilder _recvBuffer = new();

    // ── Ciclo Unity ───────────────────────────────────────────────────────────
    private void Awake()
    {
        // Singleton simple: si ya existe una instancia, destruir esta.
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject); // Persistir entre escenas
    }

    private void Start()
    {
        // Prerellenar puerto si está vacío
        if (portField && string.IsNullOrWhiteSpace(portField.text))
            portField.text = defaultPort.ToString();

        SetStatus("Listo para conectar.");
    }

    private void Update()
    {
        // Ejecutar en el main thread todo lo que los hilos de red encolaron.
        while (_mainThreadQueue.TryDequeue(out Action action))
            action?.Invoke();
    }

    private void OnApplicationQuit() => Disconnect();
    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        Disconnect();
    }

    // ── Botones UI ────────────────────────────────────────────────────────────

    /// <summary>
    /// El jugador pulsa "Conectar".
    /// Lanza la conexión en un hilo para no bloquear la UI.
    /// </summary>
    public void OnConnect()
    {
        if (Connected) { SetStatus("Ya estás conectado."); return; }

        string ip = ipField ? ipField.text.Trim() : "";
        string name = nameField ? nameField.text.Trim() : "";

        if (string.IsNullOrEmpty(ip))
        {
            SetStatus("Escribe la IP del servidor.");
            return;
        }
        if (string.IsNullOrEmpty(name))
        {
            SetStatus("Escribe tu nombre.");
            return;
        }

        int port = GetPort();
        SetStatus($"Conectando a {ip}:{port}...");

        new Thread(() => ConnectThread(ip, port, name)) { IsBackground = true }.Start();
    }

    /// <summary>
    /// El jugador pulsa "Desconectar".
    /// </summary>
    public void OnDisconnect()
    {
        Disconnect();
        SetStatus("Desconectado.");
    }

    // ── Lógica de conexión (hilo) ─────────────────────────────────────────────
    private void ConnectThread(string ip, int port, string name)
    {
        try
        {
            _client = new TcpClient();
            _client.Connect(ip, port);   // Bloqueante
            _client.NoDelay = true;
            _active = true;
            Connected = true;

            RunOnMain(() => SetStatus($"Conectado a {ip}:{port}. Entrando al lobby..."));

            // Lanzar hilo de lectura
            _readThread = new Thread(ReadLoop) { IsBackground = true };
            _readThread.Start();

            // Anunciar nombre al servidor
            Send($"LOBBY_JOIN:{name}");
        }
        catch (Exception ex)
        {
            RunOnMain(() =>
            {
                SetStatus($"No se pudo conectar: {ex.Message}");
                OnError.Invoke(ex.Message);
            });
            Connected = false;
            try { _client?.Close(); } catch { }
        }
    }

    // ── Hilo de lectura ───────────────────────────────────────────────────────
    private void ReadLoop()
    {
        var buf = new byte[4096];
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
        catch
        {
            // Desconexión inesperada.
        }
        finally
        {
            Connected = false;
            _active = false;
            RunOnMain(() =>
            {
                SetStatus("Desconectado del servidor.");
                ClearLobbyUI();
            });
        }
    }

    // ── Parseo de mensajes ────────────────────────────────────────────────────
    private void ParseMessage(string msg)
    {
        // ASSIGNED_ID:<id>
        if (msg.StartsWith("ASSIGNED_ID:"))
        {
            if (int.TryParse(msg.Substring("ASSIGNED_ID:".Length), out int id))
            {
                MyId = id;
                SetStatus($"En el lobby. Tu ID: {id}");
                OnAssignedId.Invoke(id);
            }
            return;
        }

        // LOBBY_STATE:2/4|0:Ana,1:Luis
        if (msg.StartsWith("LOBBY_STATE:"))
        {
            string raw = msg.Substring("LOBBY_STATE:".Length);
            ParseLobbyState(raw);
            OnLobbyState.Invoke(raw);
            return;
        }

        // GAME_START
        if (msg == "GAME_START")
        {
            SetStatus("¡La partida está comenzando!");
            OnGameStart.Invoke();
            return;
        }

        // SERVER_FULL
        if (msg == "SERVER_FULL")
        {
            SetStatus("La sala está llena. Intenta más tarde.");
            OnError.Invoke("Sala llena.");
            Disconnect();
            return;
        }

        Debug.Log($"[Client] Mensaje no reconocido: \"{msg}\"");
    }

    // ── Parseo del estado del lobby ───────────────────────────────────────────

    /// <summary>
    /// Interpreta "2/4|0:Ana,1:Luis" y actualiza los labels del lobby.
    /// </summary>
    private void ParseLobbyState(string raw)
    {
        // Separar "2/4" de "0:Ana,1:Luis"
        int sep = raw.IndexOf('|');
        string countPart = sep >= 0 ? raw.Substring(0, sep) : raw;
        string playersPart = sep >= 0 ? raw.Substring(sep + 1) : "";

        if (lobbyCountLabel)
            lobbyCountLabel.text = $"Jugadores: {countPart}";

        if (lobbyListLabel)
        {
            if (string.IsNullOrEmpty(playersPart))
            {
                lobbyListLabel.text = "Sin jugadores aún...";
                return;
            }

            var lines = new System.Text.StringBuilder();
            foreach (string entry in playersPart.Split(','))
            {
                if (string.IsNullOrWhiteSpace(entry)) continue;
                int colon = entry.IndexOf(':');
                if (colon < 0) continue;

                string playerId = entry.Substring(0, colon);
                string playerName = entry.Substring(colon + 1);

                // Marcar al jugador propio
                bool isMe = int.TryParse(playerId, out int pid) && pid == MyId;
                lines.AppendLine(isMe ? $"• {playerName} (tú)" : $"• {playerName}");
            }
            lobbyListLabel.text = lines.ToString().TrimEnd();
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Envía un mensaje al servidor.</summary>
    public void Send(string msg)
    {
        if (_client == null || !_client.Connected) return;
        try
        {
            byte[] data = Encoding.UTF8.GetBytes(msg + "\n");
            _client.GetStream().Write(data, 0, data.Length);
        }
        catch (Exception ex)
        {
            RunOnMain(() => SetStatus($"Error al enviar: {ex.Message}"));
        }
    }

    private void Disconnect()
    {
        if (!_active && !Connected) return;
        _active = false;
        Connected = false;
        MyId = -1;
        try { _client?.Close(); } catch { }
        try { _readThread?.Join(200); } catch { }
    }

    /// <summary>Encola una acción para ejecutarse en el hilo principal (Update).</summary>
    private void RunOnMain(Action action) => _mainThreadQueue.Enqueue(action);

    private void SetStatus(string text)
    {
        if (statusLabel) statusLabel.text = text;
    }

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