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
/// GameServer — Fase 1: Conexión y Lobby.
///
/// Qué hace este script:
///   - El host pulsa "Crear sala": levanta el TcpListener.
///   - Acepta hasta 4 jugadores. Si llega un 5º, lo rechaza.
///   - A cada cliente que se conecta le asigna un ID y espera que mande su nombre.
///   - Cada vez que alguien entra o sale, hace broadcast del estado del lobby
///     para que todos vean la lista actualizada.
///   - El host pulsa "Iniciar partida": broadcast GAME_START (solo si hay >= 2).
///
/// PROTOCOLO (solo esta fase):
///
///   Cliente → Servidor:
///     LOBBY_JOIN:<nombre>
///
///   Servidor → Cliente (solo al nuevo):
///     ASSIGNED_ID:<id>
///
///   Servidor → Todos (broadcast):
///     LOBBY_STATE:<conectados>/<max>|<id>:<nombre>,<id>:<nombre>,...
///     GAME_START
///     SERVER_FULL
/// </summary>
public class GameServer : MonoBehaviour
{
    // ── Referencias UI ────────────────────────────────────────────────────────
    [Header("UI")]
    [SerializeField] private TMP_Text ipLabel;
    [SerializeField] private TMP_Text portLabel;
    [SerializeField] private TMP_InputField portField;
    [SerializeField] private TMP_Text logArea;
    [SerializeField] private TMP_Text playerListLabel;
    [SerializeField] private Button startGameButton;

    // ── Configuración ─────────────────────────────────────────────────────────
    [Header("Configuración")]
    [SerializeField] private int defaultPort = 7777;
    [SerializeField] private int maxPlayers = 4;
    [SerializeField] private int minPlayers = 2;

    // ── Estado interno ────────────────────────────────────────────────────────
    private TcpListener _listener;
    private Thread _acceptThread;
    private volatile bool _running = false;

    private readonly List<PlayerSession> _sessions = new();
    private readonly object _sessLock = new();
    private readonly ConcurrentQueue<string> _uiQueue = new();

    // Señal para refrescar el botón y la lista desde el hilo principal
    private volatile bool _pendingUIRefresh = false;

    private int _nextId = 0;

    // ── Ciclo Unity ───────────────────────────────────────────────────────────
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
        // Volcar mensajes de log encolados desde hilos de red
        while (_uiQueue.TryDequeue(out string line))
        {
            if (logArea)
            {
                logArea.text += (logArea.text.Length > 0 ? "\n" : "") + line;
                // Limitar a 80 líneas
                var ls = logArea.text.Split('\n');
                if (ls.Length > 80)
                    logArea.text = string.Join("\n", ls.Skip(ls.Length - 80));
            }
        }

