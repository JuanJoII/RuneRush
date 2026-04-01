using System.Collections;
using UnityEngine;

// ═══════════════════════════════════════════════════════════════════════════════
// RunaObject — Componente que identifica una runa en la escena.
//
// Solo guarda el ID de red. La lógica de recolección ocurre en:
//   - PlayerController.OnTriggerEnter → envía collect_request al servidor
//   - GameManager.OnCollectConfirm    → destruye este objeto si el servidor confirma
//
// También hace el efecto visual de flotación y rotación.
// ═══════════════════════════════════════════════════════════════════════════════
public class RunaObject : MonoBehaviour
{
    public string RunaId = "";

    [SerializeField] private float rotSpeed = 80f;   // grados/seg
    [SerializeField] private float bobHeight = 0.2f;
    [SerializeField] private float bobSpeed = 2.2f;

    private Vector3 _basePos;

    private void Start()
    {
        _basePos = transform.position;

        // Asegurar collider trigger
        var col = GetComponent<Collider>();
        if (col) col.isTrigger = true;
    }

    private void Update()
    {
        // Rotación
        transform.Rotate(Vector3.up, rotSpeed * Time.deltaTime);

        // Flotación (bobbing)
        float y = _basePos.y + Mathf.Sin(Time.time * bobSpeed) * bobHeight;
        transform.position = new Vector3(_basePos.x, y, _basePos.z);
    }
}


// ═══════════════════════════════════════════════════════════════════════════════
// RemotePlayerSync — Interpola suavemente la posición de jugadores remotos.
//
// Adjuntar automáticamente por GameManager a las cápsulas de jugadores remotos.
// Recibe SetTarget() cuando llega player_move del servidor.
// ═══════════════════════════════════════════════════════════════════════════════
public class RemotePlayerSync : MonoBehaviour
{
    public string PlayerId { get; set; } = "";

    [SerializeField] private float lerpSpeed = 14f;

    private Vector3 _targetPos;
    private float _boostMultiplier = 1f;

    private void Awake()
    {
        _targetPos = transform.position;
    }

    private void Update()
    {
        if (Vector3.Distance(transform.position, _targetPos) > 0.01f)
        {
            transform.position = Vector3.MoveTowards(
                transform.position,
                _targetPos,
                lerpSpeed * _boostMultiplier * Time.deltaTime);

            // Rotar hacia destino
            Vector3 dir = (_targetPos - transform.position).normalized;
            if (dir.magnitude > 0.01f)
                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    Quaternion.LookRotation(dir),
                    Time.deltaTime * 10f);
        }
    }

    public void SetTarget(Vector3 pos) => _targetPos = pos;

    public void ApplySpeedBoost(float duration) =>
        StartCoroutine(SpeedBoostRoutine(duration));

    private IEnumerator SpeedBoostRoutine(float duration)
    {
        _boostMultiplier = 2f;

        var renderer = GetComponent<Renderer>();
        Color original = Color.white;
        if (renderer) { original = renderer.material.color; renderer.material.color = Color.cyan; }

        yield return new WaitForSeconds(duration);

        _boostMultiplier = 1f;
        if (renderer) renderer.material.color = original;
    }
}