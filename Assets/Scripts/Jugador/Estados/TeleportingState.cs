using UnityEngine;

namespace RuneRush.Player
{
 // ══════════════════════════════════════════════════════════════════════════
    // TELEPORTING — power-up de portal propio
    //
    // El servidor confirma la posición de destino antes de mover al jugador.
    // Durante el frame de teletransporte se bloquea el input.
    // ══════════════════════════════════════════════════════════════════════════
    public class TeleportingState : PlayerState
    {
        private Vector3 _target;
        private bool    _arrived;
 
        /// <summary>Llamar antes de ChangeState para indicar el destino.</summary>
        public void SetTarget(Vector3 target)
        {
            _target  = target;
            _arrived = false;
        }
 
        public override void Enter()
        {
            // Detener movimiento mientras se ejecuta la animación de salida
            Rb.linearVelocity = new Vector3(0f, Rb.linearVelocity.y, 0f);
        }
 
        public override void Update()
        {
            // La transición real ocurre tras el VFX de salida.
            // VFXController llama OnArrival() cuando termina la animación.
        }
 
        /// <summary>
        /// Llamado por VFXController al terminar la animación de salida del portal.
        /// </summary>
        public void OnArrival()
        {
            if (_arrived) return;
            _arrived = true;
 
            // Teletransportar físicamente al jugador
            Rb.MovePosition(_target);
 
            ReturnToMovementState();
        }
 
        public override void Exit()
        {
        }
 
        public override void OnNetworkEvent(NetworkEvent evt)
        {
            if (evt.Type == NetworkEventType.MatchEnd)
                Controller.ChangeState(Controller.StateFinished);
        }
 
        private void ReturnToMovementState()
        {
            bool hasInput = Controller.MoveInput.sqrMagnitude > 0.01f;
            Controller.ChangeState(hasInput ? (PlayerState)Controller.StateMoving
                                            : Controller.StateIdle);
        }
    }
}

