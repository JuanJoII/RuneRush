using UnityEngine;

namespace RuneRush.Player
{
    // ══════════════════════════════════════════════════════════════════════════
    // FROGGED — transformado en rana por un rival, velocidad reducida
    // ══════════════════════════════════════════════════════════════════════════
    public class FroggedState : PlayerState
    {
        private float _timer;

        public override void Enter()
        {
            _timer = 0f;
            Controller.VFX?.PlayEffect("frogged");
            Controller.HUD?.ShowEffectIcon("frog");
            // El modelo del jugador cambia a rana (VFXController lo maneja)
        }

        public override void Update()
        {
            _timer += Time.deltaTime;
            if (_timer >= Data.FrogDuration)
                ReturnToMovementState();
        }

        public override void FixedUpdate()
        {
            Vector2 input = Controller.MoveInput;
            if (input.sqrMagnitude <= 0.01f) return;

            float yaw = Controller.CameraYaw * Mathf.Deg2Rad;
            Vector3 fwd = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
            Vector3 right = new Vector3(Mathf.Cos(yaw), 0f, -Mathf.Sin(yaw));
            Vector3 dir = (fwd * input.y + right * input.x).normalized;

            Vector3 velocity = dir * Data.FroggedSpeed;
            velocity.y = Rb.linearVelocity.y;
            Rb.linearVelocity = velocity;

            Quaternion targetRot = Quaternion.LookRotation(dir);
            Rb.MoveRotation(Quaternion.Slerp(Rb.rotation, targetRot, 8f * Time.fixedDeltaTime));

            GameClient.Instance?.SendMove(Rb.position.x, Rb.position.z, "frogged");
        }

        public override void Exit()
        {
            _timer = 0f;
            Controller.VFX?.StopEffect("frogged");
            Controller.HUD?.HideEffectIcon("frog");
            // VFXController restaura el modelo original del jugador
        }

        public override void OnNetworkEvent(NetworkEvent evt)
        {
            if (evt.Type == NetworkEventType.MatchEnd)
                Controller.ChangeState(Controller.StateFinished);
            // Nota: Launched tiene prioridad — un meteoro puede impactar a una rana
            if (evt.Type == NetworkEventType.EffectApplied && evt.Effect == EffectType.Launched)
                Controller.StateIdle.HandleCommonEffects(evt);
        }

        private void ReturnToMovementState()
        {
            bool hasInput = Controller.MoveInput.sqrMagnitude > 0.01f;
            Controller.ChangeState(hasInput
                ? (PlayerState)Controller.StateMoving
                : Controller.StateIdle);
        }
    }
}
