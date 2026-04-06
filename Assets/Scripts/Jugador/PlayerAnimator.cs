using UnityEngine;

namespace RuneRush.Player
{
    /// <summary>
    /// PlayerAnimator — puente entre la máquina de estados y el Animator.
    ///
    /// Parámetros requeridos en el Animator Controller:
    ///   - "Speed"        : Float   — 0 = idle, 1 = caminando, 2 = boosted
    ///   - "CastSpellWind": Trigger — animación de hechizo de viento/portal
    ///   - "CastSpellFrog": Trigger — animación de hechizo de rana
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class PlayerAnimator : MonoBehaviour
    {
        [SerializeField] private PlayerManager _controller;

        private Animator _animator;

        private static readonly int SpeedHash        = Animator.StringToHash("Speed");
        private static readonly int CastSpellWindHash = Animator.StringToHash("CastSpellWind");
        private static readonly int CastSpellFrogHash = Animator.StringToHash("CastSpellFrog");

        private void Awake()
        {
            _animator = GetComponent<Animator>();
        }

        private void Update()
        {
            if (_controller == null || _animator == null) return;
            UpdateLocomotion();
        }

        private void UpdateLocomotion()
        {
            PlayerState current = _controller.CurrentState;
            float speed = 0f;

            if (current is MovingState)
                speed = 1f;
            else if (current is BoostedState)
                speed = 2f;

            // Damping al acelerar, corte inmediato al parar
            if (speed > 0f)
                _animator.SetFloat(SpeedHash, speed, 0.1f, Time.deltaTime);
            else
                _animator.SetFloat(SpeedHash, 0f);
        }

        /// <summary>Hechizo de viento o portal. Llamar desde GameManager.</summary>
        public void TriggerSpellWind() => _animator.SetTrigger(CastSpellWindHash);

        /// <summary>Hechizo de rana. Llamar desde GameManager.</summary>
        public void TriggerSpellFrog() => _animator.SetTrigger(CastSpellFrogHash);
    }
}
