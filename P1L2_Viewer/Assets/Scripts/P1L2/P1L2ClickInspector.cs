using System.Collections.Generic;
using UnityEngine;

// ============================================================================
//  Inspector IMGUI del viewer P1L2: al hacer clic izquierdo en una barra
//  (columna / viga / muro) muestra un recuadro con tag, tipo, nivel,
//  sección, nodos, ejes locales y las áreas tributarias / kN que descargan.
//  Se añade automáticamente a "App" por SceneAutoBuilder.
// ============================================================================
namespace P1L2.Viewer
{
    public class P1L2ClickInspector : MonoBehaviour
    {
        [SerializeField] private P1L2ModelBuilder modelBuilder;

        private ModelDataP1L2 model;
        private string selection = "";
        private string details = "";

        void Awake()
        {
            if (modelBuilder == null)
                modelBuilder = GetComponent<P1L2ModelBuilder>();
        }

        void Start()
        {
            if (modelBuilder != null && modelBuilder.modelJson != null)
                model = P1L2Loader.Load(modelBuilder.modelJson);
        }

        void Update()
        {
            if (Input.GetMouseButtonDown(0))
            {
                var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                if (Physics.Raycast(ray, out var hit, 2000f))
                {
                    var info = hit.collider.GetComponent<ElementoInfo>();
                    if (info != null) { ShowElementInfo(info); return; }
                }
                selection = "";
                details = "";
            }
        }

        private void ShowElementInfo(ElementoInfo info)
        {
            selection = $"{info.Kind} #{info.elementTag}";
            details =
                $"Nivel: {info.Nivel}\n" +
                $"Seccion: {info.Seccion}\n" +
                $"Nodos: I={info.NodeI}  J={info.NodeJ}\n" +
                $"Eje L1: ({info.EjeL1.x:0.000}; {info.EjeL1.y:0.000}; {info.EjeL1.z:0.000})\n";

            double area = 0, fG = 0, fQ = 0;
            var slabsInvolved = new HashSet<string>();

            if (model != null)
            {
                foreach (var ed in model.edificios)
                {
                    var slabQ = new Dictionary<string, Vector2>();
                    foreach (var s in ed.losas)
                        slabQ[s.id] = new Vector2(s.qG, s.qQ);

                    foreach (var ta in ed.tributary_areas)
                    {
                        if (ta.element == info.elementTag)
                        {
                            area += ta.area;
                            if (slabQ.TryGetValue(ta.slab, out var q))
                            {
                                fG += ta.area * q.x;
                                fQ += ta.area * q.y;
                            }
                            slabsInvolved.Add(ta.slab);
                        }
                    }
                }
            }

            if (area > 0)
            {
                details += $"Area tributaria: {area:0.00} m2\n" +
                           $"Carga G (qG*A): {fG:0.00} kN\n" +
                           $"Carga Q (qQ*A): {fQ:0.00} kN\n" +
                           $"Losas: {string.Join(", ", slabsInvolved)}";
            }
            else
            {
                details += "Sin areas tributarias asignadas";
            }
        }

        void OnGUI()
        {
            if (string.IsNullOrEmpty(selection)) return;

            var box = new GUIStyle(GUI.skin.box);
            box.normal.background = MakeTex(2, 2, new Color(0.05f, 0.06f, 0.10f, 0.8f));

            GUILayout.BeginArea(new Rect(12, 12, 340, 200), box);
            GUILayout.Label("INSPECTOR P1L2", GUILayout.Height(22));
            GUILayout.Label(selection, GUILayout.Height(18));
            GUILayout.Space(4);
            GUILayout.Label(details);
            GUILayout.EndArea();
        }

        private static Texture2D MakeTex(int w, int h, Color col)
        {
            var px = new Color[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = col;
            var t = new Texture2D(w, h);
            t.SetPixels(px);
            t.Apply();
            return t;
        }
    }
}
