using UnityEngine;

public class RunaObject : MonoBehaviour
{
    public string RunaId     = "";
    public string ObjectType = "runa_comun"; // "runa_comun" | "powerup_viento"

    [SerializeField] private float rotSpeed  = 80f;
    [SerializeField] private float bobHeight = 0.2f;
    [SerializeField] private float bobSpeed  = 2.2f;

    private Vector3    _basePos;
    private Quaternion _baseRotation;
    private float      _currentYRotation = 0f;
    private bool       _collected        = false;

    private void Start()
    {
        _basePos           = transform.position;

        if (ObjectType == "runa_comun")
        {
            _baseRotation      = Quaternion.Euler(-90f, 0f, 0f);
            transform.rotation = _baseRotation;
        }


        var col = GetComponent<Collider>();
        if (col) col.isTrigger = true;
    }

    private void Update()
    {
        if (_collected) return;

        _currentYRotation += rotSpeed * Time.deltaTime;
        transform.rotation = _baseRotation * Quaternion.Euler(0f, _currentYRotation, 0f);

        float y = _basePos.y + Mathf.Sin(Time.time * bobSpeed) * bobHeight;
        transform.position = new Vector3(_basePos.x, y, _basePos.z);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_collected) return;

        var pm = other.GetComponent<RuneRush.Player.PlayerManager>();
        if (pm == null) return;

        string localId = GameClient.Instance ? GameClient.Instance.PlayerId : "";
        if (pm.PlayerId != localId) return;

        // Si el jugador está en estado rana no puede recoger nada
        if (pm.CurrentState is RuneRush.Player.FroggedState) return;

        // Si es un power-up y el jugador ya tiene uno sin usar, ignorar
        if (ObjectType == "powerup_viento" && pm.HasPowerup)
        {
            Debug.Log("[RunaObject] El jugador ya tiene un power-up activo.");
            return;
        }

        _collected = true;

        GameClient.Instance.SendCollectRequest(RunaId, ObjectType);

        var collectingState = pm.StateCollecting;
        collectingState?.SetPendingRune(RunaId);
        pm.ChangeState(collectingState);
    }
}

    
