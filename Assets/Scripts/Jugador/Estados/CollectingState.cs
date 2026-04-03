using UnityEngine;

namespace RuneRush.Player
{
    // ══════════════════════════════════════════════════════════════════════════
    // COLLECTING — esperando collect_confirm o collect_deny del servidor
    //
    // RIESGO P2P: si el host tiene lag o se desconecta, la respuesta puede
    // no llegar nunca. El timeout garantiza que el jugador no quede bloqueado.
    //
    // FASE 3: el servidor hace broadcast de collect_confirm a TODOS los clientes.
    // NetworkEventHandler filtra por PlayerId antes de llamar OnNetworkEvent,
    // así que aquí solo llegan eventos del jugador local.
    // ══════════════════════════════════════════════════════════════════════════
    public class CollectingState : PlayerState
    {
        private string _pendingRuneId;
        private float  _timeoutTimer;

        /// <summary>
        /// Llamar antes de ChangeState para indicar qué runa se está intentando recoger.
        /// </summary>
        public void SetPendingRune(string runeId)
        {
            _pendingRuneId = runeId;
        }

        public override void Enter()
        {
            _timeoutTimer = 0f;
            // El jugador puede seguir moviéndose mientras espera confirmación.
            // La velocidad se mantiene igual que en Moving.
        }

        public override void Update()
        {
            // Acumular tiempo de espera
            _timeoutTimer += Time.deltaTime;

            if (_timeoutTimer >= Data.CollectTimeout)
            {
                // El servidor no respondió a tiempo (lag alto o host caído).
                // Cancelar silenciosamente y volver al estado anterior.
                Debug.LogWarning($"[CollectingState] Timeout esperando respuesta del servidor para runa {_pendingRuneId}.");
                ReturnToMovementState();
            }
        }

        public override void FixedUpdate()
        {
            // Mantener movimiento durante la espera (misma lógica que MovingState)
            Vector2 input = Controller.MoveInput;
            Vector3 move  = new Vector3(input.x, 0f, input.y) * Data.MoveSpeed;
            move.y = Rb.linearVelocity.y;
            Rb.linearVelocity = move;
        }

        public override void Exit()
        {
            _pendingRuneId = null;
            _timeoutTimer  = 0f;
        }

        public override void OnNetworkEvent(NetworkEvent evt)
        {
            switch (evt.Type)
            {
                case NetworkEventType.CollectConfirm:
                    // Solo procesar si es la runa que estamos esperando
                    if (evt.RuneId == _pendingRuneId)
                    {
                        Controller.VFX?.PlayCollect();
                        Controller.HUD?.OnRuneCollected();
                        ReturnToMovementState();
                    }
                    break;

                case NetworkEventType.CollectDeny:
                    // Otro jugador llegó primero — cancelar sin feedback visual de error
                    if (evt.RuneId == _pendingRuneId)
                        ReturnToMovementState();
                    break;

                case NetworkEventType.MatchEnd:
                    Controller.ChangeState(Controller.StateFinished);
                    break;

                // Los efectos de red (frogged, launched) tienen prioridad
                // sobre la recolección pendiente
                case NetworkEventType.EffectApplied:
                    Controller.StateIdle.HandleCommonEffects(evt);
                    break;
            }
        }

        private void ReturnToMovementState()
        {
            bool hasInput = Controller.MoveInput.sqrMagnitude > 0.01f;
            Controller.ChangeState(hasInput ? (PlayerState)Controller.StateMoving
                                            : Controller.StateIdle);
        }
    }
}
