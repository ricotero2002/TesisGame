using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Windows;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Parametros")]
    [SerializeField] private float speed = 5f;
    [SerializeField] private float sensitivityCamera = 150f;
    [SerializeField] private float gravityMultipler = 2.0f;

    [Header("Movimiento")]
    private Vector3 direction;
    private CharacterController controller;
    private Transform cameraTransform; // Asigna la cámara en el inspector

    [Header("Gravedad")]
    private float _gravity = -9.81f;
    private float _velocity;

    [Header("Rotacion")]
    private float rotationX = 0f;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        cameraTransform = GetComponentInChildren<Camera>().transform;

        controller.stepOffset = 0f;
        controller.slopeLimit = 0f;
        controller.skinWidth = 0.02f;  // menos “colchón”
    }

    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        Debug.Log($"Chocaste contra: {hit.collider.gameObject.name}");
    }

    void Update()
    {
        ApplyRotation();   // 1) Rotás primero (opcional pero lógico)
        ApplyMovement();   // 2) Movimiento horizontal + gravedad
    }

    private void ApplyGravity()
    {
        if (controller.isGrounded)
        {
            // “Pega” al suelo
            _velocity = -0.5f;
        }
        else
        {
            _velocity += _gravity * gravityMultipler * Time.deltaTime;
        }
    }
    private void ApplyMovement()
    {
        // Conseguir Input de movimiento
        float h = InputHandler.MoveH();
        float v = InputHandler.MoveV();
        // Dirección basada en la cámara
        Vector3 forward = cameraTransform.forward; forward.y = 0; forward.Normalize();
        Vector3 right = cameraTransform.right; right.y = 0; right.Normalize();
        // Calcular el movimiento relativo a la cámara
        Vector3 horizontal = (forward * v + right * h) * speed * Time.deltaTime;

        // 2) Aplica sólo la gravedad en otro Move
        ApplyGravity();  // ajusta _velocity

        Vector3 vertical = Vector3.up * _velocity * Time.deltaTime;

        // 3) Primero mueves horizontal (sin componente Y)
        controller.Move(horizontal);

        // 4) Luego mueves vertical
        controller.Move(vertical);
    }

    private void ApplyRotation()
    {
        //Conseguir Input de rotación
        float mouseX = InputHandler.LookX() * sensitivityCamera * Time.deltaTime;
        float mouseY = InputHandler.LookY() * sensitivityCamera * Time.deltaTime;
        // Rotación de la cámara
        rotationX -= mouseY;
        rotationX = Mathf.Clamp(rotationX, -90f, 90f); // Limita la inclinación de la cámara
        transform.localRotation = Quaternion.Euler(rotationX, transform.localRotation.eulerAngles.y + mouseX, 0);
    }

}
