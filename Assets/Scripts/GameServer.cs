using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// GameServer — Fase 2: Juego básico con protocolo JSON.
///
/// Nuevas responsabilidades respecto a la fase anterior:
///   - Al iniciar la partida, genera posiciones de spawn y de runas
///     y las envía a todos los clientes.
///   - Valida collect_request: acepta el primero, deniega al resto.
///   - Valida powerup_activate (viento propio): confirma si el jugador
///     tiene usos disponibles y difunde el efecto.
///   - Reenvía player_move a todos excepto al emisor.
///   - Controla el temporizador y emite match_end al terminarse.
///
/// PROTOCOLO JSON — mensajes nuevos en esta fase:
///
///   Cliente → Servidor:
///     { "type":"player_move",      "playerId":"P0", "position":{x,z}, "state":"moviendose" }
///     { "type":"collect_request",  "playerId":"P0", "objectId":"RUNE_5", "objectType":"runa_comun" }
///     { "type":"powerup_activate", "playerId":"P0", "powerupType":"viento_propio" }
///
///   Servidor → Todos:
///     { "type":"match_start",   "sessionId":"room01", "duration":90,
///       "players":[{"id":"P0","spawnX":f,"spawnZ":f},...],
///       "runes":[{"id":"RUNE_0","x":f,"z":f,"runeType":"runa_comun"},...]  }
///     { "type":"player_move",      "playerId":"P0", "position":{x,z}, "state":"moviendose" }
///     { "type":"collect_confirm",  "playerId":"P0", "objectId":"RUNE_5",
///                                  "scoreDelta":1, "newScore":3, "objectState":"recolectada" }
///     { "type":"collect_deny",     "playerId":"P0", "objectId":"RUNE_5" }
///     { "type":"powerup_confirm",  "playerId":"P0", "powerupType":"viento_propio",
///                                  "duration":5, "state":"acelerado", "vfx":"wind_trail_green" }
///     { "type":"match_end",        "sessionId":"room01", "winnerPlayerId":"P0",
///                                  "finalScores":[{"playerId":"P0","score":7},...] }
///
///   (Los mensajes de lobby de la fase anterior se mantienen sin cambios)
/// </summary>
public class GameServer : MonoBehaviour
{
    // ── UI ─────────────────────────────────────────────────────────────────────
    [Header("UI")]
    [SerializeField] private TMP_Text ipLabel;
    [SerializeField] private TMP_Text portLabel;
    [SerializeField] private TMP_InputField portField;
    [SerializeField] private TMP_Text logArea;
    [SerializeField] private TMP_Text playerListLabel;
    [SerializeField] private Button startGameButton;

    // ── Configuración ──────────────────────────────────────────────────────────
    [Header("Configuración")]
    [SerializeField] private int defaultPort = 7777;
    [SerializeField] private int maxPlayers = 4;
    [SerializeField] private int minPlayers = 2;
    [SerializeField] private int totalRunes = 10;   // runas para la prueba básica
    [SerializeField] private float mapSize = 20f;  // tamaño del plano de prueba
    [SerializeField] private float gameDuration = 90f;
    [SerializeField] private int powerUpUses = 2;    // usos de viento por partida

    // ── Estado interno ─────────────────────────────────────────────────────────
    private TcpListener _listener;
    private Thread _acceptThread;
    private volatile bool _running = false;
    private volatile bool _gameActive = false;

    private readonly List<PlayerSession> _sessions = new();
    private readonly object _sessLock = new();
    private readonly ConcurrentQueue<string> _uiQueue = new();

    // Runas: id → recogida (true/false). Acceso bajo _runaLock.
    private readonly Dictionary<string, bool> _runas = new();
    private readonly object _runaLock = new();

    private volatile bool _pendingUIRefresh = false;
    private float _gameEndTime;
    private int _nextId = 0;
    private string _sessionId = "room01";

    // ── Ciclo Unity ────────────────────────────────────────────────────────────
    private void Start()
    {
        UpdateIpLabel();
        int p = GetPort();
        if (portLabel) portLabel.text = $"Puerto: {p}";
        if (portField && string.IsNullOrWhiteSpace(portField.text))
            portField.text = p.ToString();
        if (startGameButton) startGameButton.interactable = false;
        if (playerListLabel) playerListLabel.text = "Sin jugadores aún...";
    }

