using UnityEngine;

namespace RuneRush.Player
{
    // ══════════════════════════════════════════════════════════════════════════
    // IDLE — jugador quieto, sin input
    // ══════════════════════════════════════════════════════════════════════════
    public class IdleState : PlayerState
    {
        public override void Enter()
        {
            // Detener cualquier velocidad residual
            Rb.linearVelocity = new Vector3(0f, Rb.linearVelocity.y, 0f);
            Controller.VFX?.StopEffect("move");
        }

        public override void Update()
        {
            // Si hay input de movimiento, transicionar a Moving
            if (Controller.MoveInput.sqrMagnitude > 0.01f)
                Controller.ChangeState(Controller.StateMoving);
        }

        public override void Exit()
        {
        }

        public override void OnNetworkEvent(NetworkEvent evt)
        {
            HandleCommonEffects(evt);
        }

        // Los efectos recibidos (frogged, launched, teleport) son comunes
        // a Idle y Moving — se centralizan aquí y Moving los reutiliza.
        internal void HandleCommonEffects(NetworkEvent evt)
        {
            switch (evt.Type)
            {
                case NetworkEventType.EffectApplied:
                    switch (evt.Effect)
                    {
                        case EffectType.Frogged:
                            Controller.ChangeState(Controller.StateFrogged);
                            break;
                        case EffectType.Launched:
                            Controller.StateLaunched.SetForce(evt.LaunchForce, evt.EffectDuration);
                            Controller.ChangeState(Controller.StateLaunched);
                            break;
                        case EffectType.Boost:
                            Controller.ChangeState(Controller.StateBoosted);
                            break;
                        case EffectType.Teleport:
                            Controller.StateTeleporting.SetTarget(evt.TeleportTarget);
                            Controller.ChangeState(Controller.StateTeleporting);
                            break;
                    }

                    break;

                case NetworkEventType.MatchEnd:
                    Controller.ChangeState(Controller.StateFinished);
                    break;
            }
        }
    }
}