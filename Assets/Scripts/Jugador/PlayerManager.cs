using UnityEngine;
using UnityEngine.InputSystem;

namespace RuneRush.Player
{
    /// <summary>
    /// PlayerController — orquesta la máquina de estados del jugador.
    ///
    /// Responsabilidades:
    ///   - Mantener el estado activo y ejecutar su ciclo (Enter/Update/FixedUpdate/Exit).
    ///   - Capturar input del New Input System y exponerlo a los estados.
    ///   - Recibir eventos de red (desde NetworkEventHandler) y delegarlos al estado activo.
    ///   - Exponer referencias a Rigidbody, VFXController y HUDManager.
    ///
    /// NO contiene lógica de juego — eso vive en cada estado concreto.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class PlayerManager : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────
        [Header("Datos")]
        [SerializeField] private PlayerData _data = new();

        [Header("Referencias")]
        [SerializeField] private VFXController  _vfx;
        [SerializeField] private HUDManager     _hud;
        [SerializeField] private PlayerAnimator _playerAnimator;

        // ── Propiedades públicas para los estados ─────────────────────────────
        public Rigidbody     Rb           { get; private set; }
        public PlayerData    Data         => _data;
        public VFXController VFX          => _vfx;
        public HUDManager    HUD          => _hud;
        public PlayerAnimator Anim        => _playerAnimator;

        // Estado activo actual — PlayerAnimator lo lee en Update
        public PlayerState CurrentState   { get; private set; }

        // Input del joystick de movimiento (leído por los estados)
        public Vector2 MoveInput { get; private set; }

        // Input del joystick de cámara (leído por CameraController)
        public Vector2 LookInput { get; private set; }

        // Yaw actual de la cámara — lo escribe CameraController, lo lee MovingState
        // para que el personaje se mueva relativo al frente de la cámara.
        public float CameraYaw { get; set; } = 0f;

        // ID de red de este jugador — formato "P0", "P1", etc. (asignado por GameManager)
        public string PlayerId { get; set; } = "";

        // ── Estados ───────────────────────────────────────────────────────────
        public IdleState       StateIdle       { get; private set; }
        public MovingState     StateMoving     { get; private set; }
        public CollectingState StateCollecting { get; private set; }
        public BoostedState    StateBoosted    { get; private set; }
        public TeleportingState StateTeleporting { get; private set; }
        public FroggedState    StateFrogged    { get; private set; }
        public LaunchedState   StateLaunched   { get; private set; }
        public FinishedState   StateFinished   { get; private set; }

        

        // ── Input Action (New Input System) ───────────────────────────────────
        private PlayerInputActions _inputActions;

        // ── Unity lifecycle ───────────────────────────────────────────────────
        private void Awake()
        {
            Rb = GetComponent<Rigidbody>();

            // Instanciar e inicializar todos los estados
            StateIdle        = new IdleState();
            StateMoving      = new MovingState();
            StateCollecting  = new CollectingState();
            StateBoosted     = new BoostedState();
            StateTeleporting = new TeleportingState();
            StateFrogged     = new FroggedState();
            StateLaunched    = new LaunchedState();
            StateFinished    = new FinishedState();

            PlayerState[] all = {
                StateIdle, StateMoving, StateCollecting, StateBoosted,
                StateTeleporting, StateFrogged, StateLaunched, StateFinished
            };
            foreach (var s in all) s.Init(this);
        }

        private void OnEnable()
        {
            _inputActions = new PlayerInputActions();
            _inputActions.Player.Move.performed += OnMovePerformed;
            _inputActions.Player.Move.canceled  += OnMoveCanceled;
            _inputActions.Player.Look.performed += OnLookPerformed;
            _inputActions.Player.Look.canceled  += OnLookCanceled;
            _inputActions.Player.ActivatePowerup.performed += OnActivatePowerup;
            _inputActions.Enable();
        }

        private void OnDisable()
        {
            _inputActions.Player.Move.performed -= OnMovePerformed;
            _inputActions.Player.Move.canceled  -= OnMoveCanceled;
            _inputActions.Player.Look.performed -= OnLookPerformed;
            _inputActions.Player.Look.canceled  -= OnLookCanceled;
            _inputActions.Player.ActivatePowerup.performed -= OnActivatePowerup;
            _inputActions.Disable();
        }

        private void Start()
        {
            ChangeState(StateIdle);
        }

        private void Update()
        {
            CurrentState?.Update();
        }

