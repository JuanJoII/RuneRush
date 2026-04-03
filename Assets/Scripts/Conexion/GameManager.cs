using System;
using System.Collections;
using System.Collections.Generic;
using RuneRush.Player;
using UnityEngine;
using TMPro;

/// <summary>
/// GameManager — Fase 3.
/// Nuevo respecto a fase anterior:
///   - Parsea "objects" en lugar de "runes" del match_start (incluye runas y powerup_viento).
///   - OnCollectConfirm: si objectType es "powerup_viento", aplica speed boost al jugador local.
///   - OnPowerupConfirm "portal_propio": teletransporta al jugador (local o remoto) al destino.
///   - OnMeteorSpawn: instancia un cubo cayendo visualmente (cápsula invertida).
///   - OnZoneBlocked: instancia un cilindro sólido como zona bloqueada.
///   - OnZoneExpired: destruye la zona bloqueada correspondiente.
/// </summary>
public class GameManager : MonoBehaviour
{
    [Header("Prefabs (opcionales, usa primitivas si están vacíos)")]
    [SerializeField] private GameObject playerPrefab;
    [SerializeField] private GameObject runaPrefab;
    [SerializeField] private GameObject vientoPrefab;    // Power-up viento propio
    [SerializeField] private GameObject meteorPrefab;    // Bola cayendo (visual)
    [SerializeField] private GameObject zonaPrefab;      // Zona bloqueada

    [Header("Colores de jugadores")]
    [SerializeField]
    private Color[] playerColors = new Color[]
    {
        new Color(0.9f, 0.2f, 0.2f),
        new Color(0.2f, 0.4f, 0.9f),
        new Color(0.2f, 0.8f, 0.3f),
        new Color(0.9f, 0.8f, 0.1f),
    };

    // [Header("Cámara")]
    // [SerializeField] private float camHeight = 18f;
    // [SerializeField] private float camDistance = 14f;

    [Header("HUD")]
    [SerializeField] private TMP_Text scoreLabel;
    [SerializeField] private TMP_Text timerLabel;
    [SerializeField] private TMP_Text portalUsesLabel;
    [SerializeField] private TMP_Text statusLabel;
    [SerializeField] private GameObject resultsPanel;
    [SerializeField] private TMP_Text resultsLabel;

    // Estado
    private readonly Dictionary<string, GameObject> _players = new();
    private readonly Dictionary<string, GameObject> _objects = new();  // runas + vientos
    private readonly Dictionary<string, GameObject> _zonas = new();  // meteorId → zona
    private readonly Dictionary<string, GameObject> _meteoros = new();  // meteorId → visual

    private Camera _cam;
    private GameObject _localPlayer;
    private string _localId = "";
    private int _myScore = 0;
    private int _portalUses = 0;  // se deduce de los confirm
    private float _matchEndTime;
    private bool _matchRunning = false;
    private int _colorIndex = 0;

    private void Start()
    {
        _cam = Camera.main;
        _localId = GameClient.Instance ? GameClient.Instance.PlayerId : "";

        if (GameClient.Instance)
        {
            GameClient.Instance.OnPlayerMove.AddListener(OnPlayerMove);
            GameClient.Instance.OnCollectConfirm.AddListener(OnCollectConfirm);
            GameClient.Instance.OnCollectDeny.AddListener(OnCollectDeny);
            GameClient.Instance.OnPowerupConfirm.AddListener(OnPowerupConfirm);
            GameClient.Instance.OnMeteorSpawn.AddListener(OnMeteorSpawn);
            GameClient.Instance.OnZoneBlocked.AddListener(OnZoneBlocked);
            GameClient.Instance.OnZoneExpired.AddListener(OnZoneExpired);
            GameClient.Instance.OnMatchEnd.AddListener(OnMatchEnd);
        }

        string matchJson = GameClient.Instance ? GameClient.Instance.PendingMatchStart : "";
        if (!string.IsNullOrEmpty(matchJson))
        {
            GameClient.Instance.ClearPendingMatchStart();
            InitMatch(matchJson);
        }
        else
        {
            InitDemo();
        }

        if (resultsPanel) resultsPanel.SetActive(false);
        UpdateHUD();
    }

