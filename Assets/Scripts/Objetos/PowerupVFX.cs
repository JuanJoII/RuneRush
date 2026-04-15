using UnityEngine;

namespace RuneRush.Player
{
    /// <summary>
    /// PowerupVFX — objeto temporal spawneado frente al jugador cuando activa
    /// un power-up de área (viento o rana). Tiene un collider trigger que detecta
    /// a qué jugadores golpea durante su vida útil.
    ///
    /// Flujo:
    ///   1. PlayerController spawnea este prefab frente al jugador.
    ///   2. OnTriggerEnter detecta jugadores remotos dentro del área.
    ///   3. Notifica al GameManager qué jugador fue golpeado y con qué efecto.
    ///   4. GameManager informa al servidor, que hace broadcast del efecto.
    ///   5. Al acabar la duración, el objeto se destruye solo.
    ///
    /// Configuración del prefab:
    ///   - Collider (Sphere o Box) en modo Trigger
    ///   - ParticleSystem o mesh visual del efecto
    ///   - Este script en el raíz del prefab
    /// </summary>
    public class PowerupVFX : MonoBehaviour
    {
        public enum VFXType { WindPush, FrogSpell }

        [HideInInspector] public VFXType    Type;
        [HideInInspector] public string     AttackerId; // PlayerId del jugador que lo lanzó
        [HideInInspector] public float      Duration = 3f;

        private float _timer = 0f;

        // Para evitar golpear al mismo jugador dos veces en la misma activación
        private System.Collections.Generic.HashSet<string> _hit = new();

        private void Update()
        {
            _timer += Time.deltaTime;
            if (_timer >= Duration)
                Destroy(gameObject);
        }

        private void OnTriggerEnter(Collider other)
        {
            var pm = other.GetComponent<PlayerManager>();
            if (pm == null) return;

            // No golpear al propio jugador que lanzó el hechizo
            if (pm.PlayerId == AttackerId) return;

            // No golpear dos veces al mismo jugador
            if (_hit.Contains(pm.PlayerId)) return;
            _hit.Add(pm.PlayerId);

            // Notificar al GameManager con el ID del jugador golpeado
            GameManager.Instance?.OnPowerupVFXHit(Type, AttackerId, pm.PlayerId);
        }
    }
}