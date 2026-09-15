using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace P1L2.Viewer
{
    /// <summary>
    /// Genera la escena MainP1L2.unity con Camera, Light y ModelBuilder listo.
    /// Expone RunCI para verificación batch:
    ///   Unity -batchmode -quit -projectPath unity -executeMethod P1L2.Viewer.SceneAutoBuilder.RunCI
    /// Salida esperada en log: "P1L2 CI OK: 206 nodos, 130 col, 270 vigas, 35 muros, losas = 6157.42 m2."
    /// </summary>
    public static class SceneAutoBuilder
    {
        private const string ScenePath = "Assets/Scenes/MainP1L2.unity";
        private const string AssetPath = "Assets/Data/model_data_p1l2.json";

        [MenuItem("P1L2/Build Scene P1L2")]
        public static void BuildP1L2Scene()
        {
            EnsureFolders();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects,
                                                     NewSceneMode.Single);

            Camera cam = Object.FindObjectOfType<Camera>();
            if (cam != null)
            {
                cam.gameObject.name = "Main Camera";
                cam.transform.position = new Vector3(30f, 45f, 95f);
                cam.transform.LookAt(new Vector3(30f, 4f, 45f));
                cam.gameObject.AddComponent<P1L2Camera>();
            }

            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var app = new GameObject("App");
            var mb = app.AddComponent<P1L2ModelBuilder>();
                app.AddComponent<P1L2ClickInspector>();
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(AssetPath);
            if (asset != null)
            {
                SerializedObject so = new SerializedObject(mb);
                so.FindProperty("modelJson").objectReferenceValue = asset;
                so.ApplyModifiedProperties();
            }
            else
            {
                Debug.LogError($"P1L2: {AssetPath} no encontrado. "
                             + "Copia data/model_data_p1l2.json a unity/Assets/Data/.");
            }

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"P1L2: escena guardada en {ScenePath}.");
        }

        [MenuItem("P1L2/Run CI")]
        public static void RunCI()
        {
            BuildP1L2Scene();

            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(AssetPath);
            if (asset == null)
            {
                Debug.LogError("P1L2 CI FAIL: model_data_p1l2.json no encontrado.");
                EditorApplication.Exit(1);
                return;
            }

            var mb = Object.FindObjectOfType<P1L2ModelBuilder>();
            if (mb == null)
            {
                Debug.LogError("P1L2 CI FAIL: no se encontró P1L2ModelBuilder tras Build.");
                EditorApplication.Exit(1);
                return;
            }
            mb.modelJson    = asset;
            mb.buildOnStart = false;
            mb.BuildFromAsset(asset);

            var data = P1L2Loader.Load(asset);
            int nodos  = data.edificios.Sum(e => e.nodos.Count);
            int cols   = data.edificios.Sum(e => e.elementos.Count(el => el.kind == "column"));
            int vigs   = data.edificios.Sum(e => e.elementos.Count(el => el.kind == "beam"));
            int muros  = data.edificios.Sum(e => e.elementos.Count(el => el.kind == "wall"));
            float area = data.edificios.Sum(e => e.losas.Sum(s => PolyArea(s.polygon)));

            Bounds qa = ModelBounds(mb);
            Debug.Log($"P1L2 QA bounds: center={qa.center} size={qa.size} "
                    + $"elementos={mb.ElementObjects.Count}.");

            Debug.Log($"P1L2 CI OK: {nodos} nodos, {cols} col, {vigs} vigas, "
                    + $"{muros} muros, losas = {area:F2} m2.");
            EditorApplication.Exit(0);
        }

        private static Bounds ModelBounds(P1L2ModelBuilder mb)
        {
            Bounds b = default;
            bool first = true;
            foreach (var go in mb.ElementObjects)
            {
                var r = go.GetComponent<Renderer>();
                if (r == null) continue;
                if (first) { b = r.bounds; first = false; }
                else b.Encapsulate(r.bounds);
            }
            return b;
        }

        private static float PolyArea(System.Collections.Generic.List<PointJson> pts)
        {
            float a = 0f;
            for (int i = 0; i < pts.Count; i++)
            {
                var p = pts[i];
                var q = pts[(i + 1) % pts.Count];
                a += p.x * q.y - q.x * p.y;
            }
            return Mathf.Abs(a) / 2f;
        }

        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
            {
                System.IO.Directory.CreateDirectory("Assets/Scenes");
                AssetDatabase.Refresh();
            }
            if (!AssetDatabase.IsValidFolder("Assets/Data"))
            {
                System.IO.Directory.CreateDirectory("Assets/Data");
                AssetDatabase.Refresh();
            }
        }
    }
}