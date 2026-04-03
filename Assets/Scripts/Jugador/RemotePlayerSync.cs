using UnityEngine;

namespace RuneRush.Player
{
    /// <summary>
    /// RemotePlayerSync — maneja la representación visual de un jugador remoto.
    ///
    /// Responsabilidades:
    ///   - Interpolar suavemente hacia la última posición recibida del servidor.
    ///   - Actualizar el Animator del jugador remoto con el estado recibido.
    ///   - Reaccionar a eventos de power-up (teletransporte, hechizo) que llegan
    ///     por broadcast desde GameClient.
    ///
    /// GameManager lo agrega al GameObject de cada jugador que NO es local.
    ///
    /// RIESGO P2P: si el host tiene lag, los player_move llegan con retraso
    /// y el jugador remoto se ve "teletransportado" en saltos. El lerp
    /// suaviza esto, pero si el retraso es muy alto (>200ms) se nota igual.
    /// No hay mucho que hacer del lado cliente sin timestamps y buffer de estados.
    /// </summary>
    public class RemotePlayerSync : MonoBehaviour
    {
        public string PlayerId { get; set; } = "";

        [SerializeField] private float _lerpSpeed = 12f;

        private Vector3   _targetPosition;
        private Animator  _animator;
        private bool      _hasTarget = false;

        // Hashes — mismos que PlayerAnimator para consistencia
        private static readonly int SpeedHash      = Animator.StringToHash("Speed");
        private static readonly int IsFroggedHash  = Animator.StringToHash("IsFrogged");
        private static readonly int IsLaunchedHash = Animator.StringToHash("IsLaunched");
        private static readonly int TeleportHash   = Animator.StringToHash("Teleport");
        private static readonly int CastSpellHash  = Animator.StringToHash("CastSpell");

        private void Awake()
        {
            _animator        = GetComponent<Animator>();
            _targetPosition  = transform.position;
        }

        private void OnEnable()
        {
            if (GameClient.Instance == null) return;
            GameClient.Instance.OnPowerupConfirm.AddListener(OnPowerupConfirm);
            GameClient.Instance.OnCollectConfirm.AddListener(OnCollectConfirm);
        }

        private void OnDisable()
        {
            if (GameClient.Instance == null) return;
            GameClient.Instance.OnPowerupConfirm.RemoveListener(OnPowerupConfirm);
            GameClient.Instance.OnCollectConfirm.RemoveListener(OnCollectConfirm);
        }

        private void Update()
        {
            if (!_hasTarget) return;

            // Interpolación suave hacia la última posición conocida
            transform.position = Vector3.Lerp(
                transform.position,
                _targetPosition,
                _lerpSpeed * Time.deltaTime
            );

            // Rotar hacia la dirección de movimiento
            Vector3 delta = _targetPosition - transform.position;
            if (delta.sqrMagnitude > 0.001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(delta);
                transform.rotation = Quaternion.Slerp(
                    transform.rotation, targetRot, 12f * Time.deltaTime
                );
            }
        }

        // ── API pública llamada por GameManager ───────────────────────────────

        /// <summary>
        /// Actualiza posición y estado de animación a partir del json de player_move.
        /// Llamado desde GameManager.OnPlayerMove().
        /// </summary>
        public void SetTargetFromMove(Vector3 position, string animState)
        {
            _targetPosition = position;
            _hasTarget = true;

            if (_animator == null) return;

            // Mapear el campo "state" del json a parámetros del Animator
            switch (animState)
            {
                case "idle":
                    _animator.SetFloat(SpeedHash, 0f, 0.1f, Time.deltaTime);
                    break;
                case "moviendose":
                    _animator.SetFloat(SpeedHash, 1f, 0.1f, Time.deltaTime);
                    break;
                case "boosted":
                    _animator.SetFloat(SpeedHash, 2f, 0.1f, Time.deltaTime);
                    break;
                case "frogged":
                    _animator.SetBool(IsFroggedHash, true);
                    _animator.SetFloat(SpeedHash, 0.5f, 0.1f, Time.deltaTime);
                    break;
                case "launched":
                    _animator.SetBool(IsLaunchedHash, true);
                    break;
                default:
                    _animator.SetFloat(SpeedHash, 0f, 0.1f, Time.deltaTime);
                    break;
            }

            // Limpiar efectos cuando vuelve a moverse normal
            if (animState == "moviendose" || animState == "idle")
            {
                _animator.SetBool(IsFroggedHash,  false);
                _animator.SetBool(IsLaunchedHash, false);
            }
        }

        /// <summary>Teletransporte instantáneo (sin lerp).</summary>
        public void SetTarget(Vector3 position)
        {
            _targetPosition  = position;
            _hasTarget       = true;
        }

        // ── Eventos de broadcast ──────────────────────────────────────────────

        private void OnPowerupConfirm(string json)
        {
            string pid         = GameServer.ExtractString(json, "playerId");
            string powerupType = GameServer.ExtractString(json, "powerupType");

            if (pid != PlayerId) return;

            if (powerupType == "portal_propio")
            {
                float destX = GameServer.ExtractFloatInObject(json, "destinationPosition", "x");
                float destZ = GameServer.ExtractFloatInObject(json, "destinationPosition", "z");

                // Teletransporte instantáneo — sin lerp para el jugador remoto
                Vector3 dest = new Vector3(destX, 1f, destZ);
                transform.position = dest;
                _targetPosition    = dest;

                _animator?.SetTrigger(TeleportHash);
            }
        }

        private void OnCollectConfirm(string json)
        {
            string pid        = GameServer.ExtractString(json, "playerId");
            string objectType = GameServer.ExtractString(json, "objectType");

            if (pid != PlayerId) return;

            // Animación de hechizo al recoger power-up
            if (objectType == "powerup_viento")
                _animator?.SetTrigger(CastSpellHash);
        }
    }
}