    private void Update()
    {
        while (_uiQueue.TryDequeue(out string line))
        {
            if (logArea)
            {
                logArea.text += (logArea.text.Length > 0 ? "\n" : "") + line;
                var ls = logArea.text.Split('\n');
                if (ls.Length > 80)
                    logArea.text = string.Join("\n", ls.Skip(ls.Length - 80));
            }
        }

        if (_pendingUIRefresh)
        {
            _pendingUIRefresh = false;
            RefreshLobbyUI();
        }

        // Temporizador de partida
        if (_gameActive && Time.time >= _gameEndTime)
        {
            _gameActive = false;
            BroadcastMatchEnd();
        }
    }

    private void OnApplicationQuit() => StopServer();
    private void OnDestroy() => StopServer();

    // ── Botones UI ─────────────────────────────────────────────────────────────
    public void OnCreateRoom()
    {
        if (_running) { Log("[Server] La sala ya está abierta."); return; }
        int port = GetPort();
        try
        {
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            _running = true;
            if (portLabel) portLabel.text = $"Puerto: {port}";
            UpdateIpLabel();
            Log($"[Server] Sala creada. IP: {GetBestIPv4()}  Puerto: {port}");
            DumpAllIPs();
            _acceptThread = new Thread(AcceptLoop) { IsBackground = true };
            _acceptThread.Start();
        }
        catch (Exception ex) { Log($"[Server] Error: {ex.Message}"); }
    }

    public void OnStartGame()
    {
        int count;
        lock (_sessLock) count = _sessions.Count;
        if (count < minPlayers)
        {
            Log($"[Server] Faltan jugadores ({count}/{minPlayers} mínimo).");
            return;
        }
        StartMatch();
    }

    public void OnCloseRoom() { StopServer(); Log("[Server] Sala cerrada."); }

    // ── Bucle de aceptación ────────────────────────────────────────────────────
    private void AcceptLoop()
    {
        try
        {
            while (_running)
            {
                TcpClient client = _listener.AcceptTcpClient();
                client.NoDelay = true;

                bool full;
                lock (_sessLock) full = _sessions.Count >= maxPlayers || _gameActive;

                if (full)
                {
                    SendDirect(client, BuildJson("error", "message", "Sala llena o partida en curso"));
                    client.Close();
                    continue;
                }

                int id = _nextId++;
                var session = new PlayerSession(id, client);
                lock (_sessLock) _sessions.Add(session);

                Log($"[Server] Conectado id=P{id} desde {client.Client.RemoteEndPoint}");

                // Enviar connect_ack con el ID asignado
                SendDirect(client, JsonConnectAck(id));

                var t = new Thread(() => ClientReadLoop(session)) { IsBackground = true };
                t.Start();
            }
        }
        catch (SocketException) { }
        catch (Exception ex) { Log($"[Server] AcceptLoop error: {ex.Message}"); }
    }

    // ── Bucle de lectura por cliente ───────────────────────────────────────────
    private void ClientReadLoop(PlayerSession sess)
    {
        string ep = sess.Client.Client.RemoteEndPoint?.ToString() ?? "?";
        var sb = new StringBuilder();
        var buf = new byte[8192];
        try
        {
            NetworkStream stream = sess.Client.GetStream();
            while (_running && sess.Client.Connected)
            {
                int read = stream.Read(buf, 0, buf.Length);
                if (read == 0) break;

                sb.Append(Encoding.UTF8.GetString(buf, 0, read).Replace("\r", ""));
                string raw = sb.ToString();
                int nl;
                while ((nl = raw.IndexOf('\n')) >= 0)
                {
                    string msg = raw.Substring(0, nl).Trim();
                    raw = raw.Substring(nl + 1);
                    if (!string.IsNullOrEmpty(msg))
                        HandleMessage(sess, msg);
                }
                sb.Clear();
                sb.Append(raw);
            }
        }
        catch { }
        finally
        {
            string name = string.IsNullOrEmpty(sess.Name) ? $"P{sess.Id}" : sess.Name;
            Log($"[Server] Desconectado: {name} ({ep})");
            lock (_sessLock) _sessions.Remove(sess);
            try { sess.Client.Close(); } catch { }
            BroadcastLobbyState();
            _pendingUIRefresh = true;
        }
    }

