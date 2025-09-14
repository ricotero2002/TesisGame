using EasyDoorSystem;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class HallEntryTrigger : MonoBehaviour
{
    public static event System.Action onPlayerEnter;
    public EasyDoorSystem.EasyDoor door;
    private bool entro = false;
    private bool jugable = false;

    private void Start()
    {
        var hallDoor = GameObject.Find("HallDoor");

        // 3) Buscar puertas (EasyDoor) dentro del Hall
        var script = hallDoor.GetComponentsInChildren<EasyDoor>(true);
        
        DoorEntryTrigger.onPlayerEnterStatic += Jugable; // Habilitar jugable cuando se crea la sala
        door = script[0];
        

    }

    public void Jugable()
    {
        jugable = true;
    }

    void Update()
    {
        if (!jugable)
        {
            return;
        }
        if (entro && !door.IsOpen)
        {
            Debug.Log("Entrando al Hall");
            var allowAutoOpen = door.GetType().GetField("allowAutoOpen", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (allowAutoOpen != null) allowAutoOpen.SetValue(door, false);
            
            jugable = false;
            // Esperar 1 segundo antes de cerrar la puerta
            StartCoroutine(CloseDoorWithDelay());
            onPlayerEnter?.Invoke();
        }
    }
    private IEnumerator CloseDoorWithDelay()
    {
        yield return new WaitForSeconds(1f);
        door.CloseDoor();
    }
    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            entro = true;
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            entro = false;
        }
    }
}
