using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// <summary>
/// GameManager — Fase 2: Instanciación y sincronización básica.
///
/// Este script vive en la escena GameScene.
/// En su Start() lee el JSON de match_start que GameClient guardó antes de
/// cargar la escena y lo usa para:
///   - Instanciar una cápsula por cada jugador en su posición de spawn.
///   - Instanciar esferas como runas en sus posiciones.
///   - Marcar al jugador local con una cámara que lo siga.
///
/// También se suscribe a los eventos de GameClient para:
///   - Mover cápsulas remotas cuando llega player_move.
///   - Destruir runas cuando llega collect_confirm.
///   - Aplicar viento propio cuando llega powerup_confirm.
///   - Mostrar resultados cuando llega match_end.
/// </summary>
public class GameManager : MonoBehaviour
{
    // ── Prefabs ────────────────────────────────────────────────────────────────
    [Header("Prefabs (asignar en Inspector)")]
    [SerializeField] private GameObject playerPrefab;  // Cápsula con PlayerController
    [SerializeField] private GameObject runaPrefab;    // Esfera pequeña

    // ── Colores de jugadores ───────────────────────────────────────────────────
    [Header("Colores")]
    [SerializeField]
    private Color[] playerColors = new Color[]
    {
        new Color(0.9f, 0.2f, 0.2f),   // Rojo
        new Color(0.2f, 0.4f, 0.9f),   // Azul
        new Color(0.2f, 0.8f, 0.3f),   // Verde
        new Color(0.9f, 0.8f, 0.1f),   // Amarillo
    };

    // ── Cámara ─────────────────────────────────────────────────────────────────
    [Header("Cámara")]
    [SerializeField] private float camHeight = 12f;
    [SerializeField] private float camDistance = 8f;

    // ── HUD ────────────────────────────────────────────────────────────────────
    [Header("HUD")]
    [SerializeField] private TMP_Text timerLabel;
    [SerializeField] private TMP_Text scoreLabel;
    [SerializeField] private TMP_Text powerupLabel;   // Usos de viento disponibles
    [SerializeField] private GameObject resultsPanel;
    [SerializeField] private TMP_Text resultsLabel;

    // ── Estado interno ─────────────────────────────────────────────────────────
    private readonly Dictionary<string, GameObject> _players = new();  // "P0" → GameObject
    private readonly Dictionary<string, GameObject> _runas = new();  // "RUNE_0" → GameObject

    private Camera _cam;
    private GameObject _localPlayer;
    private string _localId = "";

    private int _myScore = 0;
    private int _powerupUses = 0;  // se actualiza con confirm
    private float _matchEndTime;
    private bool _matchRunning = false;

    // ── Unity ──────────────────────────────────────────────────────────────────
    private void Start()
    {
        _cam = Camera.main;
        _localId = GameClient.Instance ? GameClient.Instance.PlayerId : "";

        // Suscribirse a eventos de red
        if (GameClient.Instance)
        {
            GameClient.Instance.OnPlayerMove.AddListener(OnPlayerMove);
            GameClient.Instance.OnCollectConfirm.AddListener(OnCollectConfirm);
            GameClient.Instance.OnCollectDeny.AddListener(OnCollectDeny);
            GameClient.Instance.OnPowerupConfirm.AddListener(OnPowerupConfirm);
            GameClient.Instance.OnMatchEnd.AddListener(OnMatchEnd);
        }

        // Leer el JSON de match_start que el cliente guardó antes de cargar la escena
        string matchJson = GameClient.Instance ? GameClient.Instance.PendingMatchStart : "";
        if (!string.IsNullOrEmpty(matchJson))
        {
            GameClient.Instance.ClearPendingMatchStart();
            InitMatch(matchJson);
        }
        else
        {
            Debug.LogWarning("[GameManager] No se encontró PendingMatchStart. " +
                             "¿Entraste a la escena directamente? Inicializando demo local.");
            InitMatchDemo();
        }

        if (resultsPanel) resultsPanel.SetActive(false);
    }

