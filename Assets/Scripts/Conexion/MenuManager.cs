using UnityEngine;

public class MenuManager : MonoBehaviour
{
    [Header("Paneles de UI")]
    public GameObject viewMainMenu;
    public GameObject viewHost;
    public GameObject viewClient;

    [Header("Scripts de Red (Para el botón Atrás)")]
    public GameClient gameClient;
    public GameServer gameServer;

    private void Start()
    {
        // Al iniciar, solo mostramos el menú principal
        ShowMainMenu();
    }

    public void ShowMainMenu()
    {
        viewMainMenu.SetActive(true);
        viewHost.SetActive(false);
        viewClient.SetActive(false);
    }

    public void ShowHostMenu()
    {
        viewMainMenu.SetActive(false);
        viewHost.SetActive(true);
        viewClient.SetActive(false);
    }

    public void ShowClientMenu()
    {
        viewMainMenu.SetActive(false);
        viewHost.SetActive(false);
        viewClient.SetActive(true);
    }

    // --- NUEVA FUNCIÓN PARA EL BOTÓN ATRÁS ---
    public void ActionGoBack()
    {
        // Si el usuario vuelve atrás, nos aseguramos de cerrar cualquier conexión
        // para que no queden clientes conectados en el limbo o puertos ocupados.
        if (gameClient != null && gameClient.Connected)
        {
            gameClient.OnDisconnect();
        }

        if (gameServer != null)
        {
            gameServer.OnCloseRoom();
        }

        ShowMainMenu();
    }
}