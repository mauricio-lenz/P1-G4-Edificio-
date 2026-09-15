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
        public bool unirEdificios = true;
        public bool voladizoSimetrico = false;

        public readonly List<GameObject> ElementObjects = new List<GameObject>();

        private ModelDataP1L2 model;
        private Dictionary<int, NodeJson>[] nodeIdx;
        private Dictionary<string, float>[] elev;       // nivel → elevation por edificio
        private readonly Vector2[] planOff = new Vector2[2]; // desplazamiento plano por edificio
        private readonly float[] elevOff = new float[2];  // desplazamiento vertical por edificio
        private Vector2[] minsBB = new Vector2[2];
        private Vector2[] maxsBB = new Vector2[2];

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
            ComputePlanOffsets();
            MirrorVoladizos();
            ElementObjects.Clear();
            for (int ie = 0; ie < 2; ie++)
                BuildEdificio(model.edificios[ie], ie);
        }

        // Si unirEdificios está activo, elimina el hueco entre los dos edificios
        // uniéndolos por su lado angosto (cara corta). Si ambos son más largos
        // en x, se unen a lo largo de x (borde derecho A = borde izquierdo B)
        // y se alinean sus rangos en y para que queden contiguos sin separación.
        private void ComputePlanOffsets()
        {
            planOff[0] = planOff[1] = Vector2.zero;
            elevOff[0] = elevOff[1] = 0f;
            minsBB[0] = minsBB[1] = Vector2.zero;
            maxsBB[0] = maxsBB[1] = Vector2.zero;
            if (!unirEdificios || model.edificios.Count != 2) return;

            var maxZ = new float[2];
            for (int ie = 0; ie < 2; ie++)
            {
                float mnx = float.MaxValue, mny = float.MaxValue;
                float mxx = float.MinValue, mxy = float.MinValue;
                float mz = float.MinValue;
                foreach (var n in model.edificios[ie].nodos)
                {
                    mnx = Mathf.Min(mnx, n.x); mxx = Mathf.Max(mxx, n.x);
                    mny = Mathf.Min(mny, n.y); mxy = Mathf.Max(mxy, n.y);
                    mz = Mathf.Max(mz, n.z);
                }
                minsBB[ie] = new Vector2(mnx, mny);
                maxsBB[ie] = new Vector2(mxx, mxy);
                maxZ[ie] = mz;
            }

            int A = model.edificios[0].nodos.Count >= model.edificios[1].nodos.Count ? 0 : 1;
            int B = 1 - A;

            float dxA = maxsBB[A].x - minsBB[A].x, dyA = maxsBB[A].y - minsBB[A].y;
            float dxB = maxsBB[B].x - minsBB[B].x, dyB = maxsBB[B].y - minsBB[B].y;

            if (dxA >= dyA && dxB >= dyB)
            {
                // ambos más largos en x → unir a lo largo de x
                planOff[B].y = minsBB[A].y - minsBB[B].y; // alinear rangos y
                planOff[B].x = minsBB[A].x - maxsBB[B].x; // izquierdo A = derecho B
            }
            else
            {
                // fallback: unir a lo largo de y
                planOff[B].x = minsBB[A].x - minsBB[B].x;
                planOff[B].y = maxsBB[A].y - minsBB[B].y;
            }

            // techos a la misma altura: sube el edificio más bajo hasta igualar
            // el nivel superior del más alto
            float top = Mathf.Max(maxZ[A], maxZ[B]);
            elevOff[0] = top - maxZ[0];
            elevOff[1] = top - maxZ[1];
        }

        // Si un edificio tiene voladizos y el otro no, refleja el voladizo en el
        // extremo libre del otro (mismo vuelo y niveles superiores) para que el
        // conjunto continuo luzca simétrico.
        private void MirrorVoladizos()
        {
            if (!voladizoSimetrico || model.edificios.Count != 2) return;
            int src = -1, dst = -1;
            if (model.edificios[0].voladizos.Count > 0 && model.edificios[1].voladizos.Count == 0)
            {
                src = 0; dst = 1;
            }
            else if (model.edificios[1].voladizos.Count > 0 && model.edificios[0].voladizos.Count == 0)
            {
                src = 1; dst = 0;
            }
            else return;

            var volS = model.edificios[src].voladizos;
            var edD = model.edificios[dst];

            float dx = 0f, e = 0f;
            var levels = new List<string>();
            foreach (var v in volS)
            {
                dx = Mathf.Max(dx, v.x_max - maxsBB[src].x);
                e = v.e;
                if (!levels.Contains(v.nivel)) levels.Add(v.nivel);
            }
            if (dx <= 0f || levels.Count == 0) return;

            var hasLosa = new HashSet<string>();
            foreach (var s in edD.losas) hasLosa.Add(s.nivel);
            var tops = new List<string>();
            foreach (var lv in edD.niveles)
                if (hasLosa.Contains(lv.id)) tops.Add(lv.id);
            tops.Sort((a, b) => elev[dst][b].CompareTo(elev[dst][a]));
            tops = tops.GetRange(0, Mathf.Min(levels.Count, tops.Count));

            foreach (var nv in tops)
            {
                edD.voladizos.Add(new VoladizoJson
                {
                    id = "VOL-OESTE",
                    nivel = nv,
                    e = e,
                    x_min = minsBB[dst].x - dx,
                    x_max = minsBB[dst].x,
                    y_min = minsBB[dst].y,
                    y_max = maxsBB[dst].y
                });
            }
        }

        private Vector3 ToUnity(int ie, float x, float y, float z)
        {
            return CoordinateMap.OsToUnity(x + planOff[ie].x, y + planOff[ie].y, z + elevOff[ie]);
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
                ElementObjects.Add(BuildSlab(s, ie, grpS, elev[ie][s.nivel]));
            foreach (var v in ed.voladizos)
                ElementObjects.Add(BuildVoladizo(v, ie, root, elev[ie][v.nivel]));

            // barras
            foreach (var e in ed.elementos)
            {
                Transform parent = e.kind == "column" ? grpC :
                                   e.kind == "beam"   ? grpV : grpM;
                var go = BuildBarra(e, ie, nodeIdx[ie], parent);
                ElementObjects.Add(go);
                BuildAxes(e, go.transform, parent);
                BuildLabel(e, go.transform, parent);
            }

            // nodos
            foreach (var n in ed.nodos)
                BuildNodo(n, ie, grp);

            // apoyos
            foreach (var a in ed.apoyos)
                BuildApoyo(a, ie, nodeIdx[ie], grpA);

            // diafragmas (barras delgado master → slaves)
            foreach (var d in ed.diafragmas)
                BuildDiafragma(d, ie, nodeIdx[ie], grpD);
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

        private GameObject BuildBarra(ElementoJson e, int ie,
                                      Dictionary<int, NodeJson> nodes,
                                      Transform parent)
        {
            Vector3 p1 = ToUnity(ie, nodes[e.i].x, nodes[e.i].y, nodes[e.i].z);
            Vector3 p2 = ToUnity(ie, nodes[e.j].x, nodes[e.j].y, nodes[e.j].z);
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

        private GameObject BuildSlab(SlabJson s, int ie, Transform parent, float zElev)
        {
            Vector3 centro = SlabCentroid(s.polygon);
            float dx = SlabRangeX(s.polygon);
            float dy = SlabRangeY(s.polygon);

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = s.id;
            go.transform.localScale = new Vector3(dx, s.e, dy);
            go.transform.position = ToUnity(ie,
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

        private GameObject BuildVoladizo(VoladizoJson v, int ie, Transform parent, float zElev)
        {
            float xmid = (v.x_min + v.x_max) / 2f;
            float ymid = (v.y_min + v.y_max) / 2f;
            float dx   = v.x_max - v.x_min;
            float dy   = v.y_max - v.y_min;

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = v.id;
            go.transform.localScale = new Vector3(dx, v.e, dy);
            go.transform.position = ToUnity(ie,
                xmid, ymid, zElev + v.e / 2f);
            go.GetComponent<Renderer>().sharedMaterial = matVol;
            go.transform.SetParent(parent, false);
            RemoveCollider(go);
            return go;
        }

        /* ------------------------------------------------------------------ */
        /*  Nodos (esferas grises)                                              */
        /* ------------------------------------------------------------------ */

        private void BuildNodo(NodeJson n, int ie, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = $"N_{n.tag}";
            go.transform.position = ToUnity(ie, n.x, n.y, n.z);
            go.transform.localScale = Vector3.one * 0.44f;
            go.GetComponent<Renderer>().sharedMaterial = matNodo;
            go.transform.SetParent(parent, false);
            RemoveCollider(go);
        }

        /* ------------------------------------------------------------------ */
        /*  Apoyos (esferas verdes)                                             */
        /* ------------------------------------------------------------------ */

        private void BuildApoyo(ApoyoJson a, int ie, Dictionary<int, NodeJson> nodes, Transform parent)
        {
            var n = nodes[a.node];
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = $"Apoyo_{a.node}";
            go.transform.position = ToUnity(ie, n.x, n.y, n.z);
            go.transform.localScale = Vector3.one * 0.50f;
            go.GetComponent<Renderer>().sharedMaterial = matApoyo;
            go.transform.SetParent(parent, false);
        }

        /* ------------------------------------------------------------------ */
        /*  Diafragmas (cilindros delgados entre master y slaves)               */
        /* ------------------------------------------------------------------ */

        private void BuildDiafragma(DiafragmaJson d, int ie,
                                    Dictionary<int, NodeJson> nodes,
                                    Transform parent)
        {
            var m = nodes[d.master];
            Vector3 pM = ToUnity(ie, m.x, m.y, m.z);
            foreach (int sid in d.nodes)
            {
                var s = nodes[sid];
                Vector3 pS = ToUnity(ie, s.x, s.y, s.z);
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