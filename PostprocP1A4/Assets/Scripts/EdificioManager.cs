using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

// ============================================================================
//  CONTROLADOR PRINCIPAL del postprocesador Unity (P1A4).
//  1) Carga edificio_para_unity.json (generado por exportar_unity.py).
//  2) Construye la escena: losas, 390 elementos (H<tag>), apoyos, areas
//     tributarias y cargas.
//  3) Maneja seleccion por click, combinacion activa, deformada y diagramas
//     (momento / axial / corte) como mapa de calor global.
//  4) Delega en ElementInspectorUI y PanelDC la demanda-capacidad P-M.
//
//  Trazabilidad: elementTag del JSON == nombre del GameObject (H<tag>).
// ============================================================================
namespace EdificioUnity
{
    public class EdificioManager : MonoBehaviour
    {
        public static EdificioManager Current;

        public RaizEdificio Raiz;
        public string ComboActual = "U3";
        public bool enabledDeformada;
        public ElementoView Seleccion;
        public TipoDiagrama TipoDiagramaActivo;

        // escalas de normalizacion de las coloraciones
        public const float MScale = 2000f;     // kN·m maximo
        public const float NScale = 16000f;    // kN axial maximo
        public const float VScale = 400f;      // kN corte maximo

        List<ElementoView> _elementos = new List<ElementoView>();
        List<string> _combos = new List<string> { "U1", "U2", "U3", "U4" };
        int _comboIdx = 2;

        ElementInspectorUI _ui;
        PanelDC _panelDc;
        DiagramaRenderer _diag;

        Transform _trElementos, _trApoyos, _trAreas, _trCargas;

        // toggles
        bool _momento, _axial, _corte, _apoyos, _areas, _cargas;

        void Awake()
        {
            Current = this;
        }

        public SeccionDato BuscarSeccion(int tag) =>
            Raiz != null && Raiz.secciones.TryGetValue(tag.ToString(), out var s)
                ? s : null;

        void Start()
        {
            // raycast necesita colliders: capa por defecto
            try { Raiz = EdificioJsonLoader.Cargar(); }
            catch (System.Exception ex)
            {
                Debug.LogError("[EdificioManager] " + ex.Message);
                enabled = false;
                return;
            }
            _combos = Raiz.combos();

            CrearContenedores();
            ConstruirLosas();
            ConstruirElementos();
            ConstruirApoyos();
            ConstruirAreasYCargas();

            _ui = ElementInspectorUI.Crear();
            _panelDc = PanelDC.Crear(transform);
            _diag = DiagramaRenderer.Create(transform);

            if (FindObjectOfType<Camera>() != null &&
                FindObjectOfType<CameraOrbit>() == null)
                FindObjectOfType<Camera>().gameObject.AddComponent<CameraOrbit>();

            // seleccion inicial: columna 11020 (traza P1A3)
            Seleccionar(FindView(11020), true);
            AplicarCombo(true);
        }

        void CrearContenedores()
        {
            _trElementos = new GameObject("Elementos").transform;
            _trElementos.SetParent(transform, false);
            _trApoyos = new GameObject("Apoyos").transform;
            _trApoyos.SetParent(transform, false);
            _trAreas = new GameObject("AreasTributarias").transform;
            _trAreas.SetParent(transform, false);
            _trCargas = new GameObject("Cargas").transform;
            _trCargas.SetParent(transform, false);
        }

        // ------------------------------------------------------------------ construccion
        void ConstruirLosas()
        {
            foreach (var L in Raiz.losas)
            {
                var verts = new List<Vector3>();
                foreach (var v in L["vertices"])     // [x, y, z] modelo
                    verts.Add(Uni(v[0].Value<double>(), v[2].Value<double>(),
                                  v[1].Value<double>()));
                var tri = new List<int>();
                foreach (var t in L["triangulos"]) tri.Add(t.Value<int>());

                var go = new GameObject("Losa");
                go.transform.SetParent(transform, false);
                var mf = go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>();
                var mesh = new Mesh();
                mesh.vertices = verts.ToArray();
                mesh.triangles = tri.ToArray();
                mesh.RecalculateNormals();
                mf.sharedMesh = mesh;
                mr.sharedMaterial = new Material(Shader.Find("Standard"))
                {
                    color = new Color(0.55f, 0.6f, 0.55f, 1f)
                };
            }
        }

        void ConstruirElementos()
        {
            foreach (var d in Raiz.elementos)
            {
                var go = new GameObject("H" + d.elementTag);
                go.transform.SetParent(_trElementos, false);
                var ev = go.AddComponent<ElementoView>();
                ev.Construir(d, Raiz.deformada);
                var bc = go.AddComponent<BoxCollider>();
                bc.center = (d.A + d.B) * 0.5f;
                bc.size = BoundsDe(d);
                _elementos.Add(ev);
            }
        }

