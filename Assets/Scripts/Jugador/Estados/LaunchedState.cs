using UnityEngine;

namespace RuneRush.Player
{
     // ══════════════════════════════════════════════════════════════════════════
    // LAUNCHED — impulsado por viento rival o por impacto de meteoro
    //
    // El servidor envía dirección y magnitud del impulso.
    // El Rigidbody aplica la fuerza y el drag la frena gradualmente.
    // El input del jugador está bloqueado durante el vuelo.
    // ══════════════════════════════════════════════════════════════════════════
    public class LaunchedState : PlayerState
    {
        private Vector3 _force;
        private float   _duration;
        private float   _timer;
        private float   _originalDrag;
 
        /// <summary>Llamar antes de ChangeState para indicar fuerza y duración.</summary>
        public void SetForce(Vector3 force, float duration)
        {
            _force    = force;
            _duration = duration > 0f ? duration : 0.8f; // fallback si el servidor no lo envía
        }
 
        public override void Enter()
        {
            _timer = 0f;
            // Guardar drag original para restaurarlo al salir
            _originalDrag = Rb.linearDamping;
            Rb.linearDamping = Data.LaunchDrag;
 
            // Aplicar impulso inicial
            Rb.linearVelocity = Vector3.zero;
            Rb.AddForce(_force, ForceMode.VelocityChange);
 
            Controller.VFX?.PlayEffect("launched");
            Controller.HUD?.ShowEffectIcon("launched");
        }
 
        public override void Update()
        {
            _timer += Time.deltaTime;
 
            // Salir cuando se cumple la duración O cuando la velocidad horizontal
            // es casi cero (el drag ya frenó al jugador naturalmente)
            float horizontalSpeed = new Vector3(Rb.linearVelocity.x, 0f, Rb.linearVelocity.z).magnitude;
            bool timedOut   = _timer >= _duration;
            bool almostStop = horizontalSpeed < 0.2f;
 
            if (timedOut || almostStop)
                ReturnToMovementState();
        }
 
        public override void Exit()
        {
            _timer = 0f;
            Rb.linearDamping = _originalDrag;
            Controller.VFX?.StopEffect("launched");
            Controller.HUD?.HideEffectIcon("launched");
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
