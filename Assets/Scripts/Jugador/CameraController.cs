using UnityEngine;
using Unity.Cinemachine;

namespace RuneRush.Player
{
    /// <summary>
    /// CameraController — gira la cámara de Cinemachine con el joystick derecho.
    ///
    /// Configuración en Unity:
    ///   1. En tu Virtual Camera de Cinemachine, agrega el componente
    ///      CinemachineOrbitalFollow (o usa el preset "Third Person").
    ///   2. Asigna el Follow y LookAt al transform del jugador local.
    ///   3. Coloca este script en el mismo GameObject que el Virtual Camera
    ///      o en cualquier objeto activo en la escena de juego.
    ///   4. Arrastra la referencia al PlayerController local en el Inspector.
    ///
    /// El input viene de PlayerController.LookInput, que el On-Screen Stick
    /// del joystick derecho alimenta vía el Input Action "Look".
    /// </summary>
    public class CameraController : MonoBehaviour
    {
        [Header("Referencias")]
        [SerializeField] private PlayerManager _player;
        [SerializeField] private CinemachineOrbitalFollow _orbital;

        [Header("Sensibilidad")]
        [SerializeField] private float _horizontalSpeed = 180f; // grados por segundo
        [SerializeField] private float _verticalSpeed   = 90f;

        [Header("Límites verticales")]
        [SerializeField] private float _minVerticalAngle = -10f;
        [SerializeField] private float _maxVerticalAngle =  60f;

        private float _yaw;   // rotación horizontal acumulada
        private float _pitch; // rotación vertical acumulada

        private void Start()
        {
            // Inicializar con la rotación actual de la cámara
            if (_orbital != null)
            {
                _yaw   = _orbital.HorizontalAxis.Value;
                _pitch = _orbital.VerticalAxis.Value;
            }
        }

        private void LateUpdate()
        {
            if (_player == null || _orbital == null) return;

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

            // Publicar el yaw actual al PlayerController para que MovingState
            // pueda calcular el movimiento relativo a la cámara.
            // Se hace siempre, no solo cuando hay input de cámara.
            _player.CameraYaw = _yaw;
        }
    }
}
