using System;
using System.Collections.Generic;
using RuneRush.Player;
using UnityEngine;

/// <summary>
/// GameManager — orquesta la partida en el cliente.
///
/// Responsabilidades:
///   - Spawnear jugadores y objetos del mapa al recibir match_start.
///   - Delegar toda la lógica de HUD a HUDManager.
///   - Propagar eventos de red a RemotePlayerSync y PlayerManager.
///   - Manejar meteoros y zonas bloqueadas.
///
/// NO maneja cámara (CameraController lo hace).
/// NO escribe en ningún TMP_Text directamente (HUDManager lo hace).
/// NO cambia materiales en runtime — usa uno de 4 prefabs según orden de llegada.
/// </summary>
public class GameManager : MonoBehaviour
{
    // ── Prefabs ───────────────────────────────────────────────────────────────
    [Header("Prefabs de jugador (uno por color, en orden de asignación)")]
    [SerializeField] private GameObject[] playerPrefabs = new GameObject[4];

    [Header("Prefabs de objetos")]
    [SerializeField] private GameObject runaPrefab;
    [SerializeField] private GameObject vientoPrefab;

    // ── Managers ──────────────────────────────────────────────────────────────
    [Header("Referencias")]
    [SerializeField] private HUDManager   hudManager;
    [SerializeField] private VFXController vfxController; // gestiona meteoros y zonas

    // ── Estado ────────────────────────────────────────────────────────────────
    private readonly Dictionary<string, GameObject> _players  = new();
    private readonly Dictionary<string, GameObject> _objects  = new();
    private readonly Dictionary<string, GameObject> _zonas    = new();
    private readonly Dictionary<string, GameObject> _meteoros = new();

    private GameObject _localPlayer;
    private string     _localId       = "";
    private int        _prefabIndex   = 0;
    private float      _matchEndTime;
    private bool       _matchRunning  = false;

    // ── Ciclo Unity ───────────────────────────────────────────────────────────
    private void Start()
    {
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

        hudManager?.HideResults();
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
        if (!_matchRunning) return;

        float remaining = Mathf.Max(0f, _matchEndTime - Time.time);
        hudManager?.SetTimer(remaining);
        if (remaining <= 0f) _matchRunning = false;
    }

    // ── Inicialización ────────────────────────────────────────────────────────
    private void InitMatch(string json)
    {
        float duration = GameServer.ExtractFloat(json, "duration");
        if (duration <= 0f) duration = 90f;
        _matchEndTime = Time.time + duration;
        _matchRunning = true;

        ParseAndSpawnPlayers(json);
        ParseAndSpawnObjects(json);
    }

    private void InitDemo()
    {
        _matchEndTime = Time.time + 90f;
        _matchRunning = true;
        _localId = "P0";
        SpawnPlayer("P0", 10f, 10f, isLocal: true);
        SpawnPlayer("P1", 90f, 90f, isLocal: false);
        SpawnObject("RUNE_0",  50f, 50f, "runa_comun");
        SpawnObject("VIENTO_0", 30f, 70f, "powerup_viento");
    }

    // ── Spawn de jugadores ────────────────────────────────────────────────────
    private void ParseAndSpawnPlayers(string json)
    {
        _prefabIndex = 0;
        string arr = ExtractArray(json, "players");
        foreach (string entry in SplitJsonObjects(arr))
        {
            string pid = GameServer.ExtractString(entry, "id");
            float  sx  = GameServer.ExtractFloat(entry, "spawnX");
            float  sz  = GameServer.ExtractFloat(entry, "spawnZ");
            SpawnPlayer(pid, sx, sz, pid == _localId);
        }
    }

    private void SpawnPlayer(string pid, float x, float z, bool isLocal)
    {
        // Elegir prefab por índice — cada jugador recibe un color diferente.
        // Si no hay prefabs asignados se crea una cápsula de primitiva como fallback.
        GameObject prefab = (_prefabIndex < playerPrefabs.Length)
            ? playerPrefabs[_prefabIndex]
            : null;
        _prefabIndex++;

        GameObject go = prefab
            ? Instantiate(prefab, new Vector3(x, 1f, z), Quaternion.identity)
            : CreateCapsule(pid, new Vector3(x, 1f, z));

        go.name = pid;
        AddNameTag(go, isLocal ? $"{pid} (tú)" : pid);

        if (isLocal)
        {
            var pm = go.GetComponent<PlayerManager>();
            if (pm) pm.PlayerId = pid;
            _localPlayer = go;
        }
        else
        {
            var rpc = go.GetComponent<RemotePlayerSync>()
                   ?? go.AddComponent<RemotePlayerSync>();
            rpc.PlayerId = pid;
            // Registrar en HUD para que el label de rival quede asignado
            hudManager?.RegisterRival(pid, pid);
        }

        _players[pid] = go;
    }

