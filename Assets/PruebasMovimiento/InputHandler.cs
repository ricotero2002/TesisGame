using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public interface IInputProvider
{
    float GetMoveHorizontal();
    float GetMoveVertical();
    float GetLookX();
    float GetLookY();
}

public class MobileInputProvider : IInputProvider
{

    public FixedJoystick movement;
    public FixedJoystick rotation;
    public MobileInputProvider(FixedJoystick movement, FixedJoystick rotation)
    {
        this.movement = movement;
        this.rotation = rotation;
    }
    public float GetMoveHorizontal() => movement.Horizontal;
    public float GetMoveVertical() => movement.Vertical;
    public float GetLookX() => rotation.Horizontal;
    public float GetLookY() => rotation.Vertical;
}
public class PCInputProvider : IInputProvider
{
    public float GetMoveHorizontal() => Input.GetAxis("Horizontal");
    public float GetMoveVertical() => Input.GetAxis("Vertical");
    public float GetLookX() => Input.GetAxis("Mouse X");
    public float GetLookY() => Input.GetAxis("Mouse Y");
}

public static class InputHandler
{
    private static IInputProvider _provider;
    private static GameObject joystickUI = null;
    private static bool firstTime = true;
    private static bool working = false;

    public static void SetEnabled(bool b)
    {
        working = b;
    }

    public static void Init(GameObject joystickUI)
    {
        if (firstTime) {
#if UNITY_ANDROID || UNITY_IOS
                InputHandler.joystickUI = Object.Instantiate(joystickUI);
                Transform moveT = InputHandler.joystickUI.transform.Find("Movement");
                Transform lookT = InputHandler.joystickUI.transform.Find("Rotation");
                FixedJoystick moveJs = moveT.GetComponent<FixedJoystick>();
                FixedJoystick lookJs = lookT.GetComponent<FixedJoystick>();
                _provider = new MobileInputProvider(moveJs, lookJs);
#else
            _provider = new PCInputProvider();
#endif
            firstTime = false;
        }
        else
        {
#if UNITY_ANDROID || UNITY_IOS
            InputHandler.joystickUI.SetActive(true);
#endif
            
        }
        working = true;
    }

    public static void desabilitar()
    {
#if UNITY_ANDROID || UNITY_IOS
        if(InputHandler.joystickUI != null)
            InputHandler.joystickUI.SetActive(false);
#endif
        working = false;
    }

    public static float MoveH() => working ? _provider.GetMoveHorizontal() : 0f;
    public static float MoveV() => working ? _provider.GetMoveVertical(): 0f;
    public static float LookX() => working ? _provider.GetLookX(): 0f;
    public static float LookY() => working ? _provider.GetLookY(): 0f;
}