        Vector3 BoundsDe(ElementoDatos d)
        {
            float w = d.type == "muro" ? 0.2f : 0.7f;
            return new Vector3(Mathf.Abs(d.B.x - d.A.x) + w,
                               Mathf.Abs(d.B.y - d.A.y) + w,
                               Mathf.Abs(d.B.z - d.A.z) + w);
        }

        void ConstruirApoyos()
        {
            foreach (var ap in Raiz.apoyos)
            {
                var go = new GameObject("Apoyo" + ap.nodeTag);
                go.transform.SetParent(_trApoyos, false);
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = new Material(Shader.Find("Standard"))
                {
                    color = new Color(0.9f, 0.3f, 0.3f)
                };
                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = CubeMesh();
                go.transform.position = ap.Pos;
                go.transform.localScale = new Vector3(1.6f, 0.8f, 1.6f);
                Etiqueta3D.Crear(_trApoyos, ap.Pos + Vector3.up * 1.2f,
                                 ap.nodeTag.ToString(), 0.9f);
            }
        }

        void ConstruirAreasYCargas()
        {
            foreach (var at in Raiz.areas_tributarias)
            {
                // poligono del area tributaria
                if (at.poligono != null && at.poligono.Length > 2)
                {
                    var go = new GameObject("Area_" + at.elementTag);
                    go.transform.SetParent(_trAreas, false);
                    var lr = go.AddComponent<LineRenderer>();
                    lr.useWorldSpace = true;
                    lr.material = new Material(Shader.Find("Sprites/Default"));
                    lr.startWidth = lr.endWidth = 0.18f;
                    lr.startColor = lr.endColor = new Color(0.3f, 1f, 0.6f, 0.9f);
                    var pts = new List<Vector3>();
                    foreach (var p in at.poligono)
                    {
                        double x = p[0], y = p[1];
                        double z = NivelZ(at.nivel);
                        pts.Add(new Vector3((float)x, (float)z, (float)y));
                    }
                    pts.Add(pts[0]);
                    lr.positionCount = pts.Count;
                    lr.SetPositions(pts.ToArray());
                    var txt = Etiqueta3D.Crear(_trAreas, Vector3.zero,
                                               $"{at.area_trib_m2:0.0} m²", 0.8f);
                    txt.Mostrar($"{at.area_trib_m2:0.0} m²",
                                pts[pts.Count / 2] + Vector3.up * 0.6f);
                    go.gameObject.SetActive(true);
                }

                // cargas de viga (verticales, wG y wQ)
                var el = FindView(at.elementTag);
                if (el != null)
                {
                    var go = new GameObject("Carga_" + at.elementTag);
                    go.transform.SetParent(_trCargas, false);
                    var lr = go.AddComponent<LineRenderer>();
                    lr.useWorldSpace = true;
                    lr.material = new Material(Shader.Find("Sprites/Default"));
                    lr.startWidth = lr.endWidth = 0.22f;
                    lr.startColor = lr.endColor = new Color(0.2f, 0.4f, 1f, 0.9f);
                    Vector3 mid = (el.Datos.A + el.Datos.B) * 0.5f;
                    float mag = (float)at.wG_kN_m / 40f;
                    lr.positionCount = 2;
                    lr.SetPosition(0, mid);
                    lr.SetPosition(1, mid - Vector3.up * Mathf.Clamp(mag, 0.5f, 6f));
                    Etiqueta3D.Crear(_trCargas, mid - Vector3.up *
                        (Mathf.Clamp(mag, 0.5f, 6f) + 1f),
                        $"wG={at.wG_kN_m:0.#} wQ={at.wQ_kN_m:0.#} kN/m", 0.8f);
                }
            }
        }

        double NivelZ(int nivel)
        {
            var zl = Raiz._meta?["z_niveles_m"];
            if (zl is JArray zarr && nivel >= 1 && nivel - 1 < zarr.Count)
                return zarr[nivel - 1].Value<double>();
            if (zl != null && zl[nivel.ToString()] != null)
                return zl[nivel.ToString()].Value<double>();
            return 3.96 * nivel;
        }

        static Mesh CubeMesh()
        {
            var m = new Mesh();
            m.vertices = new[]
            {
                new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0, -0.5f),
                new Vector3(0.5f, 0, 0.5f),  new Vector3(-0.5f, 0, 0.5f),
                new Vector3(-0.5f, 1, -0.5f), new Vector3(0.5f, 1, -0.5f),
                new Vector3(0.5f, 1, 0.5f),  new Vector3(-0.5f, 1, 0.5f),
            };
            m.triangles = new[]
            {
                4,6,5, 4,7,6, 0,4,1, 1,4,5,
                1,5,2, 2,5,6, 2,6,3, 3,6,7,
                3,7,0, 0,7,4, 0,2,1, 0,3,2,
            };
            m.RecalculateNormals();
            return m;
        }