    // ── Spawn de objetos ──────────────────────────────────────────────────────
    private void ParseAndSpawnObjects(string json)
    {
        string arr = ExtractArray(json, "objects");
        foreach (string entry in SplitJsonObjects(arr))
        {
            string id         = GameServer.ExtractString(entry, "id");
            float  x          = GameServer.ExtractFloat(entry, "x");
            float  z          = GameServer.ExtractFloat(entry, "z");
            string objectType = GameServer.ExtractString(entry, "objectType");
            SpawnObject(id, x, z, objectType);
        }
    }

    private void SpawnObject(string id, float x, float z, string objectType)
    {
        GameObject go = objectType == "powerup_viento"
            ? (vientoPrefab
                ? Instantiate(vientoPrefab, new Vector3(x, 0.5f, z), Quaternion.identity)
                : vfxController.CreateSphere(id, new Vector3(x, 0.5f, z), new Color(0.4f, 0.9f, 1f), 0.6f))
            : (runaPrefab
                ? Instantiate(runaPrefab, new Vector3(x, 0.5f, z), Quaternion.identity)
                : vfxController.CreateSphere(id, new Vector3(x, 0.5f, z), new Color(1f, 0.85f, 0.1f), 0.4f));

        go.name = id;

        var runa = go.GetComponent<RunaObject>() ?? go.AddComponent<RunaObject>();
        runa.RunaId     = id;
        runa.ObjectType = objectType;

        var col = go.GetComponent<Collider>();
        if (col) col.isTrigger = true;

        _objects[id] = go;
    }

    // ── Handlers de red ───────────────────────────────────────────────────────
    private void OnPlayerMove(string json)
    {
        string pid       = GameServer.ExtractString(json, "playerId");
        float  x         = GameServer.ExtractFloatInObject(json, "position", "x");
        float  z         = GameServer.ExtractFloatInObject(json, "position", "z");
        string animState = GameServer.ExtractString(json, "state");

        if (pid == _localId) return;

        if (_players.TryGetValue(pid, out GameObject go))
        {
            var rpc = go.GetComponent<RemotePlayerSync>();
            if (rpc) rpc.SetTargetFromMove(new Vector3(x, 1f, z), animState);
        }
    }

    private void OnCollectConfirm(string json)
    {
        string objectId   = GameServer.ExtractString(json, "objectId");
        string pid        = GameServer.ExtractString(json, "playerId");
        string objectType = GameServer.ExtractString(json, "objectType");
        int    newScore   = (int)GameServer.ExtractFloat(json, "newScore");

        // Destruir el objeto visualmente en todos los clientes
        if (_objects.TryGetValue(objectId, out GameObject obj))
        {
            _objects.Remove(objectId);
            Destroy(obj);
        }

        // Solo actualizar HUD y aplicar efectos si es el jugador local
        if (pid != _localId)
        {
            // Actualizar puntaje del rival en HUD
            hudManager?.SetScore(pid, newScore);
            return;
        }

        if (objectType == "powerup_viento")
        {
            float duration = GameServer.ExtractFloat(json, "vientoDuration");
            if (duration <= 0f) duration = 5f;

            var pm = _localPlayer ? _localPlayer.GetComponent<PlayerManager>() : null;
            if (pm)
            {
                pm.ApplySpeedBoost(duration);
                pm.Anim?.TriggerSpellWind();
            }
            hudManager?.ShowEffectIcon("boost");
        }
        else // runa_comun
        {
            hudManager?.SetScore(_localId, newScore);
        }
    }

    private void OnCollectDeny(string json)
    {
        // La runa la tomó otro — el collect_confirm del ganador ya la destruyó.
        // No hace falta hacer nada visual aquí.
        Debug.Log($"[GameManager] Recolección denegada: {GameServer.ExtractString(json, "objectId")}");
    }

    private void OnPowerupConfirm(string json)
    {
        string pid         = GameServer.ExtractString(json, "playerId");
        string powerupType = GameServer.ExtractString(json, "powerupType");

        if (powerupType != "portal_propio") return;

        float destX = GameServer.ExtractFloatInObject(json, "destinationPosition", "x");
        float destZ = GameServer.ExtractFloatInObject(json, "destinationPosition", "z");
        Vector3 dest = new Vector3(destX, 1f, destZ);

        if (!_players.TryGetValue(pid, out GameObject go)) return;

        if (pid == _localId)
        {
            var rb = go.GetComponent<Rigidbody>();
            if (rb) rb.position = dest;
            else    go.transform.position = dest;

            var pm = go.GetComponent<PlayerManager>();
            pm?.Anim?.TriggerSpellWind();
            pm?.SetPowerupReady("portal_propio", false);
            hudManager?.SetPowerupReady("");
        }
        else
        {
            var rpc = go.GetComponent<RemotePlayerSync>();
            if (rpc) rpc.SetTarget(dest);
            else go.transform.position = dest;
        }
    }

