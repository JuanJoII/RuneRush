using UnityEngine;

namespace RuneRush.Player
{
    /// <summary>
    /// PlayerAnimator — puente entre la máquina de estados y el Animator.
    ///
    /// No toma decisiones de juego — solo observa el PlayerController
    /// y traduce el estado actual a parámetros del Animator.
    ///
    /// Parámetros que debe tener tu Animator Controller:
    ///   - "Speed"     : Float  — 0 = idle, 1 = caminando, 2 = boosted
    ///   - "IsFrogged" : Bool   — true mientras está transformado en rana
    ///   - "IsLaunched": Bool   — true mientras está siendo impulsado
    ///   - "CastSpell" : Trigger — se dispara al activar los hechizos
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class PlayerAnimator : MonoBehaviour
    {
        [SerializeField] private PlayerManager _controller;

        private Animator _animator;

        // Hashes para no usar strings en Update (más eficiente)
        private static readonly int SpeedHash      = Animator.StringToHash("Speed");
        private static readonly int IsFroggedHash  = Animator.StringToHash("IsFrogged");
        private static readonly int IsLaunchedHash = Animator.StringToHash("IsLaunched");
        private static readonly int CastSpellWindHash   = Animator.StringToHash("CastSpellWind");
        private static readonly int CastSpellFrogHash  = Animator.StringToHash("CastSpellFrog");

        private void Awake()
        {
            _animator = GetComponent<Animator>();
        }

        private void Update()
        {
            if (_controller == null || _animator == null) return;

            UpdateLocomotion();
        }

        // ── Locomotión ────────────────────────────────────────────────────────

        private void UpdateLocomotion()
        {
            PlayerState current = _controller.CurrentState;
            float speed = 0f;

            if (current is MovingState)
                speed = 1f;
            else if (current is BoostedState)
                speed = 2f;  // animación de correr más rápido
            else if (current is FroggedState && _controller.MoveInput.sqrMagnitude > 0.01f)
                speed = 0.5f; // animación de saltar/moverse como rana

            _animator.SetFloat(SpeedHash, speed, 0.1f, Time.deltaTime); // damping suave

            if (speed == 0f)
            {
                _animator.SetFloat(SpeedHash, speed); 
            }
        }

        // ── Triggers (llamados desde PlayerController) ────────────────────────

        /// <summary>Llamar cuando el jugador activa el hechizo de impulso.</summary>
        public void TriggerTeleport()  => _animator.SetTrigger(CastSpellWindHash);

        /// <summary>Llamar cuando el jugador activa el hechizo de rana.</summary>
        public void TriggerCastSpell() => _animator.SetTrigger(CastSpellFrogHash);
    }
}
