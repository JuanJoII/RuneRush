using UnityEngine;

/// <summary>
/// PortalObject — portal ambiental spawneado por el servidor.
///
/// Cuando el jugador local entra en el trigger, notifica al GameManager
/// que inicie el teletransporte. El servidor decide el destino.
///
/// Configuración del prefab:
///   - Collider en modo Trigger (el script lo garantiza en Start)
///   - Partículas o mesh visual de portal (giran solos, sin código extra)
///   - Layer: cualquiera que NO sea Ground (para no interferir con raycast)
/// </summary>
public class PortalObject : MonoBehaviour
{
    public string PortalId   = "";
    public string PairId     = ""; // ID del portal de destino

    // Cooldown para evitar múltiples activaciones si el jugador se queda en el trigger
    private bool  _onCooldown  = false;
    private float _cooldown    = 2f;
    private float _cooldownTimer = 0f;

    private void Start()
    {
        var col = GetComponent<Collider>();
        if (col) col.isTrigger = true;
    }

    private void Update()
    {
        if (!_onCooldown) return;
        _cooldownTimer += Time.deltaTime;
        if (_cooldownTimer >= _cooldown)
        {
            _onCooldown    = false;
            _cooldownTimer = 0f;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_onCooldown) return;

        var pm = other.GetComponent<RuneRush.Player.PlayerManager>();
        if (pm == null) return;

        string localId = GameClient.Instance ? GameClient.Instance.PlayerId : "";
        if (pm.PlayerId != localId) return;

        _onCooldown    = true;
        _cooldownTimer = 0f;

        // Informar al GameManager — él maneja el teletransporte
        GameManager.Instance?.OnLocalPlayerEnterPortal(PortalId, PairId, pm);
    }
}