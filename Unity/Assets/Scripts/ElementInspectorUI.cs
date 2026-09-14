using UnityEngine;
using UnityEngine.UI;

// ============================================================================
//  Inspector del elemento seleccionado (UI uGUI construida en runtime).
//  Muestra: ID, nodos, seccion, material, ejes locales, restricciones y los
//  esfuerzos N, Vy, Vz, T, My, Mz de la combinacion activa.
//  Incluye teclas de acceso rapido para la demo y botones de combo.
// ============================================================================
namespace EdificioUnity
{
    public class ElementInspectorUI : MonoBehaviour
    {
        Text _titulo;
        Text _info;
        Text _combo;
        Text _ayuda;
        Button _btnCombo;

        public static ElementInspectorUI Crear()
        {
            if (Object.FindObjectOfType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
            }
            var canvas = Object.FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                var cg = new GameObject("Canvas");
                canvas = cg.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                var scaler = cg.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1600, 900);
                cg.AddComponent<GraphicRaycaster>();
            }
            var ui = canvas.gameObject.AddComponent<ElementInspectorUI>();
            return ui;
        }

        void Awake()
        {
            var canvas = GetComponent<Canvas>();

            // panel derecho (inspector)
            var panel = new GameObject("PanelInspector");
            panel.transform.SetParent(canvas.transform, false);
            var img = panel.AddComponent<Image>();
            img.color = new Color(0, 0, 0, 0.55f);
            var rt = panel.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.68f, 0.05f);
            rt.anchorMax = new Vector2(0.99f, 0.85f);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

            _titulo = Texto("Titulo", canvas.transform, 26, TextAnchor.UpperLeft);
            _titulo.rectTransform.anchorMin = new Vector2(0.69f, 0.8f);
            _titulo.rectTransform.anchorMax = new Vector2(0.98f, 0.9f);

            _info = Texto("Info", canvas.transform, 17, TextAnchor.UpperLeft);
            _info.rectTransform.anchorMin = new Vector2(0.70f, 0.10f);
            _info.rectTransform.anchorMax = new Vector2(0.98f, 0.78f);
            _info.color = new Color(0.95f, 0.96f, 1f);
            _info.horizontalOverflow = HorizontalWrapMode.Wrap;
            _info.verticalOverflow = VerticalWrapMode.Truncate;
            _info.lineSpacing = 1.15f;
            _info.alignment = TextAnchor.UpperLeft;

            _combo = Texto("Combo", canvas.transform, 22, TextAnchor.UpperLeft);
            _combo.rectTransform.anchorMin = new Vector2(0.69f, 0.90f);
            _combo.rectTransform.anchorMax = new Vector2(0.98f, 0.96f);
            _combo.color = new Color(1f, 0.85f, 0.4f);
            _combo.alignment = TextAnchor.UpperLeft;

            _ayuda = Texto("Ayuda", canvas.transform, 14, TextAnchor.UpperLeft);
            _ayuda.rectTransform.anchorMin = new Vector2(0.01f, 0.94f);
            _ayuda.rectTransform.anchorMax = new Vector2(0.66f, 0.99f);
            _ayuda.color = new Color(0.75f, 0.78f, 0.85f);
            _ayuda.alignment = TextAnchor.UpperLeft;
            _ayuda.text =
                "CLICK: seleccionar elemento\n" +
                "1-4: combo U1..U4   |   D: deformada\n" +
                "M: momento    A: axial    V: corte\n" +
                "S: apoyos   T: areas tributarias   C: cargas\n" +
                "ESC: deseleccionar   (eje local en los nodos)";

            _btnCombo = Boton("BtnCombo", "Combo >", canvas.transform,
                              new Vector2(0.02f, 0.90f), new Vector2(0.15f, 0.94f));
            _btnCombo.onClick.AddListener(() =>
                EdificioManager.Current?.CiclarCombo());
        }

        public void Actualizar(ElementoDatos el, string combo, RaizEdificio raiz)
        {
            if (el == null)
            {
                _titulo.text = "Ningun elemento seleccionado";
                _info.text = "";
                _combo.text = "Combinacion: " + combo;
                return;
            }
            var sec = raiz.secciones.TryGetValue(el.sectionTag.ToString(),
                out var s) ? s : null;
            var mat = sec != null && raiz.materiales.TryGetValue(
                sec.matTag.ToString(), out var m) ? m : null;
            var res = el.resultados.TryGetValue(combo, out var r) ? r : null;

            _titulo.text = $"#{el.elementTag}  {el.type}  ·  {el.descripcion}";
            _combo.text = "Combinacion activa: " + combo + "   " +
                          Formula(combo);

            string ejes =
                $"Lx=({el.AX.x:0.000}; {el.AX.y:0.000}; {el.AX.z:0.000})\n" +
                $"Ly=({el.AY.x:0.000}; {el.AY.y:0.000}; {el.AY.z:0.000})\n" +
                $"Lz=({el.AZ.x:0.000}; {el.AZ.y:0.000}; {el.AZ.z:0.000})";

            _info.text =
                $"→ Nodos: {el.iNode} [{el.A}]  →  {el.jNode} [{el.B}]\n" +
                $"→ Seccion: {sec?.nombre ?? "?"} (tag {el.sectionTag})  " +
                $"{sec?.b_m ?? 0:0.00} x {sec?.h_m ?? 0:0.00} m, A={sec?.A_m2 ?? 0:0.00} m²\n" +
                $"   Iy={sec?.Iy_m4 ?? 0:0.00000} m⁴  Iz={sec?.Iz_m4 ?? 0:0.00000} m⁴  J={sec?.J_m4 ?? 0:0.00000} m⁴\n" +
                $"→ Material: {mat?.nombre ?? "G35"}  f'c={mat?.fc_MPa ?? 35} MPa  fy={mat?.fy_MPa ?? 420} MPa\n" +
                $"   E={mat?.E_kPa ?? 27.8e6:0} kPa  G={mat?.G_kPa ?? 11.6e6:0} kPa\n" +
                $"→ Ejes locales:\n{ejes}\n" +
                $"→ Restricciones: {(el.apoyado_base ? "EMPOTRADO EN BASE; " : "")}" +
                $"{string.Join("; ", el.restricciones ?? new string[0])}\n" +
                (el.area_trib_m2.HasValue
                    ? $"→ Area tributaria: {el.area_trib_m2:0.00} m²\n" : "") +
                (res == null ? ""
                    : $"→ Esfuerzos [{combo}]:\n" +
                      $"   N = {res.N:0.0} kN\n" +
                      $"   Vy = {res.Vy:0.0} kN    Vz = {res.Vz:0.0} kN\n" +
                      $"   T = {res.T:0.0} kN·m\n" +
                      $"   My = {res.My:0.0} kN·m    Mz = {res.Mz:0.0} kN·m");
        }

        public void CampearCombo(string combo) => _combo.text =
            "Combinacion activa: " + combo + "   " + Formula(combo);

        static string Formula(string c) =>
            c == "U1" ? "= 1,4·G"
            : c == "U2" ? "= 1,2·G + 1,6·Q"
            : c == "U3" ? "= 1,2·G + Q + EX"
            : "= 0,9·G + EY";

        static Text Texto(string n, Transform p, int tam, TextAnchor a)
        {
            var g = new GameObject(n);
            g.transform.SetParent(p, false);
            var t = g.AddComponent<Text>();
            t.font = Font.CreateDynamicFontFromOSFont(new[]
                {"Arial", "Segoe UI", "Liberation Sans"}, tam);
            t.fontSize = tam;
            t.color = Color.white;
            t.alignment = a;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            var rt = g.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(300, 120);
            return t;
        }

        static Button Boton(string n, string lbl, Transform p,
                            Vector2 aMin, Vector2 aMax)
        {
            var g = new GameObject(n);
            g.transform.SetParent(p, false);
            var img = g.AddComponent<Image>();
            img.color = new Color(0.2f, 0.45f, 0.8f, 0.9f);
            var bt = g.AddComponent<Button>();
            bt.targetGraphic = img;
            var rt = g.GetComponent<RectTransform>();
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var txt = Texto("Label", g.transform, 16, TextAnchor.MiddleCenter);
            txt.text = lbl;
            txt.rectTransform.anchorMin = Vector2.zero;
            txt.rectTransform.anchorMax = Vector2.one;
            txt.rectTransform.offsetMin = Vector2.zero;
            txt.rectTransform.offsetMax = Vector2.zero;
            return bt;
        }
    }
}