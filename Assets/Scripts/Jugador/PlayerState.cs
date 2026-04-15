using UnityEngine;

namespace RuneRush.Player
{
    public abstract class PlayerState
    {
        protected PlayerManager Controller { get; private set; }
        protected Rigidbody        Rb         => Controller.Rb;
        protected PlayerData       Data       => Controller.Data;

        // Capa Ground — coincide con LayerMask 1 << 6
        private static readonly int GroundLayer = 1 << 6;

        /// <summary>
        /// True si el jugador tiene el suelo cerca debajo.
        /// Raycast corto desde el centro del Rigidbody hacia abajo.
        /// </summary>
        protected bool IsGrounded =>
            Physics.Raycast(Rb.position, Vector3.down, 0.9f, GroundLayer);

        /// <summary>
        /// Devuelve la velocidad Y segura: si el jugador está en el suelo
        /// y hay un impulso positivo (depenetración de escalón), lo zeroeamos.
        /// Esto evita que el personaje "vuele" al subir bordes.
        /// </summary>
        protected float SafeYVelocity
        {
            get
            {
                float y = Rb.linearVelocity.y;
                if (y > 0.5f && IsGrounded) return 0f;
                return y;
            }
        }

        public void Init(PlayerManager controller) => Controller = controller;

        public abstract void Enter();
        public abstract void Update();
        public virtual  void FixedUpdate() { }
        public abstract void Exit();
        public virtual  void OnNetworkEvent(NetworkEvent evt) { }
    }
}
