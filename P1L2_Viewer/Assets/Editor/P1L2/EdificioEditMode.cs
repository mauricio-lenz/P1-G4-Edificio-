using UnityEditor;
using UnityEngine;

namespace P1L2.Viewer
{
    /// <summary>
    /// Menú P1L2: Build Model / Validate Model.
    /// Build Model ejecuta P1L2ModelBuilder.BuildFromAsset en la escena activa
    /// y centra la cámara con P1L2Camera.LookAtAllElements.
    /// Validate Model imprime verificaciones 5/5 vía Python (invocación externa)
    /// o resumen rápido de nodos/col/vig/muros/losas.
    /// </summary>
    public static class EdificioEditMode
    {
        [MenuItem("P1L2/Build Model")]
        public static void BuildModel()
        {
            var mb = Object.FindObjectOfType<P1L2ModelBuilder>();
            if (mb == null)
            {
                Debug.LogError("P1L2: No hay P1L2ModelBuilder en la escena.");
                return;
            }
            if (mb.modelJson == null)
            {
                Debug.LogError("P1L2: modelJson no asignado en P1L2ModelBuilder.");
                return;
            }
            mb.BuildFromAsset(mb.modelJson);
            var cam = Object.FindObjectOfType<P1L2Camera>();
            if (cam != null && mb.ElementObjects.Count > 0)
                cam.LookAtAllElements();
        }

        [MenuItem("P1L2/Validate Model")]
        public static void ValidateModel()
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Data/model_data_p1l2.json");
            if (asset == null)
            {
                Debug.LogError("P1L2 Validate: model_data_p1l2.json no encontrado en Assets/Data/.");
                return;
            }
            var model = P1L2Loader.Load(asset);
            int nodos = 0, cols = 0, vigs = 0, muros = 0;
            float areaLosas = 0f;
            foreach (var ed in model.edificios)
            {
                nodos += ed.nodos.Count;
                cols  += ed.elementos.FindAll(e => e.kind == "column").Count;
                vigs  += ed.elementos.FindAll(e => e.kind == "beam").Count;
                muros += ed.elementos.FindAll(e => e.kind == "wall").Count;
                foreach (var s in ed.losas)
                    areaLosas += PolygonArea(s.polygon);
            }
            Debug.Log($"P1L2 Validate: {nodos} nodos, {cols} col, {vigs} vigas, {muros} muros, " +
                      $"losas = {areaLosas:F2} m2.");
        }

        private static float PolygonArea(System.Collections.Generic.List<PointJson> pts)
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
    }
}