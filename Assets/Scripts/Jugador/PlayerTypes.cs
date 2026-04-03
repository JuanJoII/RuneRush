using UnityEngine;

namespace RuneRush.Player
{
    // ── Tipos de eventos de red que le interesan al jugador ───────────────────
    public enum NetworkEventType
    {
        CollectConfirm,     // Servidor confirmó recolección de runa
        CollectDeny,        // Servidor rechazó recolección (ya la tomó otro)
        EffectApplied,      // Servidor aplicó un efecto sobre este jugador
        StateUpdate,        // Actualización periódica de posiciones y puntajes
        MatchEnd,           // Fin de partida
        PlayerLeft,         // Un jugador se desconectó
    }

    public enum EffectType
    {
        Boost,              // Viento propio: aumento de velocidad
        Frogged,            // Transformación en rana: velocidad reducida
        Launched,           // Impulso de viento o impacto de meteoro
        Teleport,           // Portal propio: teletransporte
    }

    /// <summary>
    /// Paquete genérico que llega del servidor al PlayerController.
    /// El estado activo decide si le interesa o no.
    /// </summary>
    public class NetworkEvent
    {
        public NetworkEventType Type;
        public string           RuneId;         // Para collect_confirm / collect_deny
        public EffectType       Effect;         // Para effect_applied
        public Vector3          LaunchForce;    // Para Launched (dirección + magnitud)
        public Vector3          TeleportTarget; // Para Teleport
        public float            EffectDuration; // Duración del efecto temporal (segundos)
    }

    /// <summary>
    /// Datos de configuración del jugador — se asignan desde el Inspector
    /// en PlayerController y son de solo lectura para los estados.
    /// </summary>
    [System.Serializable]
    public class PlayerData
    {
        [Header("Movimiento")]
        public float MoveSpeed       = 5f;
        public float BoostedSpeed    = 9f;
        public float FroggedSpeed    = 2.5f;

        [Header("Power-ups")]
        public float BoostDuration   = 4f;
        public float FrogDuration    = 3f;

        [Header("Collecting")]
        public float CollectTimeout  = 2f;   // Segundos antes de cancelar si el servidor no responde

        [Header("Launched")]
        public float LaunchDrag      = 3f;   // Cuánta resistencia se aplica al frenar el impulso
    }
}
