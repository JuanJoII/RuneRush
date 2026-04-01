using System.Collections;
using UnityEngine;

public class RunaObject : MonoBehaviour
{
    public string RunaId = "";
    public string ObjectType = "runa_comun";  // "runa_comun" | "powerup_viento"

    [SerializeField] private float rotSpeed = 80f;
    [SerializeField] private float bobHeight = 0.2f;
    [SerializeField] private float bobSpeed = 2.2f;

    private Vector3 _basePos;

    private void Start()
    {
        _basePos = transform.position;
        var col = GetComponent<Collider>();
        if (col) col.isTrigger = true;
    }

    private void Update()
    {
        transform.Rotate(Vector3.up, rotSpeed * Time.deltaTime);
        float y = _basePos.y + Mathf.Sin(Time.time * bobSpeed) * bobHeight;
        transform.position = new Vector3(_basePos.x, y, _basePos.z);
    }
}

public class RemotePlayerSync : MonoBehaviour
{
    public string PlayerId { get; set; } = "";

    [SerializeField] private float lerpSpeed = 14f;

    private Vector3 _targetPos;
    private float _boostMul = 1f;

    private void Awake() { _targetPos = transform.position; }

    private void Update()
    {
        if (Vector3.Distance(transform.position, _targetPos) > 0.01f)
        {
            transform.position = Vector3.MoveTowards(
                transform.position, _targetPos, lerpSpeed * _boostMul * Time.deltaTime);

            Vector3 dir = (_targetPos - transform.position).normalized;
            if (dir.magnitude > 0.01f)
                transform.rotation = Quaternion.Slerp(
                    transform.rotation, Quaternion.LookRotation(dir), Time.deltaTime * 10f);
        }
    }

    public void SetTarget(Vector3 pos) => _targetPos = pos;

    public void ApplySpeedBoost(float duration) => StartCoroutine(BoostRoutine(duration));

    private IEnumerator BoostRoutine(float duration)
    {
        _boostMul = 2f;
        var r = GetComponent<Renderer>(); Color orig = Color.white;
        if (r) { orig = r.material.color; r.material.color = Color.cyan; }
        yield return new WaitForSeconds(duration);
        _boostMul = 1f;
        if (r) r.material.color = orig;
    }
}