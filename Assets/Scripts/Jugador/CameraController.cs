using UnityEngine;
using Unity.Cinemachine;

namespace RuneRush.Player
{
    /// <summary>
    /// CameraController — cámara de Cinemachine en tercera persona para móvil.
    ///
    /// Configuración en escena:
    ///   1. Crea un GameObject vacío llamado "PlayerCamera" en la escena de juego
    ///      (NO dentro del prefab del jugador).
    ///   2. Agrégale un CinemachineCamera con CinemachineOrbitalFollow.
    ///   3. Agrégale este script.
    ///   4. Deja _player vacío en el Inspector — se asigna automáticamente al
    ///      jugador local cuando GameManager lo instancia.
    ///
    /// Auto-asignación:
    ///   Busca en escena el GameObject cuyo PlayerManager.PlayerId coincida con
    ///   GameClient.Instance.PlayerId. Si GameManager aún no ha spawnado al
    ///   jugador (los dos Start() compiten), reintenta cada frame hasta encontrarlo.
    /// </summary>
    public class CameraController : MonoBehaviour
    {
        [Header("Cinemachine")]
        [SerializeField] private CinemachineCamera        _vcam;
        [SerializeField] private CinemachineOrbitalFollow _orbital;

        [Header("Sensibilidad")]
        [SerializeField] private float _horizontalSpeed = 180f;
        [SerializeField] private float _verticalSpeed   = 90f;

        [Header("Límites verticales")]
        [SerializeField] private float _minVerticalAngle = -10f;
        [SerializeField] private float _maxVerticalAngle =  60f;

        // Referencia al jugador local — se puede asignar en Inspector o se
        // rellena automáticamente si se deja vacío.
        [Header("(Opcional) Asignación manual")]
        [SerializeField] private PlayerManager _player;

        private float _yaw;
        private float _pitch;
        private bool  _ready = false;

        private void Start()
        {
            // Si ya viene asignado desde el Inspector, usarlo directamente.
            if (_player != null)
            {
                Attach(_player);
                return;
            }

            // Si no, iniciar la búsqueda automática.
            // La búsqueda ocurre en Update hasta que encuentre al jugador local.
        }

        private void Update()
        {
            if (_ready) return;
            TryFindLocalPlayer();
        }

        private void LateUpdate()
        {
            if (!_ready || _player == null || _orbital == null) return;

            Vector2 look = _player.LookInput;
            if (look.sqrMagnitude >= 0.01f)
            {
                _yaw  += look.x * _horizontalSpeed * Time.deltaTime;
                _pitch = Mathf.Clamp(
                    _pitch - look.y * _verticalSpeed * Time.deltaTime,
                    _minVerticalAngle,
                    _maxVerticalAngle
                );

                _orbital.HorizontalAxis.Value = _yaw;
                _orbital.VerticalAxis.Value   = _pitch;
            }

            // Publicar yaw al PlayerManager para movimiento relativo a cámara
            _player.CameraYaw = _yaw;
        }

        // ── Búsqueda automática ───────────────────────────────────────────────

        private void TryFindLocalPlayer()
        {
            string localId = GameClient.Instance ? GameClient.Instance.PlayerId : "";
            if (string.IsNullOrEmpty(localId)) return;

            // Buscar todos los PlayerManager en escena y quedarse con el local
            foreach (var pm in FindObjectsByType<PlayerManager>(FindObjectsSortMode.None))
            {
                if (pm.PlayerId == localId)
                {
                    Attach(pm);
                    return;
                }
            }
            // Si no lo encontró todavía, lo intentará en el próximo frame
        }

        private void Attach(PlayerManager player)
        {
            _player = player;

            if (_vcam != null)
            {
                _vcam.Follow  = player.transform;
                _vcam.LookAt  = player.transform;
            }

            if (_orbital != null)
            {
                _yaw   = _orbital.HorizontalAxis.Value;
                _pitch = _orbital.VerticalAxis.Value;
            }

            _ready = true;
            Debug.Log($"[CameraController] Cámara asignada a {player.PlayerId}");
        }
    }
}
