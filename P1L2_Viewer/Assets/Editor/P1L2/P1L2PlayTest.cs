using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace P1L2.Viewer
{
    // Verificacion de runtime: abre MainP1L2.unity, entra en Play, encuadra el
    // modelo, guarda una captura (Captura_P1L2.png en la raiz del proyecto) y
    // registra el bounding box global. En batchmode no sale solo; el wrapper
    // externo lo detiene por timeout.
    public static class P1L2PlayTest
    {
        static double _t;
        static bool _enteredPlay, _shot;

        [MenuItem("P1L2/Play Test")]
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/MainP1L2.unity");
            _t = EditorApplication.timeSinceStartup;
            _enteredPlay = false;
            _shot = false;
            EditorApplication.update += Loop;
            EditorApplication.isPlaying = true;
        }

        static void Loop()
        {
            double t = EditorApplication.timeSinceStartup - _t;
            if (!EditorApplication.isPlaying && t > 1.5)
                EditorApplication.isPlaying = true;
            if (EditorApplication.isPlaying) _enteredPlay = true;

            if (!_shot && t > 4.0)
                _shot = TakeShot();

            if (t > 12.0)
            {
                EditorApplication.update -= Loop;
                Debug.Log("[P1L2PlayTest] fin play=" + _enteredPlay +
                          " t=" + t.ToString("0.0") + " shot=" + _shot);
                EditorApplication.isPlaying = false;
                EditorApplication.Exit(0);
            }
        }

        static bool TakeShot()
        {
            var mb = Object.FindObjectOfType<P1L2ModelBuilder>();
            if (mb == null || mb.ElementObjects.Count == 0) return false;

            Bounds b = default;
            bool first = true;
            foreach (var go in mb.ElementObjects)
            {
                var r = go.GetComponent<Renderer>();
                if (r == null) continue;
                if (first) { b = r.bounds; first = false; }
                else b.Encapsulate(r.bounds);
            }
            float maxDim = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            var cam = Object.FindObjectOfType<Camera>();
            if (cam != null)
            {
                Vector3 from = b.center + new Vector3(maxDim * 0.85f, maxDim * 0.55f, maxDim * 0.85f);
                cam.transform.SetPositionAndRotation(from,
                    Quaternion.LookRotation(b.center - from, Vector3.up));
                ScreenCapture.CaptureScreenshot(
                    Path.Combine(Application.dataPath, "..", "Captura_P1L2.png"), 2);
            }
            Debug.Log($"[P1L2PlayTest] bounds center={b.center} size={b.size} " +
                      $"maxDim={maxDim:F1} elementos={mb.ElementObjects.Count}");
            return true;
        }
    }
}