    // ── Manejo de mensajes ─────────────────────────────────────────────────────
    private void HandleMessage(PlayerSession sess, string json)
    {
        // Extraer "type" sin dependencia de JsonUtility (que requiere clases marcadas)
        string type = ExtractString(json, "type");

        switch (type)
        {
            // ── Lobby ──────────────────────────────────────────────────────────
            case "connect_request":
                sess.Name = ExtractString(json, "playerName");
                if (string.IsNullOrEmpty(sess.Name)) sess.Name = $"Jugador{sess.Id}";
                Log($"[Server] {sess.Name} se unió al lobby.");
                BroadcastLobbyState();
                _pendingUIRefresh = true;
                break;

            // ── Movimiento ─────────────────────────────────────────────────────
            case "player_move":
                if (!_gameActive) break;
                // Actualizar posición en sesión y reenviar a los demás
                sess.PosX = ExtractFloat(json, "x");
                sess.PosZ = ExtractFloat(json, "z");
                BroadcastExcept(sess, json);  // reenviar tal cual
                break;

            // ── Recolección ────────────────────────────────────────────────────
            case "collect_request":
                if (!_gameActive) break;
                HandleCollectRequest(sess, json);
                break;

            // ── Power-up viento propio ─────────────────────────────────────────
            case "powerup_activate":
                if (!_gameActive) break;
                HandlePowerupActivate(sess, json);
                break;

            default:
                Log($"[Server] Mensaje desconocido de P{sess.Id}: {type}");
                break;
        }
    }

    // ── Lógica de partida ──────────────────────────────────────────────────────

    private void StartMatch()
    {
        _runas.Clear();
        _gameActive = true;
        _gameEndTime = Time.time + gameDuration;

        var rng = new System.Random();

        // Generar spawns y resetear scores
        List<string> playersJsonItems = new();
        lock (_sessLock)
        {
            foreach (var s in _sessions)
            {
                s.Score = 0;
                s.PowerUpUses = powerUpUses;
                s.SpawnX = (float)(rng.NextDouble() * (mapSize - 4f)) + 2f;
                s.SpawnZ = (float)(rng.NextDouble() * (mapSize - 4f)) + 2f;
                playersJsonItems.Add($"{{\"id\":\"P{s.Id}\",\"spawnX\":{s.SpawnX:F2},\"spawnZ\":{s.SpawnZ:F2}}}");
            }
        }

        // Generar runas
        List<string> runesJsonItems = new();
        for (int i = 0; i < totalRunes; i++)
        {
            string rid = $"RUNE_{i}";
            float rx = (float)(rng.NextDouble() * (mapSize - 2f)) + 1f;
            float rz = (float)(rng.NextDouble() * (mapSize - 2f)) + 1f;
            lock (_runaLock) _runas[rid] = false;   // false = disponible
            runesJsonItems.Add($"{{\"id\":\"{rid}\",\"x\":{rx:F2},\"z\":{rz:F2},\"runeType\":\"runa_comun\"}}");
        }

        // Construir match_start manualmente (evita dependencia de Newtonsoft en proyectos vacíos)
        string playersJson = string.Join(",", playersJsonItems);
        string runesJson = string.Join(",", runesJsonItems);

        long ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string matchStart =
            $"{{\"type\":\"match_start\",\"sessionId\":\"{_sessionId}\"," +
            $"\"duration\":{(int)gameDuration},\"timestamp\":{ts}," +
            $"\"players\":[{playersJson}],\"runes\":[{runesJson}]}}";

        Broadcast(matchStart);
        Log($"[Server] Partida iniciada. Jugadores:{_sessions.Count}, Runas:{totalRunes}");
    }

    private void HandleCollectRequest(PlayerSession sess, string json)
    {
        string objectId = ExtractString(json, "objectId");
        string objectType = ExtractString(json, "objectType");
        long ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        bool alreadyTaken;
        lock (_runaLock)
        {
            alreadyTaken = !_runas.ContainsKey(objectId) || _runas[objectId];
            if (!alreadyTaken) _runas[objectId] = true;  // marcar como recogida
        }

        if (alreadyTaken)
        {
            // Denegar
            string deny =
                $"{{\"type\":\"collect_deny\",\"sessionId\":\"{_sessionId}\"," +
                $"\"playerId\":\"P{sess.Id}\",\"objectId\":\"{objectId}\"," +
                $"\"timestamp\":{ts}}}";
            SendDirect(sess.Client, deny);
            return;
        }

        int scoreDelta = objectType == "runa_dorada" ? 2 : 1;
        sess.Score += scoreDelta;

        // Confirmar a todos (broadcast) para que todos destruyan el objeto
        string confirm =
            $"{{\"type\":\"collect_confirm\",\"sessionId\":\"{_sessionId}\"," +
            $"\"playerId\":\"P{sess.Id}\",\"objectId\":\"{objectId}\"," +
            $"\"objectType\":\"{objectType}\",\"scoreDelta\":{scoreDelta}," +
            $"\"newScore\":{sess.Score},\"objectState\":\"recolectada\"," +
            $"\"timestamp\":{ts}}}";
        Broadcast(confirm);
        Log($"[Server] P{sess.Id} recogió {objectId}. Score={sess.Score}");
    }