        // ------------------------------------------------------------------ combos / deformada
        public void AplicarCombo(bool forzar)
        {
            foreach (var ev in _elementos)
            {
                ev.SetCombo(ComboActual);
                if (_momento) ev.Pintar(TipoDiagrama.Momento);
                else if (_axial) ev.Pintar(TipoDiagrama.Axial);
                else if (_corte) ev.Pintar(TipoDiagrama.Corte);
                else ev.RestaurarColor();
                ev.Resaltar(ev == Seleccion);
            }
            _ui?.Actualizar(Seleccion != null ? Seleccion.Datos : null,
                            ComboActual, Raiz);
            if (Seleccion != null)
            {
                if (Seleccion.Datos.type == "columna" ||
                    Seleccion.Datos.type == "muro")
                    _panelDc?.Mostrar(Seleccion.Datos, ComboActual, Raiz);
                _diag?.Dibujar(Seleccion, TipoDiagramaActivo, ComboActual);
            }
            if (forzar) ActualizarVisibilidad();
        }

        public void CiclarCombo()
        {
            _comboIdx = (_comboIdx + 1) % _combos.Count;
            ComboActual = _combos[_comboIdx];
            AplicarCombo(true);
            if (_ui != null) _ui.CampearCombo(ComboActual);
        }

        public void ToggleDeformada()
        {
            enabledDeformada = !enabledDeformada;
            // los offsets se recalculan en ElementoView.SetCombo segun el flag
            AplicarCombo(true);
        }

        public void ToggleDiagrama(TipoDiagrama tipo)
        {
            bool activo = tipo == TipoDiagrama.Momento ? _momento
                : tipo == TipoDiagrama.Axial ? _axial : _corte;
            activo = !activo;
            _momento = tipo == TipoDiagrama.Momento && activo;
            _axial = tipo == TipoDiagrama.Axial && activo;
            _corte = tipo == TipoDiagrama.Corte && activo;
            if (activo) TipoDiagramaActivo = tipo;
            AplicarCombo(false);
        }

        public void ToggleApoyos() { _apoyos = !_apoyos; ActualizarVisibilidad(); }
        public void ToggleAreas() { _areas = !_areas; ActualizarVisibilidad(); }
        public void ToggleCargas() { _cargas = !_cargas; ActualizarVisibilidad(); }

        void ActualizarVisibilidad()
        {
            if (_trApoyos != null) _trApoyos.gameObject.SetActive(_apoyos);
            if (_trAreas != null) _trAreas.gameObject.SetActive(_areas);
            if (_trCargas != null) _trCargas.gameObject.SetActive(_cargas);
        }

        // ------------------------------------------------------------------ seleccion
        public void Seleccionar(ElementoView ev, bool inicial)
        {
            if (Seleccion != null) Seleccion.Resaltar(false);
            Seleccion = ev;
            if (ev != null)
            {
                ev.Resaltar(true);
                _ui?.Actualizar(ev.Datos, ComboActual, Raiz);
                if (ev.Datos.type == "columna" || ev.Datos.type == "muro")
                    _panelDc?.Mostrar(ev.Datos, ComboActual, Raiz);
                else _panelDc?.Ocultar();
                _diag?.Dibujar(ev, TipoDiagramaActivo, ComboActual);
            }
            else
            {
                _ui?.Actualizar(null, ComboActual, Raiz);
                _panelDc?.Ocultar();
                _diag?.Ocultar();
            }
        }

        public ElementoView FindView(int tag) =>
            _elementos.Find(e => e.elementTag == tag);

        void Update()
        {
            // teclas de la demo
            for (int i = 0; i < _combos.Count; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i)) { SetComboIdx(i); return; }

            if (Input.GetKeyDown(KeyCode.D)) ToggleDeformada();
            if (Input.GetKeyDown(KeyCode.M)) ToggleDiagrama(TipoDiagrama.Momento);
            if (Input.GetKeyDown(KeyCode.A)) ToggleDiagrama(TipoDiagrama.Axial);
            if (Input.GetKeyDown(KeyCode.V)) ToggleDiagrama(TipoDiagrama.Corte);
            if (Input.GetKeyDown(KeyCode.S)) ToggleApoyos();
            if (Input.GetKeyDown(KeyCode.T)) ToggleAreas();
            if (Input.GetKeyDown(KeyCode.C)) ToggleCargas();
            if (Input.GetKeyDown(KeyCode.Escape))
            { Seleccionar(null, false); return; }

            if (Input.GetMouseButtonDown(0) &&
                (UnityEngine.EventSystems.EventSystem.current == null ||
                 !UnityEngine.EventSystems.EventSystem.current
                     .IsPointerOverGameObject()))
            {
                var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                if (Physics.Raycast(ray, out var hit, 1000f))
                {
                    var ev = hit.collider.GetComponent<ElementoView>();
                    if (ev != null) { Seleccionar(ev, false); return; }
                }
                Seleccionar(null, false);
            }
        }

        void SetComboIdx(int i)
        {
            _comboIdx = i;
            ComboActual = _combos[i];
            AplicarCombo(true);
            if (_ui != null) _ui.CampearCombo(ComboActual);
        }

        static Vector3 Uni(double x, double y, double z) =>
            new Vector3((float)x, (float)y, (float)z);
    }
}