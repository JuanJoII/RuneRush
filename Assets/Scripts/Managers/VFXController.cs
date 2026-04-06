using UnityEngine;
using System.Collections;

namespace RuneRush.Player
{
    // ══════════════════════════════════════════════════════════════════════════
    // VFXController
    // Responsabilidades:
    //   - Partículas y modelos del jugador local (boost, frogged, teleport...).
    //   - Spawn y destrucción de objetos de escena: meteoros y zonas bloqueadas.
    //     GameManager accede a estos a través de SpawnMeteor / SpawnZone / etc.
    // ══════════════════════════════════════════════════════════════════════════
    public class VFXController : MonoBehaviour
    {
        [Header("Partículas del jugador")] 

        [SerializeField] private ParticleSystem _boostParticles;
        [SerializeField] private ParticleSystem _frogBubbles;
        [SerializeField] private ParticleSystem _launchTrail;
        [SerializeField] private ParticleSystem _teleportOut;
        [SerializeField] private ParticleSystem _teleportIn;

        [Header("Modelos del jugador")] [SerializeField]
        private GameObject _normalModel;

        [SerializeField] private GameObject _frogModel;

        [Header("Prefabs de escena")] [SerializeField]
        private GameObject _meteorPrefab;

        [SerializeField] private GameObject _zonaPrefab;

        private TeleportingState _teleportingState;

        public void Init(TeleportingState teleportingState)
        {
            _teleportingState = teleportingState;
        }

        // ── Efectos del jugador ───────────────────────────────────────────────
        public void PlayEffect(string vfxId)
        {
            switch (vfxId)
            {
                case "boost": _boostParticles?.Play(); break;
                case "launched": _launchTrail?.Play(); break;
                case "teleport_in": _teleportIn?.Play(); break;
                case "frogged":
                    _frogBubbles?.Play();
                    SetFrogModel(true);
                    break;
                case "teleport_out":
                    _teleportOut?.Play();
                    Invoke(nameof(NotifyTeleportArrival), 0.4f);
                    break;
            }
        }

        public void StopEffect(string vfxId)
        {
            switch (vfxId)
            {
                case "boost": _boostParticles?.Stop(); break;
                case "launched": _launchTrail?.Stop(); break;
                case "frogged":
                    _frogBubbles?.Stop();
                    SetFrogModel(false);
                    break;
            }
        }

        public void StopAllEffects()
        {
            _boostParticles?.Stop();
            _frogBubbles?.Stop();
            _launchTrail?.Stop();
            SetFrogModel(false);
        }

        private void SetFrogModel(bool active)
        {
            if (_normalModel) _normalModel.SetActive(!active);
            if (_frogModel) _frogModel.SetActive(active);
        }

        private void NotifyTeleportArrival() => _teleportingState?.OnArrival();

        // ── Objetos de escena (GameManager los pide aquí) ─────────────────────

        /// <summary>
        /// Spawna el visual del meteoro cayendo y lo anima.
        /// Devuelve el GameObject para que GameManager lo guarde en su diccionario.
        /// </summary>
        public GameObject SpawnMeteor(string meteorId, float tx, float tz, float fallDuration)
        {
            Vector3 startPos = new Vector3(tx, 60f, tz);
            Vector3 endPos = new Vector3(tx, 0.5f, tz);

            GameObject go = _meteorPrefab
                ? Instantiate(_meteorPrefab, startPos, Quaternion.identity)
                : CreateSphere(meteorId + "_meteor", startPos, new Color(1f, 0.4f, 0.1f), 1.5f);

            go.name = meteorId + "_visual";

            var col = go.GetComponent<Collider>();
            if (col) col.enabled = false;

            StartCoroutine(AnimateMeteorFall(go, startPos, endPos, fallDuration));
            return go;
        }

        private IEnumerator AnimateMeteorFall(GameObject go, Vector3 from, Vector3 to, float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration && go != null)
            {
                elapsed += Time.deltaTime;
                go.transform.position = Vector3.Lerp(from, to, elapsed / duration);
                go.transform.Rotate(Vector3.right, 180f * Time.deltaTime);
                yield return null;
            }
            // El GameManager destruye el objeto cuando llega zone_blocked,
            // así que no lo destruimos aquí — solo paramos la animación.
        }

        /// <summary>
        /// Spawna la zona bloqueada tras el impacto del meteoro.
        /// Devuelve el GameObject para que GameManager lo guarde.
        /// </summary>
        public GameObject SpawnZona(string meteorId, float px, float pz, float radius)
        {
            GameObject go = _zonaPrefab
                ? Instantiate(_zonaPrefab, new Vector3(px, 0.5f, pz), Quaternion.identity)
                : CreateCylinder(meteorId + "_zona", new Vector3(px, 0f, pz), radius);

            go.name = meteorId + "_zona";

            var col = go.GetComponent<Collider>();
            if (col) col.isTrigger = false;

            return go;
        }

        // ── Primitivas fallback ───────────────────────────────────────────────
        public GameObject CreateSphere(string name, Vector3 pos, Color color, float scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * scale;
            go.name = name;
            go.GetComponent<Renderer>().material.color = color;
            return go;
        }

        public GameObject CreateCylinder(string name, Vector3 pos, float radius)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.transform.position = pos + Vector3.up * 0.5f;
            go.transform.localScale = new Vector3(radius * 2f, 0.5f, radius * 2f);
            go.name = name;
            return go;
        }
    }
}