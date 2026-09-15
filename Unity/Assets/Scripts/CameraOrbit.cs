using UnityEngine;

// ============================================================================
//  Camara libre con desplazamiento WASD/flechas, vista con raton derecho,
//  Q/E para subir/bajar, rueda para desplazamiento y Shift para velocidad.
// ============================================================================
namespace EdificioUnity
{
    public class CameraOrbit : MonoBehaviour
    {
        public float moveSpeed = 20f;
        public float lookSpeed = 2.5f;
        public float fastMultiplier = 3f;
        float _yaw, _pitch;

        void Start()
        {
            Vector3 target = new Vector3(28f, 16f, 45f);
            Vector3 dir = (target - transform.position).normalized;
            _yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            _pitch = Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg;
        }

        void Update()
        {
            if (Input.GetMouseButton(1))
            {
                _yaw += Input.GetAxis("Mouse X") * lookSpeed;
                _pitch -= Input.GetAxis("Mouse Y") * lookSpeed;
                _pitch = Mathf.Clamp(_pitch, -89f, 89f);
            }

            float speed = moveSpeed * (Input.GetKey(KeyCode.LeftShift) ? fastMultiplier : 1f);
            Vector3 dir = Vector3.zero;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))    dir += transform.forward;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))  dir -= transform.forward;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))  dir -= transform.right;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) dir += transform.right;
            if (Input.GetKey(KeyCode.Q)) dir += Vector3.down;
            if (Input.GetKey(KeyCode.E)) dir += Vector3.up;

            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.001f) dir += transform.forward * scroll * 60f;

            if (dir.sqrMagnitude > 1e-4f)
                transform.position += dir.normalized * speed * Time.deltaTime;

            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }
    }
}
