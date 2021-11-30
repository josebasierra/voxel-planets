using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class UIController : MonoBehaviour
{
    [SerializeField] GameObject hud;
    [SerializeField] GameObject menu;
    [SerializeField] GameObject player;

    void Start()
    {
        hud.SetActive(true);
        menu.SetActive(false);
        Cursor.lockState = CursorLockMode.Locked;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (hud.activeSelf)
            {
                hud.SetActive(false);
                menu.SetActive(true);
                player.SetActive(false);
                Cursor.lockState = CursorLockMode.None;
            }
            else 
            {
                hud.SetActive(true);
                menu.SetActive(false);
                player.SetActive(true);
                Cursor.lockState = CursorLockMode.Locked;
            }
        }
    }

    public void LoadScene(string sceneName)
    {
        // Wait for planets tasks to finish
        // Memory Leak if we change scene while there are jobs running in the background
        foreach (var planet in VoxelPlanet.voxelPlanets)
        {
            if (planet.IsDoingTasks()) return;
        }

        // TODO: remove static to manage voxel planets...
        VoxelPlanet.voxelPlanets.Clear();

        SceneManager.LoadScene(sceneName);

    }

    public void QuitApplication()
    {
        Application.Quit();
    }
}
