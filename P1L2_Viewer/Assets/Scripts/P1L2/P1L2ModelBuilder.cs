using System.Collections.Generic;
using UnityEngine;

namespace P1L2.Viewer
{
    /// <summary>
    /// Genera la escena P1L2 desde un TextAsset del contrato P1L2/model_data/1.0.
    /// Jerarquía: Modelo P1L2 / Edificio {1,2} / {Nodos,Columnas,Vigas,Muros,Losas,Apoyos,Diafragmas}.
    /// Convención geométrica: CoordinateMap.OsToUnity(x_os, y_os, z_os).
    /// </summary>
    public class P1L2ModelBuilder : MonoBehaviour
    {
        public TextAsset modelJson;
        public bool buildOnStart = true;
        public bool showLabels = false;

        public readonly List<GameObject> ElementObjects = new List<GameObject>();

        private ModelDataP1L2 model;
        private Dictionary<int, NodeJson>[] nodeIdx;
        private Dictionary<string, float>[] elev;       // nivel → elevation por edificio

        private Material matCol, matVig, matMuro, matNodo, matApoyo,
                         matSlab, matVol, matDia, matL1, matL2, matL3;

        private void Start()
        {
            if (buildOnStart && modelJson != null)
                BuildFromAsset(modelJson);
        }

        public void BuildFromAsset(TextAsset asset)
        {
            model = P1L2Loader.Load(asset);
            if (model == null || model.edificios == null || model.edificios.Count < 2)
            {
                Debug.LogError("P1L2: se esperaban 2 edificios en el contrato.");
                return;
            }
            CreateMaterials();
            nodeIdx = new Dictionary<int, NodeJson>[2];
            elev = new Dictionary<string, float>[2];
            for (int ie = 0; ie < 2; ie++)
            {
                nodeIdx[ie] = P1L2Loader.NodeIndex(model.edificios[ie]);
                elev[ie] = new Dictionary<string, float>();
                foreach (var lv in model.edificios[ie].niveles)
                    elev[ie][lv.id] = lv.elevation;
            }
            ElementObjects.Clear();
            for (int ie = 0; ie < 2; ie++)
                BuildEdificio(model.edificios[ie], ie);
        }

        /* ------------------------------------------------------------------ */
        /*  Materiales                                                         */
        /* ------------------------------------------------------------------ */

        private void CreateMaterials()
        {
            matCol  = StdMat("Col",    new Color(0.745f, 0.235f, 0.196f));
            matVig  = StdMat("Vig",    new Color(0.275f, 0.471f, 0.824f));
            matMuro = StdMat("Muro",   new Color(0.941f, 0.549f, 0.196f));
            matNodo = StdMat("Nodo",   new Color(0.353f, 0.353f, 0.392f));
            matApoyo= StdMat("Apoyo",  new Color(0.196f, 0.706f, 0.314f));
            matSlab = StdMat("Slab",   new Color(0.588f, 0.588f, 0.627f), 0.25f);
            matVol  = StdMat("Vol",    new Color(0.863f, 0.706f, 0.196f), 0.35f);
            matDia  = StdMat("Dia",    new Color(0.600f, 0.600f, 0.600f), 0.15f);
            matL1   = StdMat("L1",     new Color(1f, 0f, 1f));
            matL2   = StdMat("L2",     new Color(0f, 0.784f, 0.784f));
            matL3   = StdMat("L3",     new Color(0.784f, 0.784f, 0f));
        }

        private static Material StdMat(string name, Color c, float alpha = 1f)
        {
            var m = new Material(Shader.Find("Standard")) { name = name, color = c };
            if (alpha < 1f)
            {
                m.SetFloat("_Mode", 3);
                m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetInt("_ZWrite", 0);
                m.DisableKeyword("_ALPHATEST_ON");
                m.EnableKeyword("_ALPHABLEND_ON");
                m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                m.renderQueue = 3000;
                c.a = alpha;
                m.color = c;
            }
            return m;
        }

        /* ------------------------------------------------------------------ */
        /*  Construcción por edificio                                           */
        /* ------------------------------------------------------------------ */

