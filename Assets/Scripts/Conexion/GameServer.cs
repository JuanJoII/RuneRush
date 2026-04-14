using System;
using System.Collections;
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
/// GameServer — Fase 3.
/// Cambios:
///   - Mapa 100×100.
///   - Viento propio: objeto en el mapa (objectType "powerup_viento"). Al recogerlo,
///     collect_confirm incluye vientoDuration. No se necesita powerup_activate.
///   - Meteoros: el servidor los lanza automáticamente cada meteorInterval segundos.
///     Flujo: meteor_spawn → (fallDuration s) → zone_blocked → (blockDuration s) → zone_expired.
///   - Portal propio: cliente envía powerup_activate "portal_propio".
///     Servidor responde powerup_confirm con destinationPosition aleatoria en broadcast.
/// </summary>
public class GameServer : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TMP_Text ipLabel;
    [SerializeField] private TMP_Text portLabel;
    [SerializeField] private TMP_InputField portField;
    [SerializeField] private TMP_Text logArea;
    [SerializeField] private TMP_Text playerListLabel;
    [SerializeField] private Button startGameButton;

    [Header("Configuración")]
    [SerializeField] private int defaultPort = 7777;
    [SerializeField] private int maxPlayers = 4;
    [SerializeField] private int minPlayers = 2;
    [SerializeField] private float mapSize = 300f;
    [SerializeField] private float gameDuration = 90f;

    [Header("Runas")]
    [SerializeField] private int totalRunas = 100;

    [Header("Power-up Viento (objeto en mapa)")]
    [SerializeField] private int totalVientoItems = 5;
    [SerializeField] private float vientoDuration = 5f;

    [Header("Meteoros")]
    [SerializeField] private float meteorInterval = 10f;
    [SerializeField] private float meteorFallDuration = 3f;
    [SerializeField] private float meteorRadius = 3.5f;
    [SerializeField] private float meteorBlockDuration = 5f;

    [Header("Portal propio")]
    [SerializeField] private int portalUses = 2;

    // Estado interno
    private TcpListener _listener;
    private Thread _acceptThread;
    private volatile bool _running = false;
    private volatile bool _gameActive = false;

    private readonly List<PlayerSession> _sessions = new();
    private readonly object _sessLock = new();
    private readonly ConcurrentQueue<string> _uiQueue = new();
    private readonly Dictionary<string, bool> _collectibles = new();
    private readonly object _collectLock = new();

    private volatile bool _pendingUIRefresh = false;
    private float _gameEndTime;
    private float _nextMeteorTime;
    private int _meteorCounter = 0;
    private int _nextId = 0;
    private const string SessionId = "room01";

    private void Start()
    {
        UpdateIpLabel();
        int p = GetPort();
        if (portLabel) portLabel.text = $"Puerto: {p}";
        if (portField && string.IsNullOrWhiteSpace(portField.text)) portField.text = p.ToString();
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
                if (ls.Length > 80) logArea.text = string.Join("\n", ls.Skip(ls.Length - 80));
            }
        }

        if (_pendingUIRefresh) { _pendingUIRefresh = false; RefreshLobbyUI(); }

        if (_gameActive)
        {
            if (Time.time >= _gameEndTime) { _gameActive = false; BroadcastMatchEnd(); }
            else if (Time.time >= _nextMeteorTime) { _nextMeteorTime = Time.time + meteorInterval; LaunchMeteor(); }
        }
    }

    private void OnApplicationQuit() => StopServer();
    private void OnDestroy() => StopServer();

    // Botones UI
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
        int count; lock (_sessLock) count = _sessions.Count;
        if (count < minPlayers) { Log($"[Server] Faltan jugadores ({count}/{minPlayers})."); return; }
        StartMatch();
    }

    public void OnCloseRoom() { StopServer(); Log("[Server] Sala cerrada."); }

    // Aceptación
    private void AcceptLoop()
    {
        try
        {
            while (_running)
            {
                TcpClient client = _listener.AcceptTcpClient();
                client.NoDelay = true;
                bool full; lock (_sessLock) full = _sessions.Count >= maxPlayers || _gameActive;
                if (full) { SendDirect(client, "{\"type\":\"error\",\"message\":\"Sala llena\"}"); client.Close(); continue; }

                int id = _nextId++;
                var sess = new PlayerSession(id, client);
                lock (_sessLock) _sessions.Add(sess);
                Log($"[Server] Conectado P{id} desde {client.Client.RemoteEndPoint}");
                SendDirect(client, JsonConnectAck(id));
                new Thread(() => ClientReadLoop(sess)) { IsBackground = true }.Start();
            }
        }
        catch (SocketException) { }
        catch (Exception ex) { Log($"[Server] AcceptLoop: {ex.Message}"); }
    }

    // Lectura por cliente
    private void ClientReadLoop(PlayerSession sess)
    {
        string ep = sess.Client.Client.RemoteEndPoint?.ToString() ?? "?";
        var sb = new StringBuilder(); var buf = new byte[8192];
        try
        {
            NetworkStream stream = sess.Client.GetStream();
            while (_running && sess.Client.Connected)
            {
                int read = stream.Read(buf, 0, buf.Length);
                if (read == 0) break;
                sb.Append(Encoding.UTF8.GetString(buf, 0, read).Replace("\r", ""));
                string raw = sb.ToString(); int nl;
                while ((nl = raw.IndexOf('\n')) >= 0)
                {
                    string msg = raw.Substring(0, nl).Trim();
                    raw = raw.Substring(nl + 1);
                    if (!string.IsNullOrEmpty(msg)) HandleMessage(sess, msg);
                }
                sb.Clear(); sb.Append(raw);
            }
        }
        catch { }
        finally
        {
            Log($"[Server] Desconectado: {(string.IsNullOrEmpty(sess.Name) ? $"P{sess.Id}" : sess.Name)} ({ep})");
            lock (_sessLock) _sessions.Remove(sess);
            try { sess.Client.Close(); } catch { }
            BroadcastLobbyState();
            _pendingUIRefresh = true;
        }
    }

    // Mensajes
    private void HandleMessage(PlayerSession sess, string json)
    {
        string type = ExtractString(json, "type");
        switch (type)
        {
            case "connect_request":
                sess.Name = ExtractString(json, "playerName");
                if (string.IsNullOrEmpty(sess.Name)) sess.Name = $"Jugador{sess.Id}";
                Log($"[Server] {sess.Name} en lobby.");
                BroadcastLobbyState(); _pendingUIRefresh = true;
                break;

            case "player_move":
                if (!_gameActive) break;
                sess.PosX = ExtractFloatInObject(json, "position", "x");
                sess.PosZ = ExtractFloatInObject(json, "position", "z");
                BroadcastExcept(sess, json);
                break;

            case "collect_request":
                if (!_gameActive) break;
                HandleCollect(sess, json);
                break;

            case "powerup_activate":
                if (!_gameActive) break;
                HandlePowerupActivate(sess, json);
                break;

            default:
                Log($"[Server] Tipo desconocido de P{sess.Id}: \"{type}\"");
                break;
        }
    }

    // Inicio de partida
    private void StartMatch()
    {
        _collectibles.Clear();
        _gameActive = true;
        _gameEndTime = Time.time + gameDuration;
        _nextMeteorTime = Time.time + meteorInterval;
        _meteorCounter = 0;

        var rng = new System.Random();
        // === BLOQUE CORREGIDO - Reemplaza el anterior completo ===
        var playersParts = new List<string>();
        lock (_sessLock)
        {
            var rnga = new System.Random();   // mejor crear uno aquí

            foreach (var s in _sessions)
            {
                s.Score = 0;
                s.PortalUses = portalUses;

                // Generar posiciones correctamente
                s.SpawnX = (float)(rnga.NextDouble() * (mapSize - 10f)) + 5f;
                s.SpawnZ = (float)(rnga.NextDouble() * (mapSize - 10f)) + 5f;

                // JSON seguro (SIN :F2 dentro del f-string)
                string playerJson = "{" +
                    $"\"id\":\"P{s.Id}\"," +
                    $"\"spawnX\":{s.SpawnX}," +           // sin :F2
                    $"\"spawnZ\":{s.SpawnZ}" +            // sin :F2
                    "}";

                playersParts.Add(playerJson);

                Debug.Log($"[Server Spawn] P{s.Id} → ({s.SpawnX:F2}, {s.SpawnZ:F2})");
            }
        }

        var objectParts = new List<string>();
        for (int i = 0; i < totalRunas; i++)
        {
            string id = $"RUNE_{i}";
            float x = (float)(rng.NextDouble() * (mapSize - 4f)) + 2f;
            float z = (float)(rng.NextDouble() * (mapSize - 4f)) + 2f;
            lock (_collectLock) _collectibles[id] = false;
            objectParts.Add($"{{\"id\":\"{id}\",\"x\":{x:F2},\"z\":{z:F2},\"objectType\":\"runa_comun\"}}");
        }
        for (int i = 0; i < totalVientoItems; i++)
        {
            string id = $"VIENTO_{i}";
            float x = (float)(rng.NextDouble() * (mapSize - 4f)) + 2f;
            float z = (float)(rng.NextDouble() * (mapSize - 4f)) + 2f;
            lock (_collectLock) _collectibles[id] = false;
            objectParts.Add($"{{\"id\":\"{id}\",\"x\":{x:F2},\"z\":{z:F2},\"objectType\":\"powerup_viento\"}}");
        }

        long ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Broadcast(
            $"{{\"type\":\"match_start\",\"sessionId\":\"{SessionId}\"," +
            $"\"duration\":{(int)gameDuration},\"timestamp\":{ts}," +
            $"\"players\":[{string.Join(",", playersParts)}]," +
            $"\"objects\":[{string.Join(",", objectParts)}]}}");
        Log($"[Server] Partida iniciada. Runas:{totalRunas} Viento:{totalVientoItems}");
    }

    // Recolección
    private void HandleCollect(PlayerSession sess, string json)
    {
        string objectId = ExtractString(json, "objectId");
        string objectType = ExtractString(json, "objectType");
        long ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        bool alreadyTaken;
        lock (_collectLock)
        {
            alreadyTaken = !_collectibles.ContainsKey(objectId) || _collectibles[objectId];
            if (!alreadyTaken) _collectibles[objectId] = true;
        }

        if (alreadyTaken)
        {
            SendDirect(sess.Client,
                $"{{\"type\":\"collect_deny\",\"playerId\":\"P{sess.Id}\"," +
                $"\"objectId\":\"{objectId}\",\"timestamp\":{ts}}}");
            return;
        }

        int scoreDelta = objectType == "runa_comun" ? 1 : 0;
        sess.Score += scoreDelta;

        // Si es powerup_viento, añadir duración para que el cliente aplique el boost
        string extra = objectType == "powerup_viento"
            ? $",\"vientoDuration\":{vientoDuration},\"vfx\":\"wind_trail_green\""
            : "";

        Broadcast(
            $"{{\"type\":\"collect_confirm\",\"sessionId\":\"{SessionId}\"," +
            $"\"playerId\":\"P{sess.Id}\",\"objectId\":\"{objectId}\"," +
            $"\"objectType\":\"{objectType}\",\"scoreDelta\":{scoreDelta}," +
            $"\"newScore\":{sess.Score},\"objectState\":\"recolectada\"," +
            $"\"timestamp\":{ts}{extra}}}");
        Log($"[Server] P{sess.Id} recogió {objectId} ({objectType}). Score={sess.Score}");
    }

    // Portal propio
    private void HandlePowerupActivate(PlayerSession sess, string json)
    {
        string powerupType = ExtractString(json, "powerupType");
        long ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        if (powerupType != "portal_propio")
        {
            Log($"[Server] Power-up '{powerupType}' no implementado aún.");
            return;
        }

        if (sess.PortalUses <= 0)
        {
            SendDirect(sess.Client, "{\"type\":\"error\",\"message\":\"Sin usos de portal\"}");
            return;
        }

        sess.PortalUses--;

        var rng = new System.Random();
        float destX = (float)(rng.NextDouble() * (mapSize - 10f)) + 5f;
        float destZ = (float)(rng.NextDouble() * (mapSize - 10f)) + 5f;

        // JSON LIMPIO y seguro (misma forma que usamos con meteoros y zonas)
        string msg = "{" +
            $"\"type\":\"powerup_confirm\"," +
            $"\"sessionId\":\"{SessionId}\"," +
            $"\"playerId\":\"P{sess.Id}\"," +
            $"\"powerupType\":\"portal_propio\"," +
            $"\"destinationPosition\":{{\"x\":{destX},\"z\":{destZ}}}," +
            $"\"vfx\":\"portal_self_teleport\"," +
            $"\"timestamp\":{ts}" +
        "}";

        Broadcast(msg);

        Debug.Log($"[Server Portal] P{sess.Id} → teletransportado a ({destX:F2}, {destZ:F2}) | Usos restantes: {sess.PortalUses}");
    }

    // Meteoros
    private void LaunchMeteor()
    {
        var rng = new System.Random();
        float tx = (float)(rng.NextDouble() * (mapSize - 20f)) + 10f;
        float tz = (float)(rng.NextDouble() * (mapSize - 20f)) + 10f;
        string meteorId = $"METEOR_{_meteorCounter++}";
        long ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // JSON muy seguro y limpio
        string msg = $"{{\"type\":\"meteor_spawn\",\"sessionId\":\"{SessionId}\"," +
                    $"\"meteorId\":\"{meteorId}\"," +
                    $"\"targetPosition\":{{\"x\":{tx},\"z\":{tz}}}," +
                    $"\"impactRadius\":{meteorRadius}," +
                    $"\"blockDuration\":{(int)meteorBlockDuration}," +
                    $"\"fallDuration\":{(int)meteorFallDuration}," +
                    $"\"timestamp\":{ts}}}";

        Broadcast(msg);

        Debug.Log($"[Server Meteor] Lanzado {meteorId} → ({tx:F2}, {tz:F2})");

        StartCoroutine(MeteorImpactRoutine(meteorId, tx, tz));
    }

    private IEnumerator MeteorImpactRoutine(string meteorId, float tx, float tz)
    {
        yield return new WaitForSeconds(meteorFallDuration);

        long ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // JSON LIMPIO y seguro para zone_blocked
        string msg = "{" +
            $"\"type\":\"zone_blocked\"," +
            $"\"sessionId\":\"{SessionId}\"," +
            $"\"meteorId\":\"{meteorId}\"," +
            $"\"position\":{{\"x\":{tx},\"z\":{tz}}}," +
            $"\"radius\":{meteorRadius}," +
            $"\"duration\":{(int)meteorBlockDuration}," +
            $"\"timestamp\":{ts}" +
        "}";

        Broadcast(msg);

        Debug.Log($"[Server ZoneBlocked] {meteorId} → posición ({tx:F2}, {tz:F2}) radio={meteorRadius}");

        yield return new WaitForSeconds(meteorBlockDuration);

        ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Broadcast(
            $"{{\"type\":\"zone_expired\",\"sessionId\":\"{SessionId}\"," +
            $"\"meteorId\":\"{meteorId}\",\"timestamp\":{ts}}}");
    }

    // Fin de partida
    private void BroadcastMatchEnd()
    {
        var scores = new List<string>(); string winner = "P0"; int topScore = -1;
        lock (_sessLock)
        {
            foreach (var s in _sessions)
            {
                scores.Add($"{{\"playerId\":\"P{s.Id}\",\"score\":{s.Score}}}");
                if (s.Score > topScore) { topScore = s.Score; winner = $"P{s.Id}"; }
            }
        }
        long ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Broadcast(
            $"{{\"type\":\"match_end\",\"sessionId\":\"{SessionId}\"," +
            $"\"winnerPlayerId\":\"{winner}\"," +
            $"\"finalScores\":[{string.Join(",", scores)}]," +
            $"\"state\":\"finalizada\",\"timestamp\":{ts}}}");
        Log($"[Server] Fin de partida. Ganador: {winner}");
    }

    // Lobby
    private void BroadcastLobbyState()
    {
        string countPart, playersPart;
        lock (_sessLock)
        {
            countPart = $"{_sessions.Count}/{maxPlayers}";
            playersPart = string.Join(",",
                _sessions.Select(s => $"{s.Id}:{(string.IsNullOrEmpty(s.Name) ? "..." : s.Name)}"));
        }
        Broadcast($"LOBBY_STATE:{countPart}|{playersPart}");
    }

    // Red
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
        try { byte[] d = Encoding.UTF8.GetBytes(message + "\n"); client.GetStream().Write(d, 0, d.Length); } catch { }
    }

    private void StopServer()
    {
        if (!_running) return;
        _running = _gameActive = false;
        try { _listener?.Stop(); } catch { }
        lock (_sessLock) { foreach (var s in _sessions) try { s.Client.Close(); } catch { } _sessions.Clear(); }
        try { _acceptThread?.Join(300); } catch { }
    }

    // UI
    private void RefreshLobbyUI()
    {
        int count; string list;
        lock (_sessLock)
        {
            count = _sessions.Count;
            list = string.Join("\n", _sessions.Select(s => $"• {(string.IsNullOrEmpty(s.Name) ? $"Jugador {s.Id}" : s.Name)}"));
        }
        if (playerListLabel) playerListLabel.text = count == 0 ? "Sin jugadores aún..." : list;
        if (startGameButton) startGameButton.interactable = count >= minPlayers;
    }

    private void Log(string line) => _uiQueue.Enqueue(line);
    private void UpdateIpLabel() { if (ipLabel) ipLabel.text = $"IP: {GetBestIPv4()}"; }
    private void DumpAllIPs()
    {
        string[] ips = GetAllIPv4s();
        if (ips.Length == 0) { Log("[Server] No se encontraron IPs."); return; }
        Log("[Server] IPs disponibles:"); foreach (string ip in ips) Log("  → " + ip);
    }
    private int GetPort()
    {
        if (portField && int.TryParse(portField.text, out int p) && p > 0 && p <= 65535) return p;
        return defaultPort;
    }

    // JSON helpers
    // JSON helpers — VERSIÓN MEJORADA (reemplaza las antiguas)
    public static string ExtractString(string json, string key)
    {
        string search = $"\"{key}\":\"";
        int start = json.IndexOf(search, StringComparison.Ordinal);
        if (start < 0) return "";

        start += search.Length;
        int end = json.IndexOf('"', start);
        if (end < 0) return "";

        return json.Substring(start, end - start);
    }

    public static float ExtractFloat(string json, string key)
    {
        string search = $"\"{key}\":";
        int start = json.IndexOf(search, StringComparison.Ordinal);
        if (start < 0) return 0f;

        start += search.Length;
        while (start < json.Length && char.IsWhiteSpace(json[start])) start++;

        int end = start;
        while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '.' || json[end] == '-' || json[end] == '+'))
            end++;

        string valueStr = json.Substring(start, end - start).Trim();
        return float.TryParse(valueStr, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float val) ? val : 0f;
    }

    public static float ExtractFloatInObject(string json, string objectKey, string fieldKey)
    {
        // Busca el objeto completo {"x":..., "z":...}
        string search = $"\"{objectKey}\":";
        int objStart = json.IndexOf(search, StringComparison.Ordinal);
        if (objStart < 0) return 0f;

        int braceStart = json.IndexOf('{', objStart);
        if (braceStart < 0) return 0f;

        int braceEnd = json.IndexOf('}', braceStart);
        if (braceEnd < 0) return 0f;

        string objContent = json.Substring(braceStart, braceEnd - braceStart + 1);
        return ExtractFloat(objContent, fieldKey);
    }

    private static string JsonConnectAck(int id)
    {
        long ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return $"{{\"type\":\"connect_ack\",\"playerId\":\"P{id}\",\"timestamp\":{ts}}}";
    }

    // Detección de IP
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

    private class PlayerSession
    {
        public int Id; public string Name = ""; public TcpClient Client;
        public int Score = 0; public int PortalUses = 0;
        public float PosX, PosZ, SpawnX, SpawnZ;
        public PlayerSession(int id, TcpClient client) { Id = id; Client = client; }
    }
}