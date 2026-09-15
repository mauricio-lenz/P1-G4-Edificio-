using UnityEngine;

// ============================================================================
//  Camara orbitante para la demo: boton derecho gira, rueda zoom,
//  boton central desplaza el objetivo.
// ============================================================================
namespace EdificioUnity
{
    public class CameraOrbit : MonoBehaviour
    {
        public Vector3 target = new Vector3(28f, 16f, 45f);
        [Range(5f, 150f)] public float distancia = 60f;
        public float yaw = -35f, pitch = 35f;

        void LateUpdate()
        {
            if (Input.GetMouseButton(1))
            {
                yaw += Input.GetAxis("Mouse X") * 2.2f;
                pitch -= Input.GetAxis("Mouse Y") * 2.2f;
                pitch = Mathf.Clamp(pitch, -85f, 85f);
            }
            if (Input.GetMouseButton(2))
            {
                target += -transform.right * Input.GetAxis("Mouse X") * 0.4f;
                target += transform.up * Input.GetAxis("Mouse Y") * -0.4f;
            }
            distancia = Mathf.Clamp(
                distancia - Input.GetAxis("Mouse ScrollWheel") * 12f, 5f, 150f);

            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            transform.position = target - rot * Vector3.forward * distancia;
            transform.rotation = rot;
        }
    }
}