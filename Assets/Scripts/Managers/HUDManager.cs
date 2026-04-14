using UnityEngine;
using TMPro;

namespace RuneRush.Player
{
    // ══════════════════════════════════════════════════════════════════════════
    // HUDManager
    // ══════════════════════════════════════════════════════════════════════════
    public class HUDManager : MonoBehaviour
    {
        // ── Puntajes ──────────────────────────────────────────────────────────
        [Header("Puntaje propio")]
        [SerializeField] private TMP_Text _myScoreLabel;
 
        [Header("Puntajes de rivales (orden de aparición en pantalla)")]
        [SerializeField] private TMP_Text[] _rivalScoreLabels = new TMP_Text[3];
 
        // ── Tiempo ────────────────────────────────────────────────────────────
        [Header("Tiempo")]
        [SerializeField] private TMP_Text _timerLabel;
 
        // ── Indicadores de efecto activo ──────────────────────────────────────
        [Header("Indicadores de efecto")]
        [SerializeField] private GameObject _boostIndicator;
        [SerializeField] private GameObject _frogIndicator;
        [SerializeField] private GameObject _launchIndicator;
 
        // ── Botón de power-up ─────────────────────────────────────────────────
        [Header("Botón de power-up")]
        [SerializeField] private GameObject            _powerupButtonObject; // el GameObject completo del botón
        [SerializeField] private UnityEngine.UI.Image  _powerupButtonIcon;
 
        [Header("Sprites del botón (en orden: frog, launch, boost, portal)")]
        [SerializeField] private Sprite _spriteFrog;
        [SerializeField] private Sprite _spriteLaunch;
        [SerializeField] private Sprite _spriteBoost;
        [SerializeField] private Sprite _spritePortal;
 
        // ── Panel de resultados ───────────────────────────────────────────────
        [Header("Resultados")]
        [SerializeField] private GameObject _resultsPanel;
        [SerializeField] private TMP_Text   _resultsLabel;
 
        // ── Estado interno ────────────────────────────────────────────────────
        // Mapa playerId → índice de label de rival (se llena al inicio de partida)
        private readonly System.Collections.Generic.Dictionary<string, int> _rivalIndex = new();
        private string _localId = "";
 
        private void Start()
        {
            _localId = GameClient.Instance ? GameClient.Instance.PlayerId : "";
        }
 
        // ── Registro de jugadores ─────────────────────────────────────────────
 
        /// <summary>
        /// Llamar desde GameManager al inicio de partida para cada jugador remoto,
        /// en el mismo orden en que aparecen en match_start.
        /// Así cada label de rival queda asociado a un playerId.
        /// </summary>
        public void RegisterRival(string playerId, string displayName)
        {
            if (playerId == _localId) return;
            int idx = _rivalIndex.Count;
            if (idx >= _rivalScoreLabels.Length) return;
 
            _rivalIndex[playerId] = idx;
            if (_rivalScoreLabels[idx])
                _rivalScoreLabels[idx].text = $"{displayName}: 0";
        }
 
        // ── Puntajes ──────────────────────────────────────────────────────────
 
        public void SetMyScore(int score)
        {
            if (_myScoreLabel) _myScoreLabel.text = $"Runas: {score}";
        }
 
        /// <summary>
        /// Actualiza el puntaje de cualquier jugador.
        /// Si es el local actualiza su label, si es rival actualiza el label correspondiente.
        /// </summary>
        public void SetScore(string playerId, int score)
        {
            if (playerId == _localId)
            {
                SetMyScore(score);
                return;
            }
 
            if (_rivalIndex.TryGetValue(playerId, out int idx)
                && idx < _rivalScoreLabels.Length
                && _rivalScoreLabels[idx])
            {
                _rivalScoreLabels[idx].text = $"P{idx + 1}: {score}";
            }
        }
 
        // ── Tiempo ────────────────────────────────────────────────────────────
        public void SetTimer(float seconds)
        {
            if (_timerLabel) _timerLabel.text = $"{Mathf.CeilToInt(seconds):00}s";
        }
 
        // ── Indicadores de efecto ─────────────────────────────────────────────
        public void ShowEffectIcon(string effectId)
        {
            switch (effectId)
            {
                case "boost":    if (_boostIndicator)  _boostIndicator.SetActive(true);  break;
                case "frog":     if (_frogIndicator)   _frogIndicator.SetActive(true);   break;
                case "launched": if (_launchIndicator) _launchIndicator.SetActive(true); break;
            }
        }
 
        public void HideEffectIcon(string effectId)
        {
            switch (effectId)
            {
                case "boost":    if (_boostIndicator)  _boostIndicator.SetActive(false);  break;
                case "frog":     if (_frogIndicator)   _frogIndicator.SetActive(false);   break;
                case "launched": if (_launchIndicator) _launchIndicator.SetActive(false); break;
            }
        }
 
        // ── Botón de power-up ─────────────────────────────────────────────────
 
        /// <summary>
        /// Activa el botón y cambia su ícono según el tipo de power-up disponible.
        /// Pasar powerupType = "" para desactivarlo.
        /// </summary>
        public void SetPowerupReady(string powerupType)
        {
            bool has = !string.IsNullOrEmpty(powerupType);
 
            if (_powerupButtonObject) _powerupButtonObject.SetActive(has);
 
            if (!_powerupButtonIcon) return;
 
            _powerupButtonIcon.sprite = powerupType switch
            {
                "powerup_rana"    => _spriteFrog,
                "powerup_impulso" => _spriteLaunch,
                "powerup_viento"  => _spriteBoost,
                "portal_propio"   => _spritePortal,
                _                 => null
            };
        }
 
        // ── Resultados ────────────────────────────────────────────────────────
        public void ShowResults(string content)
        {
            if (_resultsPanel) _resultsPanel.SetActive(true);
            if (_resultsLabel) _resultsLabel.text = content;
        }
 
        public void HideResults()
        {
            if (_resultsPanel) _resultsPanel.SetActive(false);
        }
    }
}