        private void BuildEdificio(EdificioJson ed, int ie)
        {
            var root = new GameObject(ed.nombre).transform;
            var grp = Group("Nodos", root);
            var grpC = Group("Columnas", root);
            var grpV = Group("Vigas", root);
            var grpM = Group("Muros", root);
            var grpS = Group("Losas", root);
            var grpA = Group("Apoyos", root);
            var grpD = Group("Diafragmas", root);

            // losas y voladizos (requieren elevación)
            foreach (var s in ed.losas)
                ElementObjects.Add(BuildSlab(s, grpS, elev[ie][s.nivel]));
            foreach (var v in ed.voladizos)
                ElementObjects.Add(BuildVoladizo(v, root, elev[ie][v.nivel]));

            // barras
            foreach (var e in ed.elementos)
            {
                Transform parent = e.kind == "column" ? grpC :
                                   e.kind == "beam"   ? grpV : grpM;
                var go = BuildBarra(e, nodeIdx[ie], parent);
                ElementObjects.Add(go);
                BuildAxes(e, go.transform, parent);
                BuildLabel(e, go.transform, parent);
            }

            // nodos
            foreach (var n in ed.nodos)
                BuildNodo(n, grp);

            // apoyos
            foreach (var a in ed.apoyos)
                BuildApoyo(a, nodeIdx[ie], grpA);

            // diafragmas (barras delgado master → slaves)
            foreach (var d in ed.diafragmas)
                BuildDiafragma(d, nodeIdx[ie], grpD);
        }