    private void HandlePowerupActivate(PlayerSession sess, string json)
    {
        string powerupType = ExtractString(json, "powerupType");
        long ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        if (powerupType != "viento_propio")
        {
            Log($"[Server] Power-up '{powerupType}' no implementado aún.");
            return;
        }

        if (sess.PowerUpUses <= 0)
        {
            Log($"[Server] P{sess.Id} no tiene usos de power-up disponibles.");
            return;
        }

        sess.PowerUpUses--;

        string confirm =
            $"{{\"type\":\"powerup_confirm\",\"sessionId\":\"{_sessionId}\"," +
            $"\"playerId\":\"P{sess.Id}\",\"powerupType\":\"viento_propio\"," +
            $"\"duration\":5,\"state\":\"acelerado\",\"vfx\":\"wind_trail_green\"," +
            $"\"timestamp\":{ts}}}";
        Broadcast(confirm);
        Log($"[Server] P{sess.Id} activó viento_propio. Usos restantes: {sess.PowerUpUses}");
    }

    private void BroadcastMatchEnd()
    {
        List<string> scores = new();
        string winner = "P0";
        int topScore = -1;

        lock (_sessLock)
        {
            foreach (var s in _sessions)
            {
                scores.Add($"{{\"playerId\":\"P{s.Id}\",\"score\":{s.Score}}}");
                if (s.Score > topScore) { topScore = s.Score; winner = $"P{s.Id}"; }
            }
        }

        long ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string matchEnd =
            $"{{\"type\":\"match_end\",\"sessionId\":\"{_sessionId}\"," +
            $"\"winnerPlayerId\":\"{winner}\"," +
            $"\"finalScores\":[{string.Join(",", scores)}]," +
            $"\"state\":\"finalizada\",\"timestamp\":{ts}}}";
        Broadcast(matchEnd);
        Log($"[Server] Partida terminada. Ganador: {winner}");
    }

    // ── Lobby ──────────────────────────────────────────────────────────────────
    private void BroadcastLobbyState()
    {
        string countPart, playersPart;
        lock (_sessLock)
        {
            countPart = $"{_sessions.Count}/{maxPlayers}";
            playersPart = string.Join(",",
                _sessions.Select(s =>
                    $"{s.Id}:{(string.IsNullOrEmpty(s.Name) ? "..." : s.Name)}"));
        }
        // El lobby sigue en texto plano para no romper el GameClient del lobby
        Broadcast($"LOBBY_STATE:{countPart}|{playersPart}");
    }

    // ── Helpers de red ─────────────────────────────────────────────────────────
    private void Broadcast(string message)
    {
        byte[] data = Encoding.UTF8.GetBytes(message + "\n");
        lock (_sessLock)
        {
            for (int i = _sessions.Count - 1; i >= 0; i--)
            {
                try
                {
                    if (!_sessions[i].Client.Connected) { _sessions.RemoveAt(i); continue; }
                    _sessions[i].Client.GetStream().Write(data, 0, data.Length);
                }
                catch { _sessions.RemoveAt(i); }
            }
        }
    }

    private void BroadcastExcept(PlayerSession exclude, string message)
    {
        byte[] data = Encoding.UTF8.GetBytes(message + "\n");
        lock (_sessLock)
        {
            foreach (var s in _sessions)
            {
                if (s == exclude) continue;
                try { s.Client.GetStream().Write(data, 0, data.Length); } catch { }
            }
        }
    }

    private static void SendDirect(TcpClient client, string message)
    {
        try
        {
            byte[] data = Encoding.UTF8.GetBytes(message + "\n");
            client.GetStream().Write(data, 0, data.Length);
        }
        catch { }
    }

    private void StopServer()
    {
        if (!_running) return;
        _running = false;
        _gameActive = false;
        try { _listener?.Stop(); } catch { }
        lock (_sessLock)
        {
            foreach (var s in _sessions) try { s.Client.Close(); } catch { }
            _sessions.Clear();
        }
        try { _acceptThread?.Join(300); } catch { }
    }