        private void FixedUpdate()
        {
            CurrentState?.FixedUpdate();
        }

        // ── Máquina de estados ────────────────────────────────────────────────

        /// <summary>
        /// Transiciona al nuevo estado: llama Exit() en el actual y Enter() en el nuevo.
        /// Ignora si se intenta transicionar al mismo estado.
        /// </summary>
        public void ChangeState(PlayerState newState)
        {
            if (newState == null || newState == CurrentState) return;

            CurrentState?.Exit();
            CurrentState = newState;
            CurrentState.Enter();
        }

        // ── Eventos de red ────────────────────────────────────────────────────

        /// <summary>
        /// Llamado por NetworkEventHandler cuando llega un mensaje del servidor
        /// dirigido a este jugador. Delega al estado activo.
        /// </summary>
        public void OnNetworkEvent(NetworkEvent evt)
        {
            CurrentState?.OnNetworkEvent(evt);
        }

        // ── API pública para GameManager ──────────────────────────────────────

        /// <summary>
        /// Llamado por GameManager cuando collect_confirm trae objectType "powerup_viento".
        /// Configura la duración del boost y transiciona al estado correspondiente.
        /// </summary>
        /// <summary>
        /// Asigna el HUDManager en runtime.
        /// Llamado por GameManager al spawnear el jugador local,
        /// ya que HUDManager vive en escena y el prefab no puede referenciarlo.
        /// </summary>
        public void SetHUDManager(HUDManager hud)
        {
            _hud = hud;
        }

        public void ApplySpeedBoost(float duration)
        {
            Data.BoostDuration = duration;
            ChangeState(StateBoosted);
        }

        // ── Input callbacks ───────────────────────────────────────────────────
        private void OnMovePerformed(InputAction.CallbackContext ctx)
            => MoveInput = ctx.ReadValue<Vector2>();

        private void OnMoveCanceled(InputAction.CallbackContext ctx)
            => MoveInput = Vector2.zero;

        private void OnLookPerformed(InputAction.CallbackContext ctx)
            => LookInput = ctx.ReadValue<Vector2>();

        private void OnLookCanceled(InputAction.CallbackContext ctx)
            => LookInput = Vector2.zero;

        [Header("Power-up VFX")]
        [SerializeField] private float _windPushDuration  = 1.5f;
        [SerializeField] private float _frogSpellDuration = 1.5f;
        [SerializeField] private float _windPushDistance  = 2f;  // distancia frente al jugador

        private void OnActivatePowerup(InputAction.CallbackContext ctx)
        {
            if (!_powerupReady) return;

            switch (_activePowerupType)
            {
                case "powerup_viento":
                    ApplySpeedBoost(Data.BoostDuration);
                    Anim?.TriggerSpellWind();
                    SetPowerupReady("", false);
                    break;

                case "powerup_impulso":
                    Anim?.TriggerSpellWind();
                    SpawnPowerupVFX(PowerupVFX.VFXType.WindPush, _windPushDuration);
                    SetPowerupReady("", false);
                    break;

                case "powerup_rana":
                    Anim?.TriggerSpellFrog();
                    SpawnPowerupVFX(PowerupVFX.VFXType.FrogSpell, _frogSpellDuration);
                    SetPowerupReady("", false);
                    break;

                case "portal_propio":
                    GameClient.Instance?.SendPowerupActivate("portal_propio");
                    break;
            }
        }

        private void SpawnPowerupVFX(PowerupVFX.VFXType type, float duration)
        {
            if (VFX == null) return;

            // Spawnear frente al jugador a _windPushDistance unidades
            Vector3    spawnPos = Rb.position + transform.forward * _windPushDistance;
            Quaternion spawnRot = transform.rotation;

            if (type == PowerupVFX.VFXType.WindPush)
                VFX.SpawnWindPushVFX(spawnPos, spawnRot, PlayerId, duration);
            else
                VFX.SpawnFrogSpellVFX(spawnPos, spawnRot, PlayerId, duration);
        }

        // ── API power-up ──────────────────────────────────────────────────────
        private bool   _powerupReady      = false;
        private string _activePowerupType = "";

        /// <summary>True si el jugador tiene un power-up cargado sin usar.</summary>
        public bool HasPowerup => _powerupReady;

        public void SetPowerupReady(string powerupType, bool ready)
        {
            _powerupReady      = ready;
            _activePowerupType = ready ? powerupType : "";
            HUD?.SetPowerupReady(ready ? powerupType : "");
        }
    }
}