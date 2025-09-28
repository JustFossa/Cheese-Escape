using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using UnityEngine.SceneManagement;

using Unity.Netcode;
public class UIHandler : MonoBehaviour, IPointerClickHandler
{
    public TMPro.TMP_InputField ipInputField;
    public TMPro.TMP_InputField playerNameInputField;

    public void OnPointerClick(PointerEventData eventData)
    {
        Button btn = eventData.pointerPress.GetComponent<Button>();

        if (btn != null)
        {
            switch (btn.name)
            {
                case "Host":
                    SceneManager.LoadScene("HostGameScene");
                    break;
                case "HostGame":
                    StartHost();
                    break;
                case "Join":
                    SceneManager.LoadScene("JoinGameScene");
                    break;
                case "Quit":
                    Application.Quit();
                    break;
                case "Back":
                    SceneManager.LoadScene("MainMenuScene");
                    break;
                case "JoinGame":
                    JoinGame();
                    break;
            }
        }
    }

    void Start()
    {
        if (ipInputField != null)
        {
            ipInputField.onValueChanged.AddListener(delegate { PlayerPrefs.SetString("ServerIP", ipInputField.text); PlayerPrefs.Save(); });

        }
        if (playerNameInputField != null)
        {
            playerNameInputField.onValueChanged.AddListener(delegate { PlayerPrefs.SetString("PlayerName", playerNameInputField.text); PlayerPrefs.Save(); });
        }
    }


    public void StartHost()
    {
        
        NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>().SetConnectionData("127.0.0.1", (ushort)7777, "0.0.0.0");

        NetworkManager.Singleton.StartHost();

        // Start coroutine to load LobbyScene after host starts
        StartCoroutine(LoadLobbyAfterHostStart());
    }

    public void JoinGame()
    {

        
        string ip = PlayerPrefs.GetString("ServerIP", "127.0.0.1");
        NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>().SetConnectionData(ip, (ushort)7777);
        NetworkManager.Singleton.StartClient();
        
        // Start coroutine to load LobbyScene after client connects
        StartCoroutine(LoadLobbyAfterClientConnect());
    }



    private IEnumerator LoadLobbyAfterHostStart()
    {
        // Wait a frame to ensure the host has started
        yield return null;
        
        // Wait until the NetworkManager is listening (host is active)
        while (!NetworkManager.Singleton.IsListening)
        {
            yield return new WaitForSeconds(0.1f);
        }
        
        SceneManager.LoadScene("LobbyScene");
    }

    private IEnumerator LoadLobbyAfterClientConnect()
    {
        // Wait a frame to ensure the client connection attempt has started
        yield return null;
        
        // Wait until the client is connected
        float timeout = 10f; // 10 second timeout
        float elapsed = 0f;
        
        while (!NetworkManager.Singleton.IsConnectedClient && elapsed < timeout)
        {
            yield return new WaitForSeconds(0.1f);
            elapsed += 0.1f;
        }
        
        if (NetworkManager.Singleton.IsConnectedClient)
        {
            SceneManager.LoadScene("LobbyScene");
        }
        else
        {
            // Optionally show an error message to the user here
        }
    }

}
