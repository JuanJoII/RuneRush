using UnityEngine;

namespace RuneRush.Player
{
    /// <summary>
    /// PlayerAnimator — puente entre la máquina de estados y el Animator.
    ///
    /// Maneja dos Animators: uno para el modelo normal y otro para el modelo
    /// de rana. VFXController llama SwitchToFrog(true/false) para intercambiar.
    /// Solo el Animator activo recibe los parámetros.
    ///
    /// Parámetros requeridos en AMBOS Animator Controllers:
    ///   - "Speed"        : Float   — 0 = idle, 1 = caminando, 2 = boosted
    ///   - "CastSpellWind": Trigger — animación de hechizo de viento
    ///   - "CastSpellFrog": Trigger — animación de hechizo de rana
    /// </summary>
    public class PlayerAnimator : MonoBehaviour
    {
        [SerializeField] private PlayerManager _controller;

        [Header("Animators (uno por modelo)")]
        [SerializeField] private Animator _normalAnimator;
        [SerializeField] private Animator _frogAnimator;

        private Animator _active; // cuál está activo ahora mismo

        private static readonly int SpeedHash         = Animator.StringToHash("Speed");
        private static readonly int CastSpellWindHash  = Animator.StringToHash("CastSpellWind");
        private static readonly int CastSpellFrogHash  = Animator.StringToHash("CastSpellFrog");

        private void Awake()
        {
            _active = _normalAnimator;
        }

        private void Update()
        {
            if (_controller == null || _active == null) return;
            UpdateLocomotion();
        }

        // ── Locomotión ────────────────────────────────────────────────────────
        private void UpdateLocomotion()
        {
            PlayerState current = _controller.CurrentState;
            float speed = 0f;

            if (current is MovingState || current is FroggedState)
            {
                // FroggedState también camina, pero más lento — el speed lo refleja
                speed = (current is BoostedState) ? 2f : 1f;
            }
            else if (current is BoostedState)
                speed = 2f;

            if (speed > 0f)
                _active.SetFloat(SpeedHash, speed, 0.1f, Time.deltaTime);
            else
                _active.SetFloat(SpeedHash, 0f);
        }

        // ── Switch de modelo ──────────────────────────────────────────────────

        /// <summary>
        /// Llamado por VFXController al activar/desactivar la transformación en rana.
        /// Sincroniza la velocidad entre los dos Animators para evitar un salto visual.
        /// </summary>
        public void SwitchToFrog(bool frog)
        {
            if (frog)
            {
                float currentSpeed = _normalAnimator ? _normalAnimator.GetFloat(SpeedHash) : 0f;
                if (_frogAnimator) _frogAnimator.SetFloat(SpeedHash, currentSpeed);
                _active = _frogAnimator;
            }
            else
            {
                float currentSpeed = _frogAnimator ? _frogAnimator.GetFloat(SpeedHash) : 0f;
                if (_normalAnimator) _normalAnimator.SetFloat(SpeedHash, currentSpeed);
                _active = _normalAnimator;
            }
        }

        // ── Triggers ──────────────────────────────────────────────────────────

        public void TriggerSpellWind() => _active?.SetTrigger(CastSpellWindHash);
        public void TriggerSpellFrog() => _active?.SetTrigger(CastSpellFrogHash);

        /// <summary>
        /// Establece el Speed directamente sin leer el estado de PlayerManager.
        /// Usado por RemotePlayerSync, donde PlayerManager está desactivado.
        /// </summary>
        public void SetSpeedManual(float speed)
        {
            if (_active == null) return;
            if (speed > 0f)
                _active.SetFloat(SpeedHash, speed, 0.1f, Time.deltaTime);
            else
                _active.SetFloat(SpeedHash, 0f);
        }
    }
}