    private void OnDestroy()
    {
        if (!GameClient.Instance) return;
        GameClient.Instance.OnPlayerMove.RemoveListener(OnPlayerMove);
        GameClient.Instance.OnCollectConfirm.RemoveListener(OnCollectConfirm);
        GameClient.Instance.OnCollectDeny.RemoveListener(OnCollectDeny);
        GameClient.Instance.OnPowerupConfirm.RemoveListener(OnPowerupConfirm);
        GameClient.Instance.OnMeteorSpawn.RemoveListener(OnMeteorSpawn);
        GameClient.Instance.OnZoneBlocked.RemoveListener(OnZoneBlocked);
        GameClient.Instance.OnZoneExpired.RemoveListener(OnZoneExpired);
        GameClient.Instance.OnMatchEnd.RemoveListener(OnMatchEnd);
    }

    private void Update()
    {
        // // Cámara sigue al jugador local
        // if (_localPlayer && _cam)
        // {
        //     Vector3 target = _localPlayer.transform.position
        //                      + Vector3.up * camHeight
        //                      + Vector3.back * camDistance;
        //     _cam.transform.position =
        //         Vector3.Lerp(_cam.transform.position, target, Time.deltaTime * 6f);
        //     _cam.transform.LookAt(_localPlayer.transform.position + Vector3.up * 0.5f);
        // }

        // Timer
        if (_matchRunning)
        {
            float rem = Mathf.Max(0f, _matchEndTime - Time.time);
            if (timerLabel) timerLabel.text = $"{Mathf.CeilToInt(rem):00}s";
            if (rem <= 0f) _matchRunning = false;
        }
    }

    // ── Inicialización ─────────────────────────────────────────────────────────
    private void InitMatch(string json)
    {
        float duration = GameServer.ExtractFloat(json, "duration");
        if (duration <= 0f) duration = 90f;
        _matchEndTime = Time.time + duration;
        _matchRunning = true;

        ParseAndSpawnPlayers(json);
        ParseAndSpawnObjects(json);   // "objects" incluye runas y power-ups viento
    }

    private void InitDemo()
    {
        _matchEndTime = Time.time + 90f;
        _matchRunning = true;
        _localId = "P0";
        SpawnPlayer("P0", 10f, 10f, isLocal: true);
        SpawnPlayer("P1", 90f, 90f, isLocal: false);
        SpawnObject("RUNE_0", 50f, 50f, "runa_comun");
        SpawnObject("VIENTO_0", 30f, 70f, "powerup_viento");
    }

    // ── Parseo de jugadores ────────────────────────────────────────────────────
    private void ParseAndSpawnPlayers(string json)
    {
        string arr = ExtractArray(json, "players");

        _colorIndex = 0;

        foreach (string entry in SplitJsonObjects(arr))
        {
            string pid = GameServer.ExtractString(entry, "id");
            float sx = GameServer.ExtractFloat(entry, "spawnX");
            float sz = GameServer.ExtractFloat(entry, "spawnZ");

            SpawnPlayer(pid, sx, sz, pid == _localId);
        }
    }

    private void SpawnPlayer(string pid, float x, float z, bool isLocal)
    {

        Color color = _colorIndex < playerColors.Length
            ? playerColors[_colorIndex++] : Color.white;

        GameObject go = playerPrefab
            ? Instantiate(playerPrefab, new Vector3(x, 1f, z), Quaternion.identity)
            : CreateCapsule(pid, new Vector3(x, 1f, z), color);

        go.name = pid;
        ApplyColor(go, color);
        AddNameTag(go, isLocal ? $"{pid} (tú)" : pid);

        if (isLocal)
        {
            var pc = go.GetComponent<PlayerManager>() ?? go.AddComponent<PlayerManager>();
            pc.PlayerId = pid;
            _localPlayer = go;
        }
        else
        {
            var rpc = go.GetComponent<RemotePlayerSync>() ?? go.AddComponent<RemotePlayerSync>();
            rpc.PlayerId = pid;
        }

        _players[pid] = go;
    }

    // ── Parseo de objetos del mapa ─────────────────────────────────────────────
    private void ParseAndSpawnObjects(string json)
    {
        string arr = ExtractArray(json, "objects");
        foreach (string entry in SplitJsonObjects(arr))
        {
            string id = GameServer.ExtractString(entry, "id");
            float x = GameServer.ExtractFloat(entry, "x");
            float z = GameServer.ExtractFloat(entry, "z");
            string objectType = GameServer.ExtractString(entry, "objectType");
            SpawnObject(id, x, z, objectType);
        }
    }

