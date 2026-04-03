using UnityEngine;

namespace RuneRush.Player
{
    /// <summary>
    /// NetworkEventHandler — Fase 3.
    ///
    /// Se suscribe a los eventos específicos de GameClient (OnCollectConfirm,
    /// OnCollectDeny, OnPowerupConfirm, OnMatchEnd) y los traduce a
    /// NetworkEvent para el PlayerController local.
    ///
    /// NOTA: El boost de viento (powerup_viento) lo aplica directamente
    /// el GameManager llamando PlayerController.ApplySpeedBoost().
    /// Este handler solo procesa eventos que afectan al estado del jugador
    /// que no pasan por el GameManager.
    /// </summary>
    public class NetworkEventHandler : MonoBehaviour
    {
        [SerializeField] private PlayerManager _controller;

        private void OnEnable()
        {
            if (GameClient.Instance == null) return;

            GameClient.Instance.OnCollectConfirm.AddListener(OnCollectConfirm);
            GameClient.Instance.OnCollectDeny.AddListener(OnCollectDeny);
            GameClient.Instance.OnPowerupConfirm.AddListener(OnPowerupConfirm);
            GameClient.Instance.OnMatchEnd.AddListener(OnMatchEnd);
        }

        private void OnDisable()
        {
            if (GameClient.Instance == null) return;

            GameClient.Instance.OnCollectConfirm.RemoveListener(OnCollectConfirm);
            GameClient.Instance.OnCollectDeny.RemoveListener(OnCollectDeny);
            GameClient.Instance.OnPowerupConfirm.RemoveListener(OnPowerupConfirm);
            GameClient.Instance.OnMatchEnd.RemoveListener(OnMatchEnd);
        }

        // ── Handlers ──────────────────────────────────────────────────────────

        /// <summary>
        /// collect_confirm — llega a TODOS los clientes (broadcast).
        /// Solo actuar si va dirigido al jugador local.
        /// El GameManager ya destruye el objeto en escena para todos.
        /// </summary>
        private void OnCollectConfirm(string json)
        {
            string pid        = GameServer.ExtractString(json, "playerId");
            string objectType = GameServer.ExtractString(json, "objectType");

            if (pid != _controller.PlayerId) return;

            string objectId = GameServer.ExtractString(json, "objectId");

            // powerup_viento: el boost lo aplica GameManager via ApplySpeedBoost().
            // Aquí solo notificamos al estado que la recolección fue confirmada.
            _controller.OnNetworkEvent(new NetworkEvent
            {
                Type   = NetworkEventType.CollectConfirm,
                RuneId = objectId,
            });
        }

        /// <summary>
        /// collect_deny — solo llega al cliente que hizo la solicitud.
        /// </summary>
        private void OnCollectDeny(string json)
        {
            string pid      = GameServer.ExtractString(json, "playerId");
            string objectId = GameServer.ExtractString(json, "objectId");

            if (pid != _controller.PlayerId) return;

            _controller.OnNetworkEvent(new NetworkEvent
            {
                Type   = NetworkEventType.CollectDeny,
                RuneId = objectId,
            });
        }

        /// <summary>
        /// powerup_confirm — broadcast. Procesar solo el portal propio.
        /// El boost de viento va por collect_confirm + GameManager.
        /// </summary>
        private void OnPowerupConfirm(string json)
        {
            string pid         = GameServer.ExtractString(json, "playerId");
            string powerupType = GameServer.ExtractString(json, "powerupType");

            if (pid != _controller.PlayerId) return;

            if (powerupType == "portal_propio")
            {
                float destX = GameServer.ExtractFloatInObject(json, "destinationPosition", "x");
                float destZ = GameServer.ExtractFloatInObject(json, "destinationPosition", "z");

                // GameManager ya mueve el transform (teletransporte inmediato).
                // Aquí activamos el VFX y el estado de teletransporte.
                _controller.StateTeleporting.SetTarget(new UnityEngine.Vector3(destX, 1f, destZ));
                _controller.OnNetworkEvent(new NetworkEvent
                {
                    Type   = NetworkEventType.EffectApplied,
                    Effect = EffectType.Teleport,
                });
            }
        }

        /// <summary>
        /// match_end — broadcast. Congelar al jugador local.
        /// </summary>
        private void OnMatchEnd(string json)
        {
            _controller.OnNetworkEvent(new NetworkEvent
            {
                Type = NetworkEventType.MatchEnd,
            });
        }
    }
}