    private void OnDestroy()
    {
        if (GameClient.Instance)
        {
            GameClient.Instance.OnPlayerMove.RemoveListener(OnPlayerMove);
            GameClient.Instance.OnCollectConfirm.RemoveListener(OnCollectConfirm);
            GameClient.Instance.OnCollectDeny.RemoveListener(OnCollectDeny);
            GameClient.Instance.OnPowerupConfirm.RemoveListener(OnPowerupConfirm);
            GameClient.Instance.OnMatchEnd.RemoveListener(OnMatchEnd);
        }
    }

    private void Update()
    {
        // Cámara sigue al jugador local
        if (_localPlayer && _cam)
        {
            Vector3 target = _localPlayer.transform.position
                             + Vector3.up * camHeight
                             + Vector3.back * camDistance;
            _cam.transform.position =
                Vector3.Lerp(_cam.transform.position, target, Time.deltaTime * 6f);
            _cam.transform.LookAt(_localPlayer.transform.position + Vector3.up * 0.5f);
        }

        // Temporizador
        if (_matchRunning)
        {
            float remaining = Mathf.Max(0f, _matchEndTime - Time.time);
            if (timerLabel) timerLabel.text = $"{Mathf.CeilToInt(remaining):00}s";
            if (remaining <= 0f) _matchRunning = false;
        }
    }

    // ── Inicialización ─────────────────────────────────────────────────────────

    /// <summary>Inicializa la partida con el JSON de match_start.</summary>
    private void InitMatch(string json)
    {
        // Extraer duración
        float duration = GameServer.ExtractFloat(json, "duration");
        if (duration <= 0f) duration = 90f;
        _matchEndTime = Time.time + duration;
        _matchRunning = true;

        // Parsear jugadores: "players":[{"id":"P0","spawnX":f,"spawnZ":f},...]
        ParseAndSpawnPlayers(json);

        // Parsear runas: "runes":[{"id":"RUNE_0","x":f,"z":f,"runeType":"runa_comun"},...]
        ParseAndSpawnRunes(json);
    }

    /// <summary>Demo local sin servidor (para pruebas de escena en Editor).</summary>
    private void InitMatchDemo()
    {
        _matchEndTime = Time.time + 90f;
        _matchRunning = true;
        _localId = "P0";

        SpawnPlayer("P0", 5f, 5f, 0, isLocal: true);
        SpawnPlayer("P1", 15f, 15f, 1, isLocal: false);

        SpawnRuna("RUNE_0", 10f, 10f);
        SpawnRuna("RUNE_1", 3f, 17f);
        SpawnRuna("RUNE_2", 18f, 4f);
    }

    // ── Parseo de jugadores ────────────────────────────────────────────────────
    private void ParseAndSpawnPlayers(string json)
    {
        // Buscar el array "players":[...]
        string arrayContent = ExtractArray(json, "players");
        if (string.IsNullOrEmpty(arrayContent)) return;

        int colorIndex = 0;
        foreach (string entry in SplitJsonObjects(arrayContent))
        {
            string pid = GameServer.ExtractString(entry, "id");
            float spawnX = GameServer.ExtractFloat(entry, "spawnX");
            float spawnZ = GameServer.ExtractFloat(entry, "spawnZ");
            bool isLocal = pid == _localId;
            SpawnPlayer(pid, spawnX, spawnZ, colorIndex++, isLocal);
        }
    }