    // ── Meteoros ──────────────────────────────────────────────────────────────
    private void OnMeteorSpawn(string json)
    {
        string meteorId = GameServer.ExtractString(json, "meteorId");
        if (_meteoros.ContainsKey(meteorId)) return;

        float tx      = GameServer.ExtractFloatInObject(json, "targetPosition", "x");
        float tz      = GameServer.ExtractFloatInObject(json, "targetPosition", "z");
        float fallDur = GameServer.ExtractFloat(json, "fallDuration");
        if (fallDur <= 0f) fallDur = 3f;

        // VFXController crea y anima el meteoro
        GameObject meteorGo = vfxController
            ? vfxController.SpawnMeteor(meteorId, tx, tz, fallDur)
            : null;

        if (meteorGo != null)
            _meteoros[meteorId] = meteorGo;
    }

    private void OnZoneBlocked(string json)
    {
        string meteorId = GameServer.ExtractString(json, "meteorId");
        float  px       = GameServer.ExtractFloatInObject(json, "position", "x");
        float  pz       = GameServer.ExtractFloatInObject(json, "position", "z");
        float  radius   = GameServer.ExtractFloat(json, "radius");
        if (radius <= 0f) radius = 3.5f;

        // Destruir el visual del meteoro
        if (_meteoros.TryGetValue(meteorId, out GameObject meteorVis))
        {
            _meteoros.Remove(meteorId);
            if (meteorVis) Destroy(meteorVis);
        }

        // VFXController crea la zona bloqueada
        GameObject zona = vfxController
            ? vfxController.SpawnZona(meteorId, px, pz, radius)
            : null;

        if (zona != null)
            _zonas[meteorId] = zona;
    }

    private void OnZoneExpired(string json)
    {
        string meteorId = GameServer.ExtractString(json, "meteorId");
        if (_zonas.TryGetValue(meteorId, out GameObject zona))
        {
            _zonas.Remove(meteorId);
            Destroy(zona);
        }
    }

    private void OnMatchEnd(string json)
    {
        _matchRunning = false;

        string winner     = GameServer.ExtractString(json, "winnerPlayerId");
        string scoresArr  = ExtractArray(json, "finalScores");

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== RESULTADOS ===\n");
        int rank = 1;
        foreach (string entry in SplitJsonObjects(scoresArr))
        {
            string pid   = GameServer.ExtractString(entry, "playerId");
            int    score = (int)GameServer.ExtractFloat(entry, "score");
            sb.AppendLine($"#{rank++}  {pid}{(pid == winner ? " ★" : "")}  →  {score} runas");
        }

        hudManager?.ShowResults(sb.ToString());
    }

    // ── Botón portal (llamado desde el botón UI en HUD) ───────────────────────
    public void OnPortalButtonPressed()
    {
        GameClient.Instance?.SendPowerupActivate("portal_propio");
    }

    // ── Helpers de primitivas (fallback sin prefabs) ──────────────────────────
    private static GameObject CreateCapsule(string name, Vector3 pos)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.transform.position = pos;
        go.name = name;
        return go;
    }

    private static void AddNameTag(GameObject go, string label)
    {
        var child = new GameObject("NameTag");
        child.transform.SetParent(go.transform);
        child.transform.localPosition = new Vector3(0f, 1.5f, 0f);
        var tm = child.AddComponent<TextMesh>();
        tm.text          = label;
        tm.characterSize = 0.15f;
        tm.fontSize      = 40;
        tm.alignment     = TextAlignment.Center;
        tm.anchor        = TextAnchor.MiddleCenter;
        tm.color         = Color.white;
    }

    // ── JSON helpers ──────────────────────────────────────────────────────────
    private static string ExtractArray(string json, string key)
    {
        string search = $"\"{key}\":";
        int start = json.IndexOf(search, StringComparison.Ordinal);
        if (start < 0) return "";
        start = json.IndexOf('[', start);
        if (start < 0) return "";
        int depth = 1, i = start + 1;
        for (; i < json.Length; i++)
        {
            if      (json[i] == '[') depth++;
            else if (json[i] == ']') { depth--; if (depth == 0) break; }
        }
        return json.Substring(start + 1, i - start - 1);
    }

    private static List<string> SplitJsonObjects(string content)
    {
        var result = new List<string>();
        int depth = 0, start = -1;
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