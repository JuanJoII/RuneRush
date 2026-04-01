using System.Collections;
using UnityEngine;

/// <summary>
/// PlayerController — Fase 3.
/// Cambios:
///   - La tecla E ya no activa viento (se recoge del mapa).
///   - La tecla Q envía powerup_activate "portal_propio".
///   - ApplySpeedBoost sigue igual (lo llama GameManager cuando llega collect_confirm).
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Movimiento")]
    [SerializeField] private float baseSpeed = 8f;
    [SerializeField] private float speedBoostMul = 2f;
    [SerializeField] private float sendInterval = 0.05f;

    public string PlayerId { get; set; } = "";

    private CharacterController _cc;
    private float _currentSpeed;
    private float _nextSendTime;
    private bool _boosted = false;

    private void Awake()
    {
        _cc = GetComponent<CharacterController>();
        _currentSpeed = baseSpeed;
    }

    private void Update()
    {
        // Movimiento
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        Vector3 dir = new Vector3(h, 0f, v).normalized;

        if (dir.magnitude > 0.01f)
        {
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                Quaternion.LookRotation(dir),
                Time.deltaTime * 12f);
            _cc.Move(dir * (_currentSpeed * Time.deltaTime));
        }

        _cc.Move(Vector3.down * (9.81f * Time.deltaTime));

        // Enviar posición al servidor
        if (Time.time >= _nextSendTime && GameClient.Instance != null)
        {
            string state = dir.magnitude > 0.01f ? "moviendose" : "jugando";
            GameClient.Instance.SendMove(transform.position.x, transform.position.z, state);
            _nextSendTime = Time.time + sendInterval;
        }

        // Q → portal propio
        if (Input.GetKeyDown(KeyCode.Q))
            GameClient.Instance?.SendPowerupActivate("portal_propio");
    }

    private void OnTriggerEnter(Collider other)
    {
        var runa = other.GetComponent<RunaObject>();
        if (runa == null) return;
        // Enviar el objectType real para que el servidor sepa si es runa o viento
        GameClient.Instance?.SendCollectRequest(runa.RunaId, runa.ObjectType);
    }

    // Llamado por GameManager al recibir collect_confirm de powerup_viento
    public void ApplySpeedBoost(float duration)
    {
        if (_boosted) return;
        StartCoroutine(SpeedBoostRoutine(duration));
    }

    // Botón HUD "Portal"
    public void OnPortalButtonPressed()
    {
        GameClient.Instance?.SendPowerupActivate("portal_propio");
    }

    private IEnumerator SpeedBoostRoutine(float duration)
    {
        _boosted = true;
        _currentSpeed = baseSpeed * speedBoostMul;

        var renderer = GetComponent<Renderer>();
        Color original = Color.white;
        if (renderer) { original = renderer.material.color; renderer.material.color = Color.cyan; }

        yield return new WaitForSeconds(duration);

        _currentSpeed = baseSpeed;
        _boosted = false;
        if (renderer) renderer.material.color = original;
    }
}