    private void SpawnPlayer(string pid, float x, float z, int colorIdx, bool isLocal)
    {
        if (playerPrefab == null)
        {
            // Crear cápsula por código si no hay prefab asignado
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.transform.position = new Vector3(x, 1f, z);
            go.name = pid;
            ApplyColor(go, colorIdx);
            AddNameTag(go, isLocal ? $"{pid} (tú)" : pid);

            if (isLocal)
            {
                var pc = go.AddComponent<PlayerController>();
                pc.PlayerId = pid;
                _localPlayer = go;
            }
            else
            {
                var rpc = go.AddComponent<RemotePlayerSync>();
                rpc.PlayerId = pid;
            }

            _players[pid] = go;
        }
        else
        {
            var go = Instantiate(playerPrefab, new Vector3(x, 1f, z), Quaternion.identity);
            go.name = pid;
            ApplyColor(go, colorIdx);
            AddNameTag(go, isLocal ? $"{pid} (tú)" : pid);

            if (isLocal)
            {
                var pc = go.GetComponent<PlayerController>();
                if (pc == null) pc = go.AddComponent<PlayerController>();
                pc.PlayerId = pid;
                _localPlayer = go;
            }
            else
            {
                var rpc = go.GetComponent<RemotePlayerSync>();
                if (rpc == null) rpc = go.AddComponent<RemotePlayerSync>();
                rpc.PlayerId = pid;
            }

            _players[pid] = go;
        }
    }

    // ── Parseo de runas ────────────────────────────────────────────────────────
    private void ParseAndSpawnRunes(string json)
    {
        string arrayContent = ExtractArray(json, "runes");
        if (string.IsNullOrEmpty(arrayContent)) return;

        foreach (string entry in SplitJsonObjects(arrayContent))
        {
            string rid = GameServer.ExtractString(entry, "id");
            float rx = GameServer.ExtractFloat(entry, "x");
            float rz = GameServer.ExtractFloat(entry, "z");
            SpawnRuna(rid, rx, rz);
        }
    }

    private void SpawnRuna(string runaId, float x, float z)
    {
        GameObject go;
        if (runaPrefab != null)
        {
            go = Instantiate(runaPrefab, new Vector3(x, 0.5f, z), Quaternion.identity);
        }
        else
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.transform.position = new Vector3(x, 0.5f, z);
            go.transform.localScale = Vector3.one * 0.5f;
            // Color dorado
            var mat = go.GetComponent<Renderer>().material;
            mat.color = new Color(1f, 0.85f, 0.1f);
        }
        go.name = runaId;

        // Agregar componente RunaObject
        var runa = go.AddComponent<RunaObject>();
        runa.RunaId = runaId;

        // Asegurar trigger
        var col = go.GetComponent<Collider>();
        if (col) col.isTrigger = true;

