using UnityEngine;

namespace RuneRush.Player
{
    // ══════════════════════════════════════════════════════════════════════════
    // FINISHED — partida terminada, jugador congelado
    // ══════════════════════════════════════════════════════════════════════════
    public class FinishedState : PlayerState
    {
        public override void Enter()
        {
            // Detener movimiento y desactivar física
            Rb.linearVelocity = Vector3.zero;
            Rb.isKinematic    = true;

            // Detener todos los VFX activos
            Controller.VFX?.StopAllEffects();
        }

        public override void Update()
        {
            // No hay nada que hacer — MatchResultManager toma el control
        }

        public override void Exit()
        {
            // Se llama al volver al menú / reiniciar partida
            Rb.isKinematic = false;
        }

        // En este estado se ignoran todos los eventos de red
    }
}