        // Refrescar lista de jugadores y botón de inicio (solo en hilo principal)
        if (_pendingUIRefresh)
        {
            _pendingUIRefresh = false;
            RefreshLobbyUI();
        }
    }

    private void OnApplicationQuit() => StopServer();
    private void OnDestroy() => StopServer();

    // ── Botones UI ────────────────────────────────────────────────────────────

    /// <summary>
    /// El host pulsa "Crear sala".
    /// </summary>
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
            Log("[Server] Esperando jugadores...");
            DumpAllIPs();

            _acceptThread = new Thread(AcceptLoop) { IsBackground = true };
            _acceptThread.Start();
        }
        catch (Exception ex)
        {
            Log($"[Server] Error al crear sala: {ex.Message}");
        }
    }

    /// <summary>
    /// El host pulsa "Iniciar partida".
    /// </summary>
    public void OnStartGame()
    {
        int count;
        lock (_sessLock) count = _sessions.Count;

        if (count < minPlayers)
        {
            Log($"[Server] Faltan jugadores (hay {count}, mínimo {minPlayers}).");
            return;
        }

        Log("[Server] ¡Partida iniciada!");
        Broadcast("GAME_START");
    }

    /// <summary>
    /// El host pulsa "Cerrar sala".
    /// </summary>
    public void OnCloseRoom()
    {
        StopServer();
        Log("[Server] Sala cerrada.");
    }

    // ── Bucle de aceptación (hilo) ────────────────────────────────────────────
    private void AcceptLoop()
    {
        try
        {
            while (_running)
            {
                TcpClient client = _listener.AcceptTcpClient();
                client.NoDelay = true;

                bool full;
                lock (_sessLock) full = _sessions.Count >= maxPlayers;

                if (full)
                {
                    Log("[Server] Conexión rechazada: sala llena.");
                    SendDirect(client, "SERVER_FULL\n");
                    client.Close();
                    continue;
                }

                int id = _nextId++;
                var session = new PlayerSession(id, client);
                lock (_sessLock) _sessions.Add(session);

                Log($"[Server] Nueva conexión: {client.Client.RemoteEndPoint} → id={id}");

                // Enviar ID asignado directamente a este cliente
                SendDirect(client, $"ASSIGNED_ID:{id}\n");

                // Lanzar hilo de lectura para este cliente
                var t = new Thread(() => ClientReadLoop(session)) { IsBackground = true };
                t.Start();
            }
        }
        catch (SocketException)
        {
            // Normal al detener el servidor.
        }
        catch (Exception ex)
        {
            Log($"[Server] Error en AcceptLoop: {ex.Message}");
        }
    }

    // ── Bucle de lectura por cliente (hilo) ───────────────────────────────────
    private void ClientReadLoop(PlayerSession sess)
    {
        string endpoint = sess.Client.Client.RemoteEndPoint?.ToString() ?? "?";
        var sb = new StringBuilder();
        var buf = new byte[4096];

        try
        {
            NetworkStream stream = sess.Client.GetStream();

            while (_running && sess.Client.Connected)
            {
                int read = stream.Read(buf, 0, buf.Length);
                if (read == 0) break; // El cliente cerró la conexión limpiamente.

                sb.Append(Encoding.UTF8.GetString(buf, 0, read).Replace("\r", ""));

                // Extraer mensajes completos (delimitados por \n)
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
                sb.Append(raw); // Guardar fragmento incompleto
            }
        }
        catch
        {
            // Desconexión abrupta.
        }
        finally
        {
            string name = string.IsNullOrEmpty(sess.Name) ? $"id={sess.Id}" : sess.Name;
            Log($"[Server] Desconectado: {name} ({endpoint})");

            lock (_sessLock) _sessions.Remove(sess);
            try { sess.Client.Close(); } catch { }

            // Notificar a los demás que alguien salió
            BroadcastLobbyState();
            _pendingUIRefresh = true;
        }
    }

    // ── Manejo de mensajes ────────────────────────────────────────────────────
    private void HandleMessage(PlayerSession sess, string msg)
    {
        if (msg.StartsWith("LOBBY_JOIN:"))
        {
            string name = msg.Substring("LOBBY_JOIN:".Length).Trim();
            sess.Name = string.IsNullOrEmpty(name) ? $"Jugador{sess.Id}" : name;

            Log($"[Server] {sess.Name} se unió al lobby.");
            BroadcastLobbyState();
            _pendingUIRefresh = true;
        }
        else
        {
            Log($"[Server] Mensaje desconocido de id={sess.Id}: \"{msg}\"");
        }
    }

    // ── Broadcast del estado del lobby ────────────────────────────────────────

    /// <summary>
    /// Envía a todos: LOBBY_STATE:2/4|0:Ana,1:Luis
    /// </summary>
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
        Broadcast($"LOBBY_STATE:{countPart}|{playersPart}");
    }

    // ── Helpers de red ────────────────────────────────────────────────────────
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

    private static void SendDirect(TcpClient client, string message)
    {
        try
        {
            byte[] data = Encoding.UTF8.GetBytes(message);
            client.GetStream().Write(data, 0, data.Length);
        }
        catch { }
    }

    private void StopServer()
    {
        if (!_running) return;
        _running = false;
        try { _listener?.Stop(); } catch { }
        lock (_sessLock)
        {
            foreach (var s in _sessions) try { s.Client.Close(); } catch { }
            _sessions.Clear();
        }
        try { _acceptThread?.Join(300); } catch { }
    }

    // ── Helpers UI ────────────────────────────────────────────────────────────

    /// <summary>
    /// Actualiza la lista visual de jugadores y el estado del botón Iniciar.
    /// Solo llamar desde el hilo principal (Update/LateUpdate).
    /// </summary>
    private void RefreshLobbyUI()
    {
        int count;
        string list;
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
        if (ips.Length == 0) { Log("[Server] No se encontraron IPs válidas."); return; }
        Log("[Server] IPs disponibles:");
        foreach (string ip in ips) Log("  → " + ip);
    }

    private int GetPort()
    {
        if (portField && int.TryParse(portField.text, out int p) && p > 0 && p <= 65535)
            return p;
        return defaultPort;
    }

    // ── Detección de IP ───────────────────────────────────────────────────────
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

    // ── PlayerSession ─────────────────────────────────────────────────────────
    private class PlayerSession
    {
        public int Id;
        public string Name = "";
        public TcpClient Client;

        public PlayerSession(int id, TcpClient client)
        {
            Id = id;
            Client = client;
        }
    }
}