    // ── Helpers UI ─────────────────────────────────────────────────────────────
    private void RefreshLobbyUI()
    {
        int count; string list;
        lock (_sessLock)
        {
            count = _sessions.Count;
            list = string.Join("\n",
                _sessions.Select(s =>
                    $"• {(string.IsNullOrEmpty(s.Name) ? $"Jugador {s.Id}" : s.Name)}"));
        }
        if (playerListLabel)
            playerListLabel.text = count == 0 ? "Sin jugadores aún..." : list;
        if (startGameButton)
            startGameButton.interactable = count >= minPlayers;
    }

    private void Log(string line) => _uiQueue.Enqueue(line);

    private void UpdateIpLabel()
    {
        if (ipLabel) ipLabel.text = $"IP: {GetBestIPv4()}";
    }

    private void DumpAllIPs()
    {
        string[] ips = GetAllIPv4s();
        if (ips.Length == 0) { Log("[Server] No se encontraron IPs."); return; }
        Log("[Server] IPs disponibles:");
        foreach (string ip in ips) Log("  → " + ip);
    }

    private int GetPort()
    {
        if (portField && int.TryParse(portField.text, out int p) && p > 0 && p <= 65535)
            return p;
        return defaultPort;
    }

    // ── JSON helpers (sin Newtonsoft) ──────────────────────────────────────────

    /// <summary>Extrae el valor de una clave string del JSON. Ej: "type":"player_move" → "player_move"</summary>
    public static string ExtractString(string json, string key)
    {
        string search = $"\"{key}\"";
        int ki = json.IndexOf(search, StringComparison.Ordinal);
        if (ki < 0) return "";
        int colon = json.IndexOf(':', ki + search.Length);
        if (colon < 0) return "";
        int start = json.IndexOf('"', colon + 1);
        if (start < 0) return "";
        int end = json.IndexOf('"', start + 1);
        if (end < 0) return "";
        return json.Substring(start + 1, end - start - 1);
    }

    /// <summary>Extrae el valor de una clave numérica del JSON. Ej: "x":34.5 → 34.5f</summary>
    public static float ExtractFloat(string json, string key)
    {
        string search = $"\"{key}\"";
        int ki = json.IndexOf(search, StringComparison.Ordinal);
        if (ki < 0) return 0f;
        int colon = json.IndexOf(':', ki + search.Length);
        if (colon < 0) return 0f;
        // Saltar espacios
        int vi = colon + 1;
        while (vi < json.Length && (json[vi] == ' ' || json[vi] == '\t')) vi++;
        // Leer hasta coma, } o espacio
        int end = vi;
        while (end < json.Length && json[end] != ',' && json[end] != '}' &&
               json[end] != ' ' && json[end] != '\n') end++;
        string numStr = json.Substring(vi, end - vi);
        return float.TryParse(numStr,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out float val) ? val : 0f;
    }

    private static string BuildJson(string type, string key, string value)
        => $"{{\"type\":\"{type}\",\"{key}\":\"{value}\"}}";

    private static string JsonConnectAck(int id)
    {
        long ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return $"{{\"type\":\"connect_ack\",\"playerId\":\"P{id}\",\"timestamp\":{ts}}}";
    }

    // ── Detección de IP ────────────────────────────────────────────────────────
    private static string[] GetAllIPv4s()
    {
        var result = new List<string>();
        foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
            foreach (UnicastIPAddressInformation ua in ni.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                byte[] b = ua.Address.GetAddressBytes();
                if (b[0] == 169 && b[1] == 254) continue;
                result.Add(ua.Address.ToString());
            }
        }
        return result.Distinct().ToArray();
    }

    private static string GetBestIPv4()
    {
        string[] ips = GetAllIPv4s();
        if (ips.Length == 0) return "0.0.0.0";
        string[] wifiHints = { "wlan", "wifi", "wlo", "wl ", "wlp" };
        foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            string desc = (ni.Name + " " + ni.Description).ToLowerInvariant();
            if (!wifiHints.Any(h => desc.Contains(h))) continue;
            foreach (UnicastIPAddressInformation ua in ni.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                string ip = ua.Address.ToString();
                if (ips.Contains(ip)) return ip;
            }
        }
        return ips[0];
    }

    // ── PlayerSession ──────────────────────────────────────────────────────────
    private class PlayerSession
    {
        public int Id;
        public string Name = "";
        public TcpClient Client;
        public int Score = 0;
        public int PowerUpUses = 0;
        public float PosX, PosZ;
        public float SpawnX, SpawnZ;

        public PlayerSession(int id, TcpClient client) { Id = id; Client = client; }
    }
}