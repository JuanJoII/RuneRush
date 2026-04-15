using UnityEngine;

namespace RuneRush.Player
{
    // ══════════════════════════════════════════════════════════════════════════
    // BOOSTED — power-up de viento propio, velocidad aumentada temporalmente
    // ══════════════════════════════════════════════════════════════════════════
    public class BoostedState : PlayerState
    {
        private float _timer;

        public override void Enter()
        {
            _timer = 0f;
            Controller.VFX?.PlayEffect("boost");
        }

        public override void Update()
        {
            _timer += Time.deltaTime;
            if (_timer >= Data.BoostDuration)
                ReturnToMovementState();
        }

        public override void FixedUpdate()
        {
            Vector2 input = Controller.MoveInput;
            if (input.sqrMagnitude <= 0.01f) return;

            // Mismo cálculo relativo a cámara que MovingState
            float yaw = Controller.CameraYaw * Mathf.Deg2Rad;
            Vector3 fwd = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
            Vector3 right = new Vector3(Mathf.Cos(yaw), 0f, -Mathf.Sin(yaw));
            Vector3 dir = (fwd * input.y + right * input.x).normalized;

            Vector3 velocity = dir * Data.BoostedSpeed;
            velocity.y = SafeYVelocity;
            Rb.linearVelocity = velocity;

            Quaternion targetRot = Quaternion.LookRotation(dir);
            Rb.MoveRotation(Quaternion.Slerp(Rb.rotation, targetRot, 12f * Time.fixedDeltaTime));

            GameClient.Instance?.SendMove(Rb.position.x, Rb.position.z, "boosted");
        }

        public override void Exit()
        {
            Controller.VFX?.StopEffect("boost");
            _timer = 0f;
        }

        public override void OnNetworkEvent(NetworkEvent evt)
        {
            // Frogged o Launched interrumpen el boost
            if (evt.Type == NetworkEventType.EffectApplied &&
                (evt.Effect == EffectType.Frogged || evt.Effect == EffectType.Launched))
            {
                Controller.StateIdle.HandleCommonEffects(evt);
                return;
            }

            if (evt.Type == NetworkEventType.MatchEnd)
                Controller.ChangeState(Controller.StateFinished);
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
