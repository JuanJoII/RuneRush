using UnityEngine;

namespace RuneRush.Player
{
    // ══════════════════════════════════════════════════════════════════════════
    // VFXController — dispara y detiene efectos visuales por nombre.
    // Los estados llaman PlayEffect/StopEffect con IDs de string.
    // Aquí conectas tus partículas, animaciones y cambios de modelo.
    // ══════════════════════════════════════════════════════════════════════════
    public class VFXController : MonoBehaviour
    {
        [Header("Partículas")]

        [SerializeField] private ParticleSystem _boostParticles;
        [SerializeField] private ParticleSystem _frogBubbles;
        [SerializeField] private ParticleSystem _launchTrail;
        [SerializeField] private ParticleSystem _teleportOut;
        [SerializeField] private ParticleSystem _teleportIn;

        [Header("Modelos")] [SerializeField] private GameObject _normalModel;
        [SerializeField] private GameObject _frogModel;

        // Referencia al TeleportingState para llamar OnArrival tras el VFX
        private TeleportingState _teleportingState;

        public void Init(TeleportingState teleportingState)
        {
            _teleportingState = teleportingState;
        }

        public void PlayEffect(string vfxId)
        {
            switch (vfxId)
            {
                case "boost":
                    _boostParticles?.Play();
                    break;
                case "frogged":
                    _frogBubbles?.Play();
                    SetFrogModel(true);
                    break;
                case "launched":
                    _launchTrail?.Play();
                    break;
                case "teleport_out":
                    _teleportOut?.Play();
                    // Simular duración del VFX y notificar llegada
                    // En producción esto debería ser un callback de animación
                    Invoke(nameof(NotifyTeleportArrival), 0.4f);
                    break;
                case "teleport_in":
                    _teleportIn?.Play();
                    break;
                case "collect":
                    // Implementar: flash de luz + partículas subiendo al HUD
                    break;
            }
        }

        public void StopEffect(string vfxId)
        {
            switch (vfxId)
            {
                case "boost": _boostParticles?.Stop(); break;
                case "frogged":
                    _frogBubbles?.Stop();
                    SetFrogModel(false);
                    break;
                case "launched": _launchTrail?.Stop(); break;
            }
        }

        public void StopAllEffects()
        {
            _boostParticles?.Stop();
            _frogBubbles?.Stop();
            _launchTrail?.Stop();
            SetFrogModel(false);
        }

        public void PlayCollect() => PlayEffect("collect");

        private void SetFrogModel(bool active)
        {
            if (_normalModel) _normalModel.SetActive(!active);
            if (_frogModel) _frogModel.SetActive(active);
        }

        private void NotifyTeleportArrival()
        {
            _teleportingState?.OnArrival();
        }
    }
}