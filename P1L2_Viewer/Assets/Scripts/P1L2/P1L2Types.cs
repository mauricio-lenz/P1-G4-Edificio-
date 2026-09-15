using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace P1L2.Viewer
{
    // ---- modelado del contrato P1L2/model_data/1.0 -------------------------

    [Serializable]
    public class NodeJson
    {
        public int tag;
        public float x, y, z;
        public string nivel;
    }

    [Serializable]
    public class ElementoJson
    {
        public int tag;
        public int i, j;
        public string kind;
        public string nivel;
        public string seccion;
        public List<float> local_x;
    }

    [Serializable]
    public class SlabJson
    {
        public string id, nivel;
        public float e, qG, qQ;
        public List<PointJson> polygon;
    }

    [Serializable]
    public class PointJson
    {
        public float x, y;
    }

    [Serializable]
    public class VoladizoJson
    {
        public string id, nivel;
        public float e, x_min, x_max, y_min, y_max;
    }

    [Serializable]
    public class ApoyoJson
    {
        public int node;
        public List<int> fixity;
    }

    [Serializable]
    public class DiafragmaJson
    {
        public string id, nivel;
        public int master;
        public List<int> nodes;
    }

    [Serializable]
    public class TributaryAreaJson
    {
        public string id, slab, nivel, @case;
        public int element;
        public float area;
    }

    [Serializable]
    public class NivelJson
    {
        public string id;
        public float elevation;
        public float? qG, qQ;
    }

    [Serializable]
    public class EdificioJson
    {
        public string id, nombre, proyecto, material;
        public List<NivelJson> niveles;
        public List<string> sections;
        public List<NodeJson> nodos;
        public List<ElementoJson> elementos;
        public List<SlabJson> losas;
        public List<VoladizoJson> voladizos;
        public List<ApoyoJson> apoyos;
        public List<DiafragmaJson> diafragmas;
        public List<TributaryAreaJson> tributary_areas;
    }

    [Serializable]
    public class ModelDataP1L2
    {
        public string schema_version;
        public List<EdificioJson> edificios;
    }

    /// <summary>Lee el JSON P1L2/model_data/1.0 desde un TextAsset (JsonUtility).</summary>
    public static class P1L2Loader
    {
        public static ModelDataP1L2 Load(TextAsset asset)
        {
            return JsonUtility.FromJson<ModelDataP1L2>(asset.text);
        }

        public static Dictionary<int, NodeJson> NodeIndex(EdificioJson ed)
        {
            var idx = new Dictionary<int, NodeJson>();
            foreach (var n in ed.nodos)
            {
                if (n == null || idx.ContainsKey(n.tag))
                    throw new Exception($"P1L2 nodo tag repetido: {n?.tag}");
                idx.Add(n.tag, n);
            }
            return idx;
        }
    }

    /// <summary>Metadatos por barra (tag, tipo, nivel, sección, ejes, muro).</summary>
    public class ElementoInfo : MonoBehaviour
    {
        public int elementTag;
        public string Kind;
        public string Nivel;
        public string Seccion;
        public int NodeI;
        public int NodeJ;
        public Vector3 EjeL1;

        public string Resumen()
        {
            return $"{Kind} tag={elementTag} [{NodeI}|{NodeJ}] nivel={Nivel} " +
                   $"seccion={Seccion} L1={EjeL1.ToString("F2")}";
        }
    }

    /// <summary>Cámara orbitable: clic derecho rota, rueda zoom, clic central/medio pan.</summary>
    public class P1L2Camera : MonoBehaviour
    {
        public float distance = 80f;
        public float speed = 0.4f;
        public float moveSpeed = 30f;

        private Vector3 target = Vector3.zero;
        private float pitch = 35f;
        private float yaw = 45f;

        private void Update()
        {
            float boost = Input.GetKey(KeyCode.LeftShift) ? 3f : 1f;

            // movimiento libre WASD/flechas, Q/E subir-bajar
            Vector3 move = Vector3.zero;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))    move += transform.forward;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))  move -= transform.forward;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))  move -= transform.right;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) move += transform.right;
            if (Input.GetKey(KeyCode.E)) move += Vector3.up;
            if (Input.GetKey(KeyCode.Q)) move -= Vector3.up;

            bool moved = false;
            if (move.sqrMagnitude > 1e-5f)
            {
                Vector3 delta = move.normalized * moveSpeed * boost * Time.deltaTime;
                transform.position += delta;
                target += delta;
                moved = true;
            }

            if (!moved)
            {
                if (Input.GetMouseButton(1))
                {
                    yaw += Input.GetAxis("Mouse X") * speed * 4f;
                    pitch -= Input.GetAxis("Mouse Y") * speed * 4f;
                    pitch = Mathf.Clamp(pitch, 5f, 89f);
                }
                if (Input.GetMouseButton(2))
                {
                    target += transform.right * (-Input.GetAxis("Mouse X") * speed);
                    target += transform.up * (-Input.GetAxis("Mouse Y") * speed);
                }
                distance *= 1f - Input.GetAxis("Mouse ScrollWheel") * 1.2f;
                distance = Mathf.Clamp(distance, 8f, 300f);

                Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
                transform.position = target + rot * (Vector3.back * distance);
                transform.LookAt(target);
            }

            if (Input.GetKeyDown(KeyCode.F)) LookAtAllElements();
        }

        public void LookAt(Vector3 center, float radius)
        {
            target = center;
            distance = Mathf.Clamp(radius * 1.6f, 8f, 300f);
            pitch = 35f;
            yaw = 45f;
        }

        /// <summary>Encuadra todos los elementos creados bajo el raíz del viewer.</summary>
        public void LookAtAllElements()
        {
            var builder = GetComponent<P1L2ModelBuilder>();
            if (builder == null || builder.ElementObjects.Count == 0) return;
            Bounds b = builder.ElementObjects[0].GetComponent<Renderer>().bounds;
            foreach (var go in builder.ElementObjects)
            {
                var r = go.GetComponent<Renderer>();
                if (r != null) b.Encapsulate(r.bounds);
            }
            LookAt(b.center, b.extents.magnitude);
        }
    }
}