    private void SpawnObject(string id, float x, float z, string objectType)
    {
        GameObject go;
        if (objectType == "powerup_viento")
        {
            go = vientoPrefab
                ? Instantiate(vientoPrefab, new Vector3(x, 0.5f, z), Quaternion.identity)
                : CreateSphere(id, new Vector3(x, 0.5f, z), new Color(0.4f, 0.9f, 1f), 0.6f);
        }
        else  // runa_comun
        {
            go = runaPrefab
                ? Instantiate(runaPrefab, new Vector3(x, 0.5f, z), Quaternion.identity)
                : CreateSphere(id, new Vector3(x, 0.5f, z), new Color(1f, 0.85f, 0.1f), 0.4f);
        }

        go.name = id;

        var runa = go.GetComponent<RunaObject>() ?? go.AddComponent<RunaObject>();
        runa.RunaId = id;
        runa.ObjectType = objectType;

        var col = go.GetComponent<Collider>();
        if (col) col.isTrigger = true;

        _objects[id] = go;
    }

    // ── Handlers de eventos de red ─────────────────────────────────────────────
    private void OnPlayerMove(string json)
    {
        string pid = GameServer.ExtractString(json, "playerId");
        float x = GameServer.ExtractFloatInObject(json, "position", "x");
        float z = GameServer.ExtractFloatInObject(json, "position", "z");

        if (pid == _localId) return;

        if (_players.TryGetValue(pid, out GameObject go))
        {
            var rpc = go.GetComponent<RemotePlayerSync>();
            string animState = GameServer.ExtractString(json, "state");
            if (rpc) rpc.SetTargetFromMove(new Vector3(x, 1f, z), animState);
        }
    }

    private void OnCollectConfirm(string json)
    {
        string objectId = GameServer.ExtractString(json, "objectId");
        string pid = GameServer.ExtractString(json, "playerId");
        string objectType = GameServer.ExtractString(json, "objectType");
        int newScore = (int)GameServer.ExtractFloat(json, "newScore");

        Debug.Log($"[Collect Confirm] {pid} recogió {objectId} ({objectType}) → Nuevo score: {newScore}");

        // Destruir el objeto en todos los clientes
        if (_objects.TryGetValue(objectId, out GameObject go))
        {
            _objects.Remove(objectId);
            Destroy(go);
            Debug.Log($"[Collect Confirm] Objeto {objectId} destruido visualmente");
        }

        if (pid != _localId) return;

        if (objectType == "powerup_viento")
        {
            float duration = GameServer.ExtractFloat(json, "vientoDuration");
            if (duration <= 0f) duration = 5f;
            var pc = _localPlayer ? _localPlayer.GetComponent<PlayerManager>() : null;
            if (pc) pc.ApplySpeedBoost(duration);
            if (statusLabel) statusLabel.text = $"¡Viento activo {duration}s!";
            Debug.Log($"[Collect Confirm] Power-up viento aplicado por {duration}s");
        }
        else
        {
            _myScore = newScore;
            if (scoreLabel) scoreLabel.text = $"Runas: {_myScore}";
            Debug.Log($"[Collect Confirm] Score actualizado a {_myScore}");
        }

        UpdateHUD();
    }

    private void OnCollectDeny(string json)
    {
        // La runa ya fue tomada: no hacer nada visual, la destrucción
        // la habrá hecho el collect_confirm que llegó al otro jugador.
        Debug.Log($"[GameManager] Recolección denegada: {GameServer.ExtractString(json, "objectId")}");
    }