        private static Transform Group(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        /* ------------------------------------------------------------------ */
        /*  Barras (columnas / vigas / muros)                                   */
        /* ------------------------------------------------------------------ */

        private GameObject BuildBarra(ElementoJson e,
                                      Dictionary<int, NodeJson> nodes,
                                      Transform parent)
        {
            Vector3 p1 = CoordinateMap.OsToUnity(nodes[e.i].x, nodes[e.i].y, nodes[e.i].z);
            Vector3 p2 = CoordinateMap.OsToUnity(nodes[e.j].x, nodes[e.j].y, nodes[e.j].z);
            Vector3 dir = p2 - p1;
            float len = dir.magnitude;
            float r = e.kind == "column" ? 0.45f
                     : e.kind == "beam"   ? 0.40f : 0.18f;

            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = $"{e.kind}_{e.tag}";
            go.transform.position = (p1 + p2) / 2f;
            go.transform.rotation = Quaternion.FromToRotation(Vector3.up, dir);
            go.transform.localScale = new Vector3(r * 2, len / 2f, r * 2);
            go.GetComponent<Renderer>().sharedMaterial =
                e.kind == "column" ? matCol : e.kind == "beam" ? matVig : matMuro;
            go.transform.SetParent(parent, false);

            var info = go.AddComponent<ElementoInfo>();
            info.elementTag = e.tag;
            info.Kind       = e.kind;
            info.Nivel      = e.nivel;
            info.Seccion    = e.seccion;
            info.NodeI      = e.i;
            info.NodeJ      = e.j;
            info.EjeL1      = dir.normalized;
            return go;
        }

        /* ------------------------------------------------------------------ */
        /*  Ejes locales (L1, L2, L3)                                          */
        /* ------------------------------------------------------------------ */

        private void BuildAxes(ElementoJson e, Transform elem, Transform parent)
        {
            var info = elem.GetComponent<ElementoInfo>();
            Vector3 L1 = info.EjeL1;
            Vector3 L2 = Vector3.Cross(L1, Vector3.up).normalized;
            if (L2.sqrMagnitude < 1e-6f)
                L2 = Vector3.Cross(L1, Vector3.right).normalized;
            Vector3 L3 = Vector3.Cross(L1, L2).normalized;

            SpawnCylinder(elem.position, L1, 0.6f, 0.02f, matL1, parent, "L1");
            SpawnCylinder(elem.position, L2, 0.6f, 0.02f, matL2, parent, "L2");
            SpawnCylinder(elem.position, L3, 0.6f, 0.02f, matL3, parent, "L3");
        }

        private static void SpawnCylinder(Vector3 origin, Vector3 dir,
                                           float length, float radius,
                                           Material mat, Transform parent, string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.position = origin + dir * (length / 2f);
            go.transform.rotation = Quaternion.FromToRotation(Vector3.up, dir);
            go.transform.localScale = new Vector3(radius * 2, length / 2f, radius * 2);
            go.GetComponent<Renderer>().sharedMaterial = mat;
            go.transform.SetParent(parent, false);
            RemoveCollider(go);
        }

        /* ------------------------------------------------------------------ */
        /*  Etiquetas ID                                                       */
        /* ------------------------------------------------------------------ */

private void BuildLabel(ElementoJson e, Transform elem, Transform parent)
        {
            if (!showLabels) return;
            var go = new GameObject($"ID_{e.tag}");
            go.transform.position = elem.position + Vector3.up * 0.5f;
            var tm = go.AddComponent<TextMesh>();
            tm.text = $"{e.tag} {e.kind[0]}";
            tm.characterSize = 0.015f;
            tm.fontSize = 60;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.color = new Color(0.12f, 0.12f, 0.12f);
            go.transform.localScale = Vector3.one * 0.3f;
            go.transform.SetParent(parent, false);
        }

        /* ------------------------------------------------------------------ */
        /*  Losas (cubos translúcidos)                                          */
        /* ------------------------------------------------------------------ */

        private GameObject BuildSlab(SlabJson s, Transform parent, float zElev)
        {
            Vector3 centro = SlabCentroid(s.polygon);
            float dx = SlabRangeX(s.polygon);
            float dy = SlabRangeY(s.polygon);

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = s.id;
            go.transform.localScale = new Vector3(dx, s.e, dy);
            go.transform.position = CoordinateMap.OsToUnity(
                centro.x, centro.y, zElev + s.e / 2f);
            go.GetComponent<Renderer>().sharedMaterial = matSlab;
            go.transform.SetParent(parent, false);
            RemoveCollider(go);
            return go;
        }

        private static Vector3 SlabCentroid(List<PointJson> pts)
        {
            float cx = 0, cy = 0;
            foreach (var p in pts) { cx += p.x; cy += p.y; }
            int n = pts.Count;
            return new Vector3(cx / n, cy / n, 0f);
        }

        private static float SlabRangeX(List<PointJson> pts)
        {
            float mn = float.MaxValue, mx = float.MinValue;
            foreach (var p in pts) { mn = Mathf.Min(mn, p.x); mx = Mathf.Max(mx, p.x); }
            return mx - mn;
        }

        private static float SlabRangeY(List<PointJson> pts)
        {
            float mn = float.MaxValue, mx = float.MinValue;
            foreach (var p in pts) { mn = Mathf.Min(mn, p.y); mx = Mathf.Max(mx, p.y); }
            return mx - mn;
        }

        /* ------------------------------------------------------------------ */
        /*  Voladizos (cubos ámbar)                                             */
        /* ------------------------------------------------------------------ */

        private GameObject BuildVoladizo(VoladizoJson v, Transform parent, float zElev)
        {
            float xmid = (v.x_min + v.x_max) / 2f;
            float ymid = (v.y_min + v.y_max) / 2f;
            float dx   = v.x_max - v.x_min;
            float dy   = v.y_max - v.y_min;

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = v.id;
            go.transform.localScale = new Vector3(dx, v.e, dy);
            go.transform.position = CoordinateMap.OsToUnity(
                xmid, ymid, zElev + v.e / 2f);
            go.GetComponent<Renderer>().sharedMaterial = matVol;
            go.transform.SetParent(parent, false);
            RemoveCollider(go);
            return go;
        }

        /* ------------------------------------------------------------------ */
        /*  Nodos (esferas grises)                                              */
        /* ------------------------------------------------------------------ */

        private void BuildNodo(NodeJson n, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = $"N_{n.tag}";
            go.transform.position = CoordinateMap.OsToUnity(n.x, n.y, n.z);
            go.transform.localScale = Vector3.one * 0.44f;
            go.GetComponent<Renderer>().sharedMaterial = matNodo;
            go.transform.SetParent(parent, false);
            RemoveCollider(go);
        }

        /* ------------------------------------------------------------------ */
        /*  Apoyos (esferas verdes)                                             */
        /* ------------------------------------------------------------------ */

        private void BuildApoyo(ApoyoJson a, Dictionary<int, NodeJson> nodes, Transform parent)
        {
            var n = nodes[a.node];
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = $"Apoyo_{a.node}";
            go.transform.position = CoordinateMap.OsToUnity(n.x, n.y, n.z);
            go.transform.localScale = Vector3.one * 0.50f;
            go.GetComponent<Renderer>().sharedMaterial = matApoyo;
            go.transform.SetParent(parent, false);
        }

        /* ------------------------------------------------------------------ */
        /*  Diafragmas (cilindros delgados entre master y slaves)               */
        /* ------------------------------------------------------------------ */

        private void BuildDiafragma(DiafragmaJson d,
                                    Dictionary<int, NodeJson> nodes,
                                    Transform parent)
        {
            var m = nodes[d.master];
            Vector3 pM = CoordinateMap.OsToUnity(m.x, m.y, m.z);
            foreach (int sid in d.nodes)
            {
                var s = nodes[sid];
                Vector3 pS = CoordinateMap.OsToUnity(s.x, s.y, s.z);
                Vector3 dir = pS - pM;
                float len = dir.magnitude;
                SpawnCylinder((pM + pS) / 2f, dir, len, 0.05f,
                              matDia, parent, $"Dia_{d.id}_{sid}");
            }
        }

        /* ------------------------------------------------------------------ */
        /*  Utilidades                                                         */
        /* ------------------------------------------------------------------ */

        private static void RemoveCollider(GameObject go)
        {
            var c = go.GetComponent<Collider>();
            if (c != null) DestroyImmediate(c);
        }
    }
}