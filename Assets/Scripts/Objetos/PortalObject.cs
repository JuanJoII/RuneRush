using UnityEngine;

/// <summary>
/// PortalObject — portal ambiental spawneado determinísticamente al inicio de partida.
/// Cuando el jugador local entra, lo teletransporta al portal par.
/// El cooldown se aplica a AMBOS portales para evitar el rebote inmediato.
/// </summary>
public class PortalObject : MonoBehaviour
{
    public string PortalId = "";
    public string PairId   = "";

    [SerializeField] private float _cooldownDuration = 2f;

    private bool  _onCooldown    = false;
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
        if (_cooldownTimer >= _cooldownDuration)
        {
            _onCooldown    = false;
            _cooldownTimer = 0f;
        }
    }

    /// <summary>
    /// Inicia el cooldown desde fuera — GameManager lo llama en el portal
    /// de destino para evitar que el jugador que acaba de llegar sea
    /// teletransportado de vuelta inmediatamente.
    /// </summary>
    public void StartCooldown()
    {
        _onCooldown    = true;
        _cooldownTimer = 0f;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_onCooldown) return;

        var pm = other.GetComponent<RuneRush.Player.PlayerManager>();
        if (pm == null) return;

        string localId = GameClient.Instance ? GameClient.Instance.PlayerId : "";
        if (pm.PlayerId != localId) return;

        // Activar cooldown en este portal antes de teletransportar
        StartCooldown();

        GameManager.Instance?.OnLocalPlayerEnterPortal(PortalId, PairId, pm);
    }
}