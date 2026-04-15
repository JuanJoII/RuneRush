using UnityEngine;

namespace RuneRush.Player
{
    public class RemotePlayerSync : MonoBehaviour
    {
        public string PlayerId { get; set; } = "";

        [SerializeField] private float     _lerpSpeed    = 12f;
        [SerializeField] private float     _groundOffset = 0f;
        [SerializeField] private LayerMask _groundMask   = 1 << 6;

        private Rigidbody           _rb;
        private PlayerAnimator      _playerAnim;
        private PlayerVFXController _vfx;
        private Vector3             _targetPosition;
        private bool                _hasTarget  = false;
        private bool                _wasBoosted = false;
        private bool                _wasFrogged = false;

        private void Awake()
        {
            _rb             = GetComponent<Rigidbody>();
            _playerAnim     = GetComponentInChildren<PlayerAnimator>(includeInactive: true);
            _vfx            = GetComponentInChildren<PlayerVFXController>(includeInactive: true);
            _targetPosition = transform.position;
        }

        private void OnEnable()
        {
            if (GameClient.Instance == null) return;
            GameClient.Instance.OnPowerupConfirm.AddListener(OnPowerupConfirm);
            GameClient.Instance.OnCollectConfirm.AddListener(OnCollectConfirm);
        }

        private void OnDisable()
        {
            if (GameClient.Instance == null) return;
            GameClient.Instance.OnPowerupConfirm.RemoveListener(OnPowerupConfirm);
            GameClient.Instance.OnCollectConfirm.RemoveListener(OnCollectConfirm);
        }

        private void FixedUpdate()
        {
            if (!_hasTarget) return;

            Vector3 next = Vector3.Lerp(
                _rb ? _rb.position : transform.position,
                _targetPosition,
                _lerpSpeed * Time.fixedDeltaTime
            );

            if (_rb) _rb.MovePosition(next);
            else     transform.position = next;

            Vector3 delta = _targetPosition - (_rb ? _rb.position : transform.position);
            delta.y = 0f;
            if (delta.sqrMagnitude > 0.01f)
            {
                Quaternion targetRot = Quaternion.LookRotation(delta);
                if (_rb)
                    _rb.MoveRotation(Quaternion.Slerp(_rb.rotation, targetRot, 10f * Time.fixedDeltaTime));
                else
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 10f * Time.fixedDeltaTime);
            }
        }

        // ── Movimiento y estado visual ────────────────────────────────────────

        public void SetTargetFromMove(Vector3 position, string animState)
        {
            _targetPosition = ResolveY(position);
            _hasTarget      = true;

            // Animaciones de hechizo — no afectan locomotión
            if (animState == "casting_wind")
            {
                _playerAnim?.TriggerSpellWind();
                return;
            }
            if (animState == "casting_frog")
            {
                _playerAnim?.TriggerSpellFrog();
                return;
            }

            // Locomotión normal
            float speed = animState switch
            {
                "moviendose" => 1f,
                "boosted"    => 2f,
                "frogged"    => 0.5f,
                _            => 0f
            };
            _playerAnim?.SetSpeedManual(speed);

            // Trail de boost
            bool isBoosted = animState == "boosted";
            if (isBoosted != _wasBoosted)
            {
                _wasBoosted = isBoosted;
                _vfx?.SetBoostTrail(isBoosted);
            }

            // Modelo de rana
            bool isFrogged = animState == "frogged";
            if (isFrogged != _wasFrogged)
            {
                _wasFrogged = isFrogged;
                if (isFrogged) _vfx?.PlayEffect("frogged");
                else           _vfx?.StopEffect("frogged");
            }
        }

        /// <summary>Teletransporte instantáneo — sin lerp.</summary>
        public void SetTarget(Vector3 position)
        {
            _targetPosition = ResolveY(position);
            _hasTarget      = true;

            if (_rb) _rb.MovePosition(_targetPosition);
            else     transform.position = _targetPosition;
        }

        // ── Efectos de power-up ───────────────────────────────────────────────

        public void ApplyFroggedVisual()
        {
            if (_wasFrogged) return;
            _wasFrogged = true;
            _vfx?.PlayEffect("frogged");
            Invoke(nameof(RevertFroggedVisual), 3f);
        }

        private void RevertFroggedVisual()
        {
            _wasFrogged = false;
            _vfx?.StopEffect("frogged");
        }

        public void ApplyLaunchedVisual(Vector3 attackerPos)
        {
            Vector3 dir = (transform.position - attackerPos).normalized;
            dir.y = 0.3f;
            _targetPosition += dir.normalized * 5f;
        }

        // ── Resolución de Y ───────────────────────────────────────────────────

        private Vector3 ResolveY(Vector3 position)
        {
            if (TryRaycast(position.x, position.z, out float y))
                return new Vector3(position.x, y + _groundOffset, position.z);

            float[] dists  = { 1f, 2f, 3f, 4f, 5f };
            Vector2[] dirs = {
                Vector2.right, Vector2.left, Vector2.up, Vector2.down,
                new Vector2(1,1).normalized,  new Vector2(-1,1).normalized,
                new Vector2(1,-1).normalized, new Vector2(-1,-1).normalized
            };
            foreach (float d in dists)
                foreach (Vector2 dir in dirs)
                    if (TryRaycast(position.x + dir.x * d, position.z + dir.y * d, out y))
                        return new Vector3(position.x, y + _groundOffset, position.z);

            return position;
        }

        private bool TryRaycast(float x, float z, out float groundY)
        {
            if (Physics.Raycast(new Vector3(x, 200f, z), Vector3.down,
                                out RaycastHit hit, 400f, _groundMask))
            {
                groundY = hit.point.y;
                return true;
            }
            groundY = 0f;
            return false;
        }

        // ── Eventos de broadcast ──────────────────────────────────────────────

        private void OnPowerupConfirm(string json)
        {
            string pid         = GameServer.ExtractString(json, "playerId");
            string powerupType = GameServer.ExtractString(json, "powerupType");
            if (pid != PlayerId) return;

            if (powerupType == "portal_propio")
            {
                float dx = GameServer.ExtractFloatInObject(json, "destinationPosition", "x");
                float dz = GameServer.ExtractFloatInObject(json, "destinationPosition", "z");
                SetTarget(new Vector3(dx, 1f, dz));
            }
        }

        private void OnCollectConfirm(string json)
        {
            string pid        = GameServer.ExtractString(json, "playerId");
            string objectType = GameServer.ExtractString(json, "objectType");
            if (pid != PlayerId) return;

            if (objectType == "powerup_viento")
                _playerAnim?.TriggerSpellWind();
        }
    }
}