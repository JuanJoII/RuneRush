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
    // ── Singleton ─────────────────────────────────────────────────────────────
    public static GameManager Instance { get; private set; }

    // ── Prefabs ───────────────────────────────────────────────────────────────
    [Header("Prefabs de jugador (uno por color, en orden de asignación)")]
    [SerializeField] private GameObject[] playerPrefabs = new GameObject[4];

    [Header("Prefabs de objetos")]
    [SerializeField] private GameObject runaPrefab;
    [SerializeField] private GameObject powerupPrefab;
    [SerializeField] private GameObject portalPrefab;  // cualquier power-up recogible

    // ── Managers ──────────────────────────────────────────────────────────────
    [Header("Referencias")]
    [SerializeField] private HUDManager   hudManager;
    [SerializeField] private VFXController vfxController; // gestiona meteoros y zonas

    // ── Estado ────────────────────────────────────────────────────────────────
    private readonly Dictionary<string, GameObject> _players  = new();
    private readonly Dictionary<string, GameObject> _objects  = new();
    private readonly Dictionary<string, GameObject> _zonas    = new();
    private readonly Dictionary<string, GameObject> _meteoros = new();
    private readonly Dictionary<string, Vector3>    _portals  = new(); // portalId → posición

    private GameObject _localPlayer;
    private string     _localId       = "";
    private int        _prefabIndex   = 0;
    private float      _matchEndTime;
    private bool       _matchRunning  = false;

    // ── Ciclo Unity ───────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

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
            // Portal ambiental — tu amigo necesita agregar este evento en GameClient
            // GameClient.Instance.OnPortalSpawn.AddListener(OnPortalSpawn);
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
        if (Instance == this) Instance = null;
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
        SpawnPortalesDeterministicos();
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
        SpawnPortalesDeterministicos();
    }

    /// <summary>
    /// Spawna portales en posiciones fijas conocidas por todos los clientes.
    /// No necesita coordinación con el servidor.
    /// Los portales se crean en pares: entrar en uno lleva al otro.
    /// </summary>
    private void SpawnPortalesDeterministicos()
    {
        // Pares de portales (posXA, posZA, posXB, posZB)
        (float, float, float, float)[] pairs =
        {
            (20f, 20f,  80f, 80f),
            (20f, 80f,  80f, 20f),
            (50f, 10f,  50f, 90f),
        };

        int portalIndex = 0;
        foreach (var (ax, az, bx, bz) in pairs)
        {
            string idA = $"PORTAL_{portalIndex}A";
            string idB = $"PORTAL_{portalIndex}B";

            SpawnPortalLocal(idA, idB, ax, az);
            SpawnPortalLocal(idB, idA, bx, bz);
            portalIndex++;
        }
    }

    private void SpawnPortalLocal(string portalId, string pairId, float px, float pz)
    {
        float groundY = SnapToGroundY(px, pz);
        Vector3 pos   = new Vector3(px, groundY, pz);
        _portals[portalId] = pos;

        GameObject go = portalPrefab
            ? Instantiate(portalPrefab, pos, Quaternion.identity)
            : CreateDebugSphere("Portal_" + portalId, pos, new Color(0.5f, 0f, 1f), 1f);

        go.name = "Portal_" + portalId;
        var portal = go.GetComponent<PortalObject>() ?? go.AddComponent<PortalObject>();
        portal.PortalId = portalId;
        portal.PairId   = pairId;
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

    // Máscara de suelo — debe coincidir con la del RemotePlayerSync
    private static readonly LayerMask GroundMask = 1 << 6;

    /// <summary>
    /// Dado un punto XZ, devuelve la Y real del suelo usando raycast.
    /// Si el punto cae en un hueco entre meshes, busca en espiral hasta
    /// encontrar suelo cercano. Si no encuentra nada, retorna fallbackY.
    /// </summary>
    private static float SnapToGroundY(float x, float z, float fallbackY = 0f, float pivotOffset = 0f)
    {
        // Intentar en el punto exacto primero
        if (TryRaycastGround(x, z, out float y))
            return y + pivotOffset;

        // Si falla, buscar en espiral con pasos de 1 unidad hasta radio 5
        float[] offsets = { 1f, 2f, 3f, 4f, 5f };
        Vector2[] dirs  = {
            Vector2.right, Vector2.left, Vector2.up, Vector2.down,
            new Vector2(1,1).normalized, new Vector2(-1,1).normalized,
            new Vector2(1,-1).normalized, new Vector2(-1,-1).normalized
        };

        foreach (float dist in offsets)
            foreach (Vector2 dir in dirs)
                if (TryRaycastGround(x + dir.x * dist, z + dir.y * dist, out y))
                    return y + pivotOffset;

        // Sin suelo encontrado en ningún punto cercano
        return fallbackY;
    }

    private static bool TryRaycastGround(float x, float z, out float groundY)
    {
        Vector3 origin = new Vector3(x, 200f, z);
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 400f, GroundMask))
        {
            groundY = hit.point.y;
            return true;
        }
        groundY = 0f;
        return false;
    }

    private void SpawnPlayer(string pid, float x, float z, bool isLocal)
    {
        float groundY = SnapToGroundY(x, z, fallbackY: 1f, pivotOffset: 0f);

        GameObject prefab = (_prefabIndex < playerPrefabs.Length)
            ? playerPrefabs[_prefabIndex] : null;
        _prefabIndex++;

        GameObject go = prefab
            ? Instantiate(prefab, new Vector3(x, groundY, z), Quaternion.identity)
            : CreateCapsule(pid, new Vector3(x, groundY, z));

        go.name = pid;
        AddNameTag(go, isLocal ? $"{pid} (tú)" : pid);

        if (isLocal)
        {
            var pm = go.GetComponent<PlayerManager>();
            if (pm != null)
            {
                pm.PlayerId = pid;
                // Conectar HUDManager al PlayerController en runtime
                // (HUDManager vive en escena, PlayerController en prefab)
                pm.SetHUDManager(hudManager);
            }
            _localPlayer = go;

            var strayRpc = go.GetComponent<RemotePlayerSync>();
            if (strayRpc) strayRpc.enabled = false;
        }
        else
        {
            // ── Desactivar todo lo que es exclusivo del jugador local ─────────

            // 1. PlayerManager — evita que el input del joystick mueva al remoto
            var pm = go.GetComponent<PlayerManager>();
            if (pm) pm.enabled = false;

            // 2. NetworkEventHandler — evita que procese eventos ajenos
            var neh = go.GetComponent<NetworkEventHandler>();
            if (neh) neh.enabled = false;

            // 3. PlayerAnimator — RemotePlayerSync maneja el Animator directamente
            var anim = go.GetComponent<PlayerAnimator>();
            if (anim) anim.enabled = false;

            // 4. Rigidbody — la posición la mueve RemotePlayerSync vía transform,
            //    no la física. isKinematic evita que el motor físico interfiera.
            var rb = go.GetComponent<Rigidbody>();
            if (rb) rb.isKinematic = true;

            // 5. Agregar RemotePlayerSync si no está en el prefab
            var rpc = go.GetComponent<RemotePlayerSync>()
                   ?? go.AddComponent<RemotePlayerSync>();
            rpc.enabled  = true;
            rpc.PlayerId = pid;

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
        // Los coleccionables flotan ligeramente sobre el suelo (0.5f de offset)
        float groundY = SnapToGroundY(x, z, fallbackY: 0.5f, pivotOffset: 5f);

        GameObject go = objectType == "powerup_viento"
            ? (powerupPrefab
                ? Instantiate(powerupPrefab, new Vector3(x, groundY, z), Quaternion.identity)
                : CreateDebugSphere(id, new Vector3(x, groundY, z), new Color(0.4f, 0.9f, 1f), 0.6f))
            : (runaPrefab
                ? Instantiate(runaPrefab, new Vector3(x, groundY, z), Quaternion.identity)
                : CreateDebugSphere(id, new Vector3(x, groundY, z), new Color(1f, 0.85f, 0.1f), 0.4f));

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
            hudManager?.SetScore(pid, newScore);
            return;
        }

        if (objectType == "powerup_viento")
        {
            var pm = _localPlayer ? _localPlayer.GetComponent<PlayerManager>() : null;
            if (pm != null)
            {
                // Asignar power-up aleatorio — el jugador lo activa cuando quiera
                string[] available = { "powerup_viento", "portal_propio" };
                string chosen = available[UnityEngine.Random.Range(0, available.Length)];
                pm.SetPowerupReady(chosen, true);
            }
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

        // ── Comprobar si el jugador local está dentro del radio de impacto ─────
        if (_localPlayer == null) return;

        Vector3 impactCenter = new Vector3(px, _localPlayer.transform.position.y, pz);
        float   distToPlayer = Vector3.Distance(_localPlayer.transform.position, impactCenter);

        if (distToPlayer <= radius)
        {
            var pm = _localPlayer.GetComponent<PlayerManager>();
            if (pm == null) return;

            // Calcular dirección del impulso: desde el centro hacia el jugador
            Vector3 dir = (_localPlayer.transform.position - impactCenter).normalized;
            // Si el jugador está justo en el centro, lanzar en una dirección aleatoria
            if (dir == Vector3.zero) dir = new Vector3(1f, 0f, 0f);

            // Fuerza proporcional a la cercanía — más cerca = más fuerte
            float proximity  = 1f - (distToPlayer / radius); // 0..1
            float forceMag   = Mathf.Lerp(100f, 180f, proximity);
            Vector3 force    = (dir + Vector3.up * 0.4f).normalized * forceMag;

            pm.OnNetworkEvent(new RuneRush.Player.NetworkEvent
            {
                Type           = RuneRush.Player.NetworkEventType.EffectApplied,
                Effect         = RuneRush.Player.EffectType.Launched,
                LaunchForce    = force,
                EffectDuration = 0.8f,
            });
        }
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

    // ── Portales ambientales ──────────────────────────────────────────────────

    public void OnPortalSpawn(string json)
    {
        string arr = ExtractArray(json, "portals");
        foreach (string entry in SplitJsonObjects(arr))
        {
            string portalId = GameServer.ExtractString(entry, "id");
            string pairId   = GameServer.ExtractString(entry, "pairId");
            float  px       = GameServer.ExtractFloat(entry, "x");
            float  pz       = GameServer.ExtractFloat(entry, "z");
            float  groundY  = SnapToGroundY(px, pz);

            Vector3 pos = new Vector3(px, groundY, pz);
            _portals[portalId] = pos;

            GameObject go = portalPrefab
                ? Instantiate(portalPrefab, pos, Quaternion.identity)
                : CreateDebugSphere("Portal_" + portalId, pos, new Color(0.5f, 0f, 1f), 1f);

            go.name = "Portal_" + portalId;
            var portal = go.GetComponent<PortalObject>() ?? go.AddComponent<PortalObject>();
            portal.PortalId = portalId;
            portal.PairId   = pairId;
        }
    }

    public void OnLocalPlayerEnterPortal(string portalId, string pairId,
                                          RuneRush.Player.PlayerManager pm)
    {
        if (!_portals.TryGetValue(pairId, out Vector3 dest)) return;

        var rb = pm.GetComponent<Rigidbody>();
        if (rb) rb.position = dest;
        else    pm.transform.position = dest;

        pm.VFX?.PlayEffect("teleport_out");
    }

    public void OnPowerupVFXHit(RuneRush.Player.PowerupVFX.VFXType type,
                                  string attackerId, string targetId)
    {
        if (targetId == _localId)
        {
            // El jugador local fue golpeado — aplicar estado directamente
            var pm = _localPlayer ? _localPlayer.GetComponent<RuneRush.Player.PlayerManager>() : null;
            if (pm == null) return;

            if (type == RuneRush.Player.PowerupVFX.VFXType.WindPush)
            {
                if (!_players.TryGetValue(attackerId, out GameObject attacker)) return;
                Vector3 dir   = (pm.transform.position - attacker.transform.position).normalized;
                dir.y         = 0.3f;
                Vector3 force = dir.normalized * 14f;
                pm.StateLaunched.SetForce(force, 0.8f);
                pm.OnNetworkEvent(new RuneRush.Player.NetworkEvent
                {
                    Type           = RuneRush.Player.NetworkEventType.EffectApplied,
                    Effect         = RuneRush.Player.EffectType.Launched,
                    LaunchForce    = force,
                    EffectDuration = 0.8f,
                });
            }
            else
            {
                pm.OnNetworkEvent(new RuneRush.Player.NetworkEvent
                {
                    Type           = RuneRush.Player.NetworkEventType.EffectApplied,
                    Effect         = RuneRush.Player.EffectType.Frogged,
                    EffectDuration = 3f,
                });
            }
        }
        else if (_players.TryGetValue(targetId, out GameObject targetGo))
        {
            // Un rival fue golpeado — aplicar efecto visual en su representación remota
            // sin necesitar que el servidor retransmita (funciona en la misma red local)
            var rpc = targetGo.GetComponent<RuneRush.Player.RemotePlayerSync>();
            if (rpc != null)
            {
                if (type == RuneRush.Player.PowerupVFX.VFXType.FrogSpell)
                    rpc.ApplyFroggedVisual();
                else if (_players.TryGetValue(attackerId, out GameObject attackerGo))
                    rpc.ApplyLaunchedVisual(attackerGo.transform.position);
            }
        }

        // Notificar al servidor para que informe al dispositivo del jugador golpeado
        string powerupType = type == RuneRush.Player.PowerupVFX.VFXType.WindPush
            ? "wind_hit" : "frog_hit";
        GameClient.Instance?.SendPowerupActivate($"{powerupType}:{targetId}");
    }

    // ── Botón portal (llamado desde el botón UI en HUD) ───────────────────────
    public void OnPortalButtonPressed()
    {
        GameClient.Instance?.SendPowerupActivate("portal_propio");
    }

    // ── Helpers de primitivas (fallback sin prefabs) ──────────────────────────
    private static GameObject CreateDebugSphere(string name, Vector3 pos, Color color, float scale)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.transform.position   = pos;
        go.transform.localScale = Vector3.one * scale;
        go.name = name;
        go.GetComponent<Renderer>().material.color = color;
        return go;
    }

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