using UnityEngine;

namespace RuneRush.Player
{
    // ══════════════════════════════════════════════════════════════════════════
    // MOVING — jugador desplazándose con el joystick
    // ══════════════════════════════════════════════════════════════════════════
    public class MovingState : PlayerState
    {
        public override void Enter()
        {
            Controller.VFX?.PlayEffect("move");
        }
 
        public override void Update()
        {
            // La transición a Idle se evalúa en Update para respuesta inmediata,
            // pero la velocidad se aplica en FixedUpdate para consistencia física.
            if (Controller.MoveInput.sqrMagnitude <= 0.01f)
                Controller.ChangeState(Controller.StateIdle);
        }
 
        public override void FixedUpdate()
        {
            Vector2 input = Controller.MoveInput;
            if (input.sqrMagnitude <= 0.01f) return;
 
            // ── Movimiento relativo a la cámara ───────────────────────────────
            // Rotamos el input 2D por el yaw actual de la cámara.
            // Así "arriba" en el joystick siempre es "hacia donde mira la cámara".
            float yaw      = Controller.CameraYaw * Mathf.Deg2Rad;
            float cos      = Mathf.Cos(yaw);
            float sin      = Mathf.Sin(yaw);
 
            // input.y = adelante/atrás, input.x = izquierda/derecha
            Vector3 forward = new Vector3( sin,  0f, cos);
            Vector3 right   = new Vector3( cos,  0f, -sin);
            Vector3 moveDir = (forward * input.y + right * input.x).normalized;
 
            Vector3 velocity = moveDir * Data.MoveSpeed;
            velocity.y       = Rb.linearVelocity.y; // conservar gravedad
            Rb.linearVelocity = velocity;
 
            // Rotar el personaje hacia la dirección de movimiento
            Quaternion targetRot = Quaternion.LookRotation(moveDir);
            Rb.MoveRotation(Quaternion.Slerp(Rb.rotation, targetRot, 12f * Time.fixedDeltaTime));
 
            // Sincronizar con servidor
            GameClient.Instance?.SendMove(Rb.position.x, Rb.position.z, "moviendose");
        }
 
        public override void Exit()
        {
            Controller.VFX?.StopEffect("move");
            GameClient.Instance?.SendMove(Rb.position.x, Rb.position.z, "idle");
        }
 
        public override void OnNetworkEvent(NetworkEvent evt)
        {
            Controller.StateIdle.HandleCommonEffects(evt);
        }
    }
}