    private void OnPowerupConfirm(string json)
    {
        string pid = GameServer.ExtractString(json, "playerId");
        string powerupType = GameServer.ExtractString(json, "powerupType");

        if (powerupType == "portal_propio")
        {
            float destX = GameServer.ExtractFloatInObject(json, "destinationPosition", "x");
            float destZ = GameServer.ExtractFloatInObject(json, "destinationPosition", "z");

            Debug.Log($"[OnPowerupConfirm] Portal de {pid} → destino ({destX:F2}, {destZ:F2})");

            if (_players.TryGetValue(pid, out GameObject go))
            {
                Vector3 dest = new Vector3(destX, 1f, destZ);

                if (pid == _localId)
                {
                    // Teletransporte local
                    var cc = go.GetComponent<CharacterController>();
                    if (cc)
                    {
                        cc.enabled = false;
                        go.transform.position = dest;
                        cc.enabled = true;
                    }
                    else
                    {
                        go.transform.position = dest;
                    }
                    if (statusLabel) statusLabel.text = "¡Portal activado!";
                }
                else
                {
                    // Teletransporte remoto
                    var rpc = go.GetComponent<RemotePlayerSync>();
                    if (rpc) rpc.SetTarget(dest);
                    else go.transform.position = dest;
                }
            }
        }
    }

    // ── Meteoros ───────────────────────────────────────────────────────────────
    private void OnMeteorSpawn(string json)
    {
        string meteorId = GameServer.ExtractString(json, "meteorId");
        float tx = GameServer.ExtractFloatInObject(json, "targetPosition", "x");
        float tz = GameServer.ExtractFloatInObject(json, "targetPosition", "z");
        float fallDur = GameServer.ExtractFloat(json, "fallDuration");
        if (fallDur <= 0f) fallDur = 3f;

        Debug.Log($"[OnMeteorSpawn] Recibido {meteorId} → target ({tx:F2}, {tz:F2})  fallDur={fallDur}");

        // Anti-duplicado fuerte
        if (_meteoros.ContainsKey(meteorId))
        {
            return;
        }

        Vector3 startPos = new Vector3(tx, 60f, tz);
        Vector3 endPos = new Vector3(tx, 0.5f, tz);

        GameObject meteorGo = meteorPrefab
            ? Instantiate(meteorPrefab, startPos, Quaternion.identity)
            : CreateSphere(meteorId + "_meteor", startPos, new Color(1f, 0.4f, 0.1f), 1.5f);

        meteorGo.name = meteorId + "_visual";
        _meteoros[meteorId] = meteorGo;

        var col = meteorGo.GetComponent<Collider>();
        if (col) col.enabled = false;

        StartCoroutine(AnimateMeteorFall(meteorGo, startPos, endPos, fallDur, meteorId));
        if (statusLabel) statusLabel.text = $"¡Meteoro {meteorId} cayendo!";
    }

    // ← NUEVO COROUTINE CON LIMPIEZA CORRECTA
    private IEnumerator AnimateMeteorFall(GameObject go, Vector3 from, Vector3 to, float duration, string meteorId)
    {
        float elapsed = 0f;
        while (elapsed < duration && go != null)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            go.transform.position = Vector3.Lerp(from, to, t);
            go.transform.Rotate(Vector3.right, 180f * Time.deltaTime);
            yield return null;
        }

