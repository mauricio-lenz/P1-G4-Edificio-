using Newtonsoft.Json.Linq;
using UnityEngine;

// ============================================================================
//  Panel demanda-capacidad: dibuja en el espacio de la escena la curva P-M
//  (KN axial vs kN·m de momento) de la columna 11020 o del muro 11033 y el
//  punto de demanda de la combinacion activa (con su DCR).
//  Datos: raiz.demanda_capacidad (generado en P1A3/curvas_pm + exportador).
// ============================================================================
namespace EdificioUnity
{
    public class PanelDC : MonoBehaviour
    {
        const float S_P = 10f / 17000f;   // kN  -> metros (eje vertical +Y)
        const float S_M = 8f / 2300f;     // kN·m -> metros (eje horizontal +X)
        static readonly Vector3 Origen = new Vector3(20f, 8f, 64f);

        LineRenderer _curva, _punto, _axes;
        TextMesh _rotulo;

        public static PanelDC Crear(Transform parent)
        {
            var go = new GameObject("PanelDC");
            go.transform.SetParent(parent, false);
            var p = go.AddComponent<PanelDC>();
            p._curva = NuevaLinea(go.transform, "CurvaPM", 0.12f,
                                  new Color(0.35f, 0.6f, 1f));
            p._punto = NuevaLinea(go.transform, "PuntoDemanda", 0.28f,
                                  new Color(1f, 0.25f, 0.15f));
            p._axes = NuevaLinea(go.transform, "Ejes", 0.06f,
                                 new Color(0.8f, 0.8f, 0.8f));
            var gTxt = new GameObject("Rotulo");
            gTxt.transform.SetParent(go.transform, false);
            p._rotulo = gTxt.AddComponent<TextMesh>();
            p._rotulo.characterSize = 0.5f;
            p._rotulo.fontSize = 40;
            p.Ocultar();
            return p;
        }

        static LineRenderer NuevaLinea(Transform p, string n, float w, Color c)
        {
            var g = new GameObject(n);
            g.transform.SetParent(p, false);
            var lr = g.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.startWidth = lr.endWidth = w;
            lr.startColor = lr.endColor = c;
            lr.positionCount = 0;
            return lr;
        }

        public void Mostrar(ElementoDatos el, string combo, RaizEdificio raiz)
        {
            var dc = raiz.demanda_capacidad;
            string grupo = Mapear(el.type);          // "columna"/"muro"
            if (dc == null || grupo == null ||
                !dc.TryGetValue("curva_pm", out var curvaObj)) { Ocultar(); return; }

            // curva
            var curva = curvaObj[grupo];
            if (curva is JArray arr && arr.Count > 1)
            {
                var pts = new Vector3[arr.Count];
                for (int i = 0; i < arr.Count; i++)
                {
                    double M = (double)arr[i]["M_kNm"];
                    double P = (double)arr[i]["P_kN"];
                    pts[i] = Origen + new Vector3((float)(M * S_M),
                                                  (float)(P * S_P), 0f);
                }
                _curva.positionCount = arr.Count;
                _curva.SetPositions(pts);
            }

            // ejes de referencia (P vertical, M horizontal)
            _axes.positionCount = 4;
            _axes.SetPositions(new[]
            {
                Origen, Origen + new Vector3(30f, 0, 0),
                Origen, Origen + new Vector3(0, 12f, 0)
            });

            // punto de demanda de la combinacion activa
            if (dc.TryGetValue("demandas", out var demandas) &&
                demandas[grupo] is JObject dg && dg[combo] != null)
            {
                double P = (double)dg[combo]["P_kN"];
                double M = (double)dg[combo]["M_efectivo_kNm"];
                double dcr = (double)dg[combo]["DCR_M"];
                Vector3 pdem = Origen + new Vector3((float)(M * S_M),
                                                    (float)(P * S_P), 0f);
                float r = 0.45f;
                _punto.positionCount = 4;
                _punto.SetPositions(new[]
                {
                    pdem + new Vector3(-r, -r, 0), pdem + new Vector3(r, r, 0),
                    pdem + new Vector3(r, -r, 0),  pdem + new Vector3(-r, r, 0),
                });
                _rotulo.text =
                    $"{grupo} #{el.elementTag} — P-M [{combo}]\n" +
                    $"Punto demanda: P={P:0} kN · M_ef={M:0} kN·m  →  DCR={dcr:0.00}\n" +
                    $"(capacidad M_nom por la curva P1A3)";
                _rotulo.transform.position = pdem + new Vector3(2f, 0.5f, 0);
                return;
            }
            Ocultar();
        }

        public void Ocultar()
        {
            _curva.positionCount = 0;
            _punto.positionCount = 0;
            _rotulo.text = "";
        }

        static string Mapear(string type) =>
            type == "columna" ? "columna" : type == "muro" ? "muro" : null;
    }
}