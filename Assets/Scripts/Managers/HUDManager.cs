using UnityEngine;
using TMPro;

namespace RuneRush.Player
{
    // ══════════════════════════════════════════════════════════════════════════
    // HUDManager — actualiza la interfaz del jugador local.
    // Los estados llaman métodos semánticos; aquí conectas tus elementos de UI.
    // ══════════════════════════════════════════════════════════════════════════
    public class HUDManager : MonoBehaviour
    {
        [Header("Puntaje y tiempo")]
        [SerializeField] private TMP_Text _scoreLabel;
        [SerializeField] private TMP_Text _timerLabel;

        [Header("Botón de power-up")]
        [SerializeField] private UnityEngine.UI.Button _powerupButton;
        [SerializeField] private UnityEngine.UI.Image  _powerupButtonIcon;

        [Header("Efectos activos")]
        [SerializeField] private GameObject _frogEffectIndicator;
        [SerializeField] private GameObject _launchEffectIndicator;

        private int _score = 0;

        // ── Puntaje ───────────────────────────────────────────────────────────
        public void OnRuneCollected(int points = 1)
        {
            _score += points;
            if (_scoreLabel) _scoreLabel.text = _score.ToString();
        }

        public void SetScore(int score)
        {
            _score = score;
            if (_scoreLabel) _scoreLabel.text = _score.ToString();
        }

        // ── Temporizador (actualizado por el servidor vía state_update) ───────
        public void SetTimer(float seconds)
        {
            if (_timerLabel)
                _timerLabel.text = $"{Mathf.CeilToInt(seconds)}";
        }

        // ── Botón de power-up ─────────────────────────────────────────────────

        /// <summary>
        /// Activa o desactiva el botón de power-up.
        /// Lo llama PlayerController.SetPowerupReady().
        /// </summary>
        public void SetPowerupButtonInteractable(bool active)
        {
            if (_powerupButton) _powerupButton.interactable = active;
        }

        /// <summary>
        /// Cambia el ícono del botón según el power-up recogido.
        /// Pasar null para limpiar el ícono al consumirse.
        /// </summary>
        public void SetPowerupIcon(Sprite icon)
        {
            if (_powerupButtonIcon) _powerupButtonIcon.sprite = icon;
        }

        // ── Indicadores de efecto activo ──────────────────────────────────────
        public void ShowEffectIcon(string effectId)
        {
            switch (effectId)
            {
                case "frog":     if (_frogEffectIndicator)   _frogEffectIndicator.SetActive(true);   break;
                case "launched": if (_launchEffectIndicator) _launchEffectIndicator.SetActive(true); break;
            }
        }

        public void HideEffectIcon(string effectId)
        {
            switch (effectId)
            {
                case "frog":     if (_frogEffectIndicator)   _frogEffectIndicator.SetActive(false);   break;
                case "launched": if (_launchEffectIndicator) _launchEffectIndicator.SetActive(false); break;
            }
        }
    }
}
