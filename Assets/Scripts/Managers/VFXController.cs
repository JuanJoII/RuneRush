using UnityEngine;
using System.Collections;

namespace RuneRush.Player
{
    // ══════════════════════════════════════════════════════════════════════════
    // VFXController
    // ══════════════════════════════════════════════════════════════════════════
    public class VFXController : MonoBehaviour
    {
        [Header("Prefabs de escena")]
        [SerializeField] private GameObject _meteorPrefab;
        [SerializeField] private GameObject _zonaPrefab;
 
        [Header("Prefabs de power-up VFX")]
        [SerializeField] private GameObject _windPushVFXPrefab;
        [SerializeField] private GameObject _frogSpellVFXPrefab;
 
        // Encontrados en Awake por marcador — no requieren asignación manual
        private GameObject     _normalModel;
        private GameObject     _frogModel;
        private GameObject _boostTrail;
 
        private TeleportingState _teleportingState;
        private PlayerAnimator   _playerAnimator;
 
        private void Awake()
        {
            _playerAnimator = GetComponentInParent<PlayerAnimator>();
 
            var normal = GetComponentInChildren<PlayerModelNormal>(includeInactive: true);
            if (normal) _normalModel = normal.gameObject;
 
            var frog = GetComponentInChildren<PlayerModelFrog>(includeInactive: true);
            if (frog) _frogModel = frog.gameObject;
 
            var trail = GetComponentInChildren<BoostTrailMarker>(includeInactive: true);
            if (trail) _boostTrail = trail.gameObject;
        }
 
        public void Init(TeleportingState teleportingState)
        {
            _teleportingState = teleportingState;
        }
 
        // ── Efectos del jugador ───────────────────────────────────────────────
 
        public void PlayEffect(string vfxId)
        {
            switch (vfxId)
            {
                case "boost":
                    if (_boostTrail) _boostTrail.SetActive(true);
                    break;;
                case "frogged":
                    SetFrogModel(true);
                    break;
                case "teleport_out":
                    Invoke(nameof(NotifyTeleportArrival), 0.4f);
                    break;
            }
        }
 
        public void StopEffect(string vfxId)
        {
            switch (vfxId)
            {
                case "boost":
                    if (_boostTrail) _boostTrail.SetActive(false);
                    break;
                case "frogged":
                    SetFrogModel(false);
                    break;
            }
        }
 
        public void StopAllEffects()
        {
            if (_boostTrail) _boostTrail.SetActive(false);
            SetFrogModel(false);
        }
 
        public void SetBoostTrail(bool active)
        {
            if (_boostTrail == null) return;
            _boostTrail.SetActive(active);
        }
 
        private void SetFrogModel(bool active)
        {
            if (_normalModel) _normalModel.SetActive(!active);
            if (_frogModel)   _frogModel.SetActive(active);
            _playerAnimator?.SwitchToFrog(active);
        }
 
        private void NotifyTeleportArrival() => _teleportingState?.OnArrival();
 
        // ── Power-up VFX ──────────────────────────────────────────────────────
 
        public GameObject SpawnWindPushVFX(Vector3 position, Quaternion rotation,
                                           string attackerId, float duration)
        {
            GameObject go = _windPushVFXPrefab
                ? Instantiate(_windPushVFXPrefab, position, rotation)
                : CreateDebugSphere("WindVFX", Color.cyan, 1.5f, position);
 
            var vfx = go.GetComponent<PowerupVFX>() ?? go.AddComponent<PowerupVFX>();
            vfx.Type       = PowerupVFX.VFXType.WindPush;
            vfx.AttackerId = attackerId;
            vfx.Duration   = duration;
            return go;
        }
 
        public GameObject SpawnFrogSpellVFX(Vector3 position, Quaternion rotation,
                                            string attackerId, float duration)
        {
            GameObject go = _frogSpellVFXPrefab
                ? Instantiate(_frogSpellVFXPrefab, position, rotation)
                : CreateDebugSphere("FrogVFX", Color.green, 1.5f, position);
 
            var vfx = go.GetComponent<PowerupVFX>() ?? go.AddComponent<PowerupVFX>();
            vfx.Type       = PowerupVFX.VFXType.FrogSpell;
            vfx.AttackerId = attackerId;
            vfx.Duration   = duration;
            return go;
        }
 
        // ── Objetos de escena ─────────────────────────────────────────────────
 
        public GameObject SpawnMeteor(string meteorId, float tx, float tz, float fallDuration)
        {
            Vector3 startPos = new Vector3(tx, 60f, tz);
            Vector3 endPos   = new Vector3(tx, 0.5f, tz);
 
            GameObject go = _meteorPrefab
                ? Instantiate(_meteorPrefab, startPos, Quaternion.identity)
                : CreateDebugSphere(meteorId + "_meteor", new Color(1f, 0.4f, 0.1f), 1.5f, startPos);
 
            go.name = meteorId + "_visual";
 
            var col = go.GetComponent<Collider>();
            if (col) col.enabled = false;
 
            StartCoroutine(AnimateMeteorFall(go, startPos, endPos, fallDuration));
            return go;
        }
 
        private IEnumerator AnimateMeteorFall(GameObject go, Vector3 from,
                                               Vector3 to, float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration && go != null)
            {
                elapsed += Time.deltaTime;
                go.transform.position = Vector3.Lerp(from, to, elapsed / duration);
                go.transform.Rotate(Vector3.right, 180f * Time.deltaTime);
                yield return null;
            }
        }
 
        public GameObject SpawnZona(string meteorId, float px, float pz, float radius)
        {
            float groundY = 0f;
            if (!TryRaycastGround(px, pz, out groundY))
            {
                float[] dists  = { 1f, 2f, 3f };
                Vector2[] dirs = { Vector2.right, Vector2.left, Vector2.up, Vector2.down };
                foreach (float d in dists)
                    foreach (Vector2 dir in dirs)
                        if (TryRaycastGround(px + dir.x * d, pz + dir.y * d, out groundY))
                            goto foundGround;
            }
            foundGround:
 
            GameObject go = _zonaPrefab
                ? Instantiate(_zonaPrefab, new Vector3(px, groundY, pz), Quaternion.identity)
                : CreateDebugCylinder(meteorId + "_zona", new Vector3(px, groundY, pz), radius);
 
            go.name = meteorId + "_zona";
 
            var col = go.GetComponent<Collider>();
            if (col) col.isTrigger = false;
 
            return go;
        }
 
        private static bool TryRaycastGround(float x, float z, out float groundY)
        {
            if (Physics.Raycast(new Vector3(x, 200f, z), Vector3.down,
                                out RaycastHit hit, 400f, 1 << 6))
            {
                groundY = hit.point.y;
                return true;
            }
            groundY = 0f;
            return false;
        }
 
        // ── Primitivas fallback ───────────────────────────────────────────────
 
        public GameObject CreateDebugSphere(string name, Color color,
                                             float scale = 0.5f, Vector3 pos = default)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.transform.position   = pos;
            go.transform.localScale = Vector3.one * scale;
            go.name = name;
            go.GetComponent<Renderer>().material.color = color;
            return go;
        }
 
        public GameObject CreateDebugCylinder(string name, Vector3 pos, float radius)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.transform.position   = pos + Vector3.up * 0.5f;
            go.transform.localScale = new Vector3(radius * 2f, 0.5f, radius * 2f);
            go.name = name;
            return go;
        }
    }
 
}