        _runas[runaId] = go;
    }

    // ── Handlers de eventos de red ─────────────────────────────────────────────

    private void OnPlayerMove(string json)
    {
        string pid = GameServer.ExtractString(json, "playerId");
        if (pid == _localId) return;  // Ignorar nuestro propio rebote

        float x = GameServer.ExtractFloat(json, "x");
        float z = GameServer.ExtractFloat(json, "z");

        // Buscar dentro del objeto "position": { "x":..., "z":... }
        // ExtractFloat ya busca la clave en todo el JSON; si hay ambigüedad con x/z
        // en posición anidada, necesitamos extracción más precisa.
        // Como solo hay un "x" y un "z" en player_move, funciona directo.

        if (_players.TryGetValue(pid, out GameObject go))
        {
            var rpc = go.GetComponent<RemotePlayerSync>();
            if (rpc) rpc.SetTarget(new Vector3(x, 1f, z));
        }
    }

    private void OnCollectConfirm(string json)
    {
        string objectId = GameServer.ExtractString(json, "objectId");
        string pid = GameServer.ExtractString(json, "playerId");
        int delta = (int)GameServer.ExtractFloat(json, "scoreDelta");
        int newScore = (int)GameServer.ExtractFloat(json, "newScore");

        // Destruir la runa en todos los clientes
        if (_runas.TryGetValue(objectId, out GameObject go))
        {
            _runas.Remove(objectId);
            Destroy(go);
        }

        // Actualizar score si somos el que recogió
        if (pid == _localId)
        {
            _myScore = newScore;
            if (scoreLabel) scoreLabel.text = $"Runas: {_myScore}";
            Debug.Log($"[GameManager] Recogiste {objectId} (+{delta}). Total: {newScore}");
        }
    }

    private void OnCollectDeny(string json)
    {
        string objectId = GameServer.ExtractString(json, "objectId");
        Debug.Log($"[GameManager] Recolección denegada: {objectId} ya fue tomada.");
        // Aquí podrías mostrar un feedback visual breve al jugador local
    }

    private void OnPowerupConfirm(string json)
    {
        string pid = GameServer.ExtractString(json, "playerId");
        string powerupType = GameServer.ExtractString(json, "powerupType");
        float duration = GameServer.ExtractFloat(json, "duration");

        if (!_players.TryGetValue(pid, out GameObject go)) return;

        if (powerupType == "viento_propio")
        {
            // Aplicar boost al PlayerController (local) o RemotePlayerSync (remoto)
            var pc = go.GetComponent<PlayerController>();
            if (pc) pc.ApplySpeedBoost(duration);

            var rpc = go.GetComponent<RemotePlayerSync>();
            if (rpc) rpc.ApplySpeedBoost(duration);

            if (pid == _localId)
                Debug.Log($"[GameManager] Viento propio activo por {duration}s.");
        }
    }

    private void OnMatchEnd(string json)
    {
        _matchRunning = false;
        string winner = GameServer.ExtractString(json, "winnerPlayerId");

        if (resultsPanel) resultsPanel.SetActive(true);
        if (resultsLabel)
        {
            // Parsear finalScores
            string scoresArray = ExtractArray(json, "finalScores");
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== RESULTADOS ===\n");
            int rank = 1;
            foreach (string entry in SplitJsonObjects(scoresArray))
            {
                string pid = GameServer.ExtractString(entry, "playerId");
                int score = (int)GameServer.ExtractFloat(entry, "score");
                string mark = pid == winner ? " ★" : "";
                sb.AppendLine($"#{rank++}  {pid}{mark}  →  {score} runas");
            }
            resultsLabel.text = sb.ToString();
        }
    }

    // ── Helpers de UI ──────────────────────────────────────────────────────────
    private void ApplyColor(GameObject go, int colorIdx)
    {
        var renderer = go.GetComponent<Renderer>();
        if (!renderer) renderer = go.GetComponentInChildren<Renderer>();
        if (renderer && colorIdx < playerColors.Length)
            renderer.material.color = playerColors[colorIdx];
    }

    private void AddNameTag(GameObject go, string label)
    {
        // Crear un objeto hijo con TextMesh para el nombre flotante
        var child = new GameObject("NameTag");
        child.transform.SetParent(go.transform);
        child.transform.localPosition = new Vector3(0f, 1.4f, 0f);

        var tm = child.AddComponent<TextMesh>();
        tm.text = label;
        tm.characterSize = 0.15f;
        tm.fontSize = 40;
        tm.alignment = TextAlignment.Center;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.color = Color.white;
    }

    // ── Helpers de JSON ────────────────────────────────────────────────────────

    /// <summary>
    /// Extrae el contenido de un array JSON por nombre de clave.
    /// Ej: "players":[{...},{...}] → "{...},{...}"
    /// </summary>
    private static string ExtractArray(string json, string key)
    {
        string search = $"\"{key}\"";
        int ki = json.IndexOf(search);
        if (ki < 0) return "";
        int start = json.IndexOf('[', ki + search.Length);
        if (start < 0) return "";

        int depth = 0; int i = start;
        for (; i < json.Length; i++)
        {
            if (json[i] == '[') depth++;
            else if (json[i] == ']') { depth--; if (depth == 0) break; }
        }
        return json.Substring(start + 1, i - start - 1);
    }

    /// <summary>
    /// Divide una cadena de objetos JSON en la raíz: "{...},{...}" → ["{...}", "{...}"]
    /// </summary>
    private static List<string> SplitJsonObjects(string content)
    {
        var result = new List<string>();
        int depth = 0; int start = -1;
        for (int i = 0; i < content.Length; i++)
        {
            if (content[i] == '{') { if (depth == 0) start = i; depth++; }
            else if (content[i] == '}')
            {
                depth--;
                if (depth == 0 && start >= 0)
                {
                    result.Add(content.Substring(start, i - start + 1));
                    start = -1;
                }
            }
        }
        return result;
    }
}