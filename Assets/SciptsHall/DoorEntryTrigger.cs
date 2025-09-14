using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting.Antlr3.Runtime;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.ProBuilder.Shapes;

public class DoorEntryTrigger : MonoBehaviour
{
    public UnityEvent onPlayerEnter;
    public static event System.Action onPlayerEnterStatic;
    public  EasyDoorSystem.EasyDoor door;
    private bool entro = false;
    private bool yaEmpezo = false;

    void Update()
    {
        if (yaEmpezo)
        {
            return;
        }
        if (entro && !door.IsOpen)
        {
            onPlayerEnter?.Invoke();
            onPlayerEnterStatic.Invoke();
            // Esperar 1 segundo antes de cerrar la puerta
            StartCoroutine(CloseDoorWithDelay());
            yaEmpezo = true;
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
            entro=true;
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
