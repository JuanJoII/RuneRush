using UnityEngine;

namespace RuneRush.Player
{
    /// <summary>
    /// Clase base abstracta para todos los estados del jugador.
    /// Cada estado concreto implementa su propia lógica de entrada,
    /// actualización y salida.
    /// </summary>
    public abstract class PlayerState
    {
        // Referencia al controller central — todos los estados la necesitan
        protected PlayerManager Controller { get; private set; }
        protected Rigidbody           Rb        => Controller.Rb;
        protected PlayerData          Data      => Controller.Data;

        public void Init(PlayerManager controller)
        {
            Controller = controller;
        }

        /// <summary>Llamado una vez al entrar al estado.</summary>
        public abstract void Enter();

        /// <summary>Llamado en Update() mientras el estado está activo.</summary>
        public abstract void Update();

        /// <summary>Llamado en FixedUpdate() mientras el estado está activo.</summary>
        public virtual void FixedUpdate() { }

        /// <summary>Llamado una vez al salir del estado.</summary>
        public abstract void Exit();

        /// <summary>
        /// Punto de entrada para eventos de red que llegan del servidor.
        /// Los estados que necesiten reaccionar a mensajes de red sobreescriben este método.
        /// </summary>
        public virtual void OnNetworkEvent(NetworkEvent evt) { }
    }
}