        // Limpieza correcta: destruimos y quitamos del diccionario
        if (go != null)
        {
            Destroy(go);
            if (!string.IsNullOrEmpty(meteorId) && _meteoros.ContainsKey(meteorId))
                _meteoros.Remove(meteorId);
        }
    }

    private void OnZoneBlocked(string json)
    {
        string meteorId = GameServer.ExtractString(json, "meteorId");
        float px = GameServer.ExtractFloatInObject(json, "position", "x");
        float pz = GameServer.ExtractFloatInObject(json, "position", "z");
        float radius = GameServer.ExtractFloat(json, "radius");
        if (radius <= 0f) radius = 3.5f;

        Debug.Log($"[OnZoneBlocked] Recibido {meteorId} → posición ({px:F2}, {pz:F2}) radio={radius}");

        // Eliminar visual del meteoro si aún existe
        if (_meteoros.TryGetValue(meteorId, out GameObject meteorVis))
        {
            _meteoros.Remove(meteorId);
            if (meteorVis) Destroy(meteorVis);
        }

        // Crear zona bloqueada
        GameObject zona = zonaPrefab
            ? Instantiate(zonaPrefab, new Vector3(px, 0.5f, pz), Quaternion.identity)
            : CreateCylinder(meteorId + "_zona", new Vector3(px, 0f, pz), radius);

        zona.name = meteorId + "_zona";

        var renderer = zona.GetComponent<Renderer>();
        if (renderer)
            renderer.material.color = new Color(0.8f, 0.1f, 0.1f, 0.6f);

        var col = zona.GetComponent<Collider>();
        if (col) col.isTrigger = false;

        _zonas[meteorId] = zona;

        if (statusLabel) statusLabel.text = $"¡Zona bloqueada en ({px:F1}, {pz:F1})!";
    }

    private void OnZoneExpired(string json)
    {
        string meteorId = GameServer.ExtractString(json, "meteorId");
        if (_zonas.TryGetValue(meteorId, out GameObject zona))
        {
            _zonas.Remove(meteorId);
            Destroy(zona);
        }
        if (statusLabel) statusLabel.text = "Zona libre.";
    }

    private void OnMatchEnd(string json)
    {
        _matchRunning = false;
        string winner = GameServer.ExtractString(json, "winnerPlayerId");
        if (resultsPanel) resultsPanel.SetActive(true);
        if (resultsLabel)
        {
            string scoresArr = ExtractArray(json, "finalScores");
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== RESULTADOS ===\n");
            int rank = 1;
            foreach (string entry in SplitJsonObjects(scoresArr))
            {
                string pid = GameServer.ExtractString(entry, "playerId");
                int score = (int)GameServer.ExtractFloat(entry, "score");
                sb.AppendLine($"#{rank++}  {pid}{(pid == winner ? " ★" : "")}  →  {score} runas");
            }
            resultsLabel.text = sb.ToString();
        }
    }

    // ── Botón de portal en HUD ─────────────────────────────────────────────────
    /// <summary>Llamar desde botón UI "Portal" del HUD.</summary>
    public void OnPortalButtonPressed()
    {
        GameClient.Instance?.SendPowerupActivate("portal_propio");
    }

    // ── Helpers de creación de primitivas ─────────────────────────────────────
    private static GameObject CreateCapsule(string name, Vector3 pos, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.transform.position = pos; go.name = name;
        go.GetComponent<Renderer>().material.color = color;
        return go;
    }

    private static GameObject CreateSphere(string name, Vector3 pos, Color color, float scale = 0.5f)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.transform.position = pos;
        go.transform.localScale = Vector3.one * scale;
        go.name = name;
        go.GetComponent<Renderer>().material.color = color;
        return go;
    }

    private static GameObject CreateCylinder(string name, Vector3 pos, float radius)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.transform.position = pos + Vector3.up * 0.5f;
        go.transform.localScale = new Vector3(radius * 2f, 0.5f, radius * 2f);
        go.name = name;
        return go;
    }

    private static void ApplyColor(GameObject go, Color color)
    {
        var r = go.GetComponent<Renderer>() ?? go.GetComponentInChildren<Renderer>();
        if (r) r.material.color = color;
    }

    private static void AddNameTag(GameObject go, string label)
    {
        var child = new GameObject("NameTag");
        child.transform.SetParent(go.transform);
        child.transform.localPosition = new Vector3(0f, 1.5f, 0f);
        var tm = child.AddComponent<TextMesh>();
        tm.text = label; tm.characterSize = 0.15f; tm.fontSize = 40;
        tm.alignment = TextAlignment.Center; tm.anchor = TextAnchor.MiddleCenter;
        tm.color = Color.white;
    }

    private void UpdateHUD()
    {
        if (scoreLabel) scoreLabel.text = $"Runas: {_myScore}";
    }

    // ── JSON array helpers ─────────────────────────────────────────────────────
    private static string ExtractArray(string json, string key)
    {
        string search = $"\"{key}\":";
        int start = json.IndexOf(search, StringComparison.Ordinal);
        if (start < 0) return "";

        start = json.IndexOf('[', start);
        if (start < 0) return "";

        int depth = 1;
        int i = start + 1;
        for (; i < json.Length; i++)
        {
            if (json[i] == '[') depth++;
            else if (json[i] == ']')
            {
                depth--;
                if (depth == 0) break;
            }
        }
        return json.Substring(start + 1, i - start - 1);
    }

    private static List<string> SplitJsonObjects(string content)
    {
        var result = new List<string>();
        int depth = 0;
        int start = -1;

        for (int i = 0; i < content.Length; i++)
        {
            if (content[i] == '{')
            {
                if (depth == 0) start = i;
                depth++;
            }
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