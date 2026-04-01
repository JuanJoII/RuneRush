using System.Collections;
using UnityEngine;

/// <summary>
/// PlayerController — Fase 2: Movimiento WASD y power-up viento propio.
///
/// Adjuntar al GameObject del jugador LOCAL.
/// Responsabilidades:
///   - Leer input WASD / flechas y mover la cápsula.
///   - Enviar la posición al servidor cada sendInterval segundos.
///   - Al entrar en trigger con RunaObject, enviar collect_request.
///   - Al pulsar E (o el botón de HUD), enviar powerup_activate viento_propio.
///   - Recibir ApplySpeedBoost() desde GameManager cuando el servidor confirma.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    // ── Configuración ──────────────────────────────────────────────────────────
    [Header("Movimiento")]
    [SerializeField] private float baseSpeed = 6f;
    [SerializeField] private float speedBoostMul = 2f;    // multiplicador viento propio
    [SerializeField] private float sendInterval = 0.05f; // 20 veces/seg

    // ── Propiedades públicas ───────────────────────────────────────────────────
    public string PlayerId { get; set; } = "";

    // ── Estado interno ─────────────────────────────────────────────────────────
    private CharacterController _cc;
    private float _currentSpeed;
    private float _nextSendTime;
    private bool _boosted = false;

    // ── Unity ──────────────────────────────────────────────────────────────────
    private void Awake()
    {
        _cc = GetComponent<CharacterController>();
        _currentSpeed = baseSpeed;
    }

    private void Update()
    {
        // ── Movimiento ─────────────────────────────────────────────────────────
        float h = Input.GetAxisRaw("Horizontal");  // A/D o ←/→
        float v = Input.GetAxisRaw("Vertical");    // W/S o ↑/↓

        Vector3 dir = new Vector3(h, 0f, v).normalized;

        if (dir.magnitude > 0.01f)
        {
            // Rotar hacia la dirección de movimiento
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                Quaternion.LookRotation(dir),
                Time.deltaTime * 12f);

            _cc.Move(dir * (_currentSpeed * Time.deltaTime));
        }

        // Gravedad simple
        _cc.Move(Vector3.down * (9.81f * Time.deltaTime));

        // ── Enviar posición al servidor ────────────────────────────────────────
        if (Time.time >= _nextSendTime && GameClient.Instance != null)
        {
            string state = dir.magnitude > 0.01f ? "moviendose" : "jugando";
            GameClient.Instance.SendMove(
                transform.position.x,
                transform.position.z,
                state);
            _nextSendTime = Time.time + sendInterval;
        }

        // ── Activar viento propio con tecla E ──────────────────────────────────
        if (Input.GetKeyDown(KeyCode.E) && !_boosted)
        {
            GameClient.Instance?.SendPowerupActivate("viento_propio");
        }
    }

    // ── Colisión con runas ─────────────────────────────────────────────────────
    private void OnTriggerEnter(Collider other)
    {
        var runa = other.GetComponent<RunaObject>();
        if (runa == null) return;

        // Solicitar al servidor. El objeto NO se destruye aquí:
        // solo desaparece cuando llega collect_confirm (en GameManager).
        GameClient.Instance?.SendCollectRequest(runa.RunaId, "runa_comun");
    }

    // ── API pública ────────────────────────────────────────────────────────────

    /// <summary>
    /// Llamado por GameManager cuando el servidor confirma powerup_confirm.
    /// </summary>
    public void ApplySpeedBoost(float duration)
    {
        if (_boosted) return;
        StartCoroutine(SpeedBoostRoutine(duration));
    }

    /// <summary>Botón HUD de viento propio (alternativa a tecla E).</summary>
    public void OnVientoPropioBtnPressed()
    {
        if (!_boosted)
            GameClient.Instance?.SendPowerupActivate("viento_propio");
    }

    // ── Coroutine ──────────────────────────────────────────────────────────────
    private IEnumerator SpeedBoostRoutine(float duration)
    {
        _boosted = true;
        _currentSpeed = baseSpeed * speedBoostMul;

        // Feedback visual sencillo: cambiar color temporalmente
        var renderer = GetComponent<Renderer>();
        Color original = Color.white;
        if (renderer) { original = renderer.material.color; renderer.material.color = Color.cyan; }

        yield return new WaitForSeconds(duration);

        _currentSpeed = baseSpeed;
        _boosted = false;
        if (renderer) renderer.material.color = original;
    }
}