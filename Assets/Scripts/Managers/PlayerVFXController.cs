using UnityEngine;

namespace RuneRush.Player
{
    public class PlayerVFXController : MonoBehaviour
    {
        [Header("Referencias del jugador")]
        [SerializeField] private GameObject _normalModel;
        [SerializeField] private GameObject _frogModel;
        [SerializeField] private GameObject _boostTrail;

        private TeleportingState _teleportingState;
        private PlayerAnimator   _playerAnimator;

        private void Awake()
        {
            _playerAnimator = GetComponent<PlayerAnimator>();
        }

        public void Init(TeleportingState teleportingState)
        {
            _teleportingState = teleportingState;
        }

        // ── Efectos del jugador ───────────────────────────────────────

        public void PlayEffect(string vfxId)
        {
            switch (vfxId)
            {
                case "boost":
                    if (_boostTrail) _boostTrail.SetActive(true);
                    break;

                case "frogged":
                    SetFrogModel(true);
                    break;

                case "teleport_out":
                    Invoke(nameof(NotifyTeleportArrival), 0.4f);
                    break;
            }
        }

        public void StopEffect(string vfxId)
        {
            switch (vfxId)
            {
                case "boost":
                    if (_boostTrail) _boostTrail.SetActive(false);
                    break;

                case "frogged":
                    SetFrogModel(false);
                    break;
            }
        }

        public void StopAllEffects()
        {
            if (_boostTrail) _boostTrail.SetActive(false);
            SetFrogModel(false);
        }

        public void SetBoostTrail(bool active)
        {
            if (_boostTrail == null) return;
            _boostTrail.SetActive(active);
        }

        private void SetFrogModel(bool active)
        {
            if (_normalModel) _normalModel.SetActive(!active);
            if (_frogModel)   _frogModel.SetActive(active);
            _playerAnimator?.SwitchToFrog(active);
        }

        private void NotifyTeleportArrival()
        {
            _teleportingState?.OnArrival();
        }
    }
}