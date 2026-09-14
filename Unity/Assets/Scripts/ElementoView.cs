using System.Collections.Generic;
using UnityEngine;

// ============================================================================
//  Vista grafica de un elemento estructural (columna / muro / viga).
//  - Prisma orientado por los ejes locales del JSON (trazabilidad).
//  - Deformada: desplaza extremos con deformada[combo][nodeTag].
//  - Coloracion por diagramas (momento / axial / corte) y resaltado.
//  name == "H<elementTag>" para rastrear OpenSees <-> Unity.
// ============================================================================
namespace EdificioUnity
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class ElementoView : MonoBehaviour
    {
        public ElementoDatos Datos;
        public int elementTag => Datos.elementTag;

        Vector3 _a0, _b0;
        Dictionary<string, double[]> _off;       // nodoTag -> [dx,dy,dz]
        string _combo = "U3";
        Mesh _mesh;
        Color _base = new Color(0.76f, 0.80f, 0.86f);

        static readonly Color SelColor = new Color(1f, 0.8f, 0.25f);
        static readonly Color Nada = Color.white;   // sentinela

        public static Color Heat(float t) =>
            Color.Lerp(new Color(0.55f, 0.68f, 0.9f),
                       new Color(0.92f, 0.12f, 0.08f), Mathf.Clamp01(t));

        public void Construir(ElementoDatos d,
                              Dictionary<string, Dictionary<string, double[]>> deformada)
        {
            Datos = d;
            _a0 = d.A;
            _b0 = d.B;
            if (deformada != null)
                _off = deformada.TryGetValue(_combo, out var v) ? v : null;

            var r = GetComponent<MeshRenderer>();
            r.sharedMaterial = new Material(Shader.Find("Standard"))
            {
                color = _base,
                enableInstancing = true
            };
            _mesh = new Mesh { name = "H" + d.elementTag };
            Actualizar();
            GetComponent<MeshFilter>().sharedMesh = _mesh;
        }

        // ---------- geometria de prisma entre extremos (deformados o no) -----
        void Actualizar()
        {
            Vector3 a = _a0 + Offset(_combo, Datos.iNode);
            Vector3 b = _b0 + Offset(_combo, Datos.jNode);
            Vector3 ex = (b - a).normalized;
            Vector3 ey = Datos.AY.normalized;
            Vector3 ez = Datos.AZ.normalized;
            float L = Vector3.Distance(a, b);
            float w = Datos.type == "muro" ? 0.2f : Ancho();
            float h = Alto();

            _mesh.vertices = new[]
            {
                a - ey*w*0.5f - ez*h*0.5f,                    // 0
                a + ey*w*0.5f - ez*h*0.5f,                    // 1
                a + ey*w*0.5f + ez*h*0.5f,                    // 2
                a - ey*w*0.5f + ez*h*0.5f,                    // 3
                b - ey*w*0.5f - ez*h*0.5f,                    // 4
                b + ey*w*0.5f - ez*h*0.5f,                    // 5
                b + ey*w*0.5f + ez*h*0.5f,                    // 6
                b - ey*w*0.5f + ez*h*0.5f,                    // 7
            };
            _mesh.triangles = new[]
            {
                4,5,6, 4,6,7,   1,0,3, 1,3,2,
                0,1,5, 0,5,4,   2,3,7, 2,7,6,
                0,4,7, 0,7,3,   1,2,6, 1,6,5,
            };
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
        }

        Vector3 Offset(string combo, int node)
        {
            if (_off == null) return Vector3.zero;
            if (_off.TryGetValue(node.ToString(), out var v) && v.Length >= 3)
                return new Vector3((float)v[0], (float)v[2], (float)v[1]);
            return Vector3.zero;
        }

        public void SetCombo(string combo)
        {
            _combo = combo;
            if (EdificioManager.Current != null)
                _off = EdificioManager.Current.enabledDeformada &&
                    EdificioManager.Current.Raiz.deformada != null
                    ? (EdificioManager.Current.Raiz.deformada.TryGetValue(
                        combo, out var v) ? v : null) : null;
            Actualizar();
        }

        public double Valor(string campo)
        {
            if (Datos.resultados == null ||
                !Datos.resultados.TryGetValue(_combo, out var r)) return 0;
            switch (campo)
            {
                case "N": return r.N;
                case "Vy": return r.Vy;
                case "Vz": return r.Vz;
                case "T": return r.T;
                case "My": return r.My;
                default: return r.Mz;
            }
        }

        public float EscalaDiagrama(DiagramaRenderer.TipoDiagrama tipo)
        {
            switch (tipo)
            {
                case DiagramaRenderer.TipoDiagrama.Momento:
                    return (float)Mathf.Sqrt(
                        (float)(Valor("My") * Valor("My") +
                                Valor("Mz") * Valor("Mz"))) / EdificioManager.MScale;
                case DiagramaRenderer.TipoDiagrama.Axial:
                    return (float)Mathf.Abs((float)Valor("N")) / EdificioManager.NScale;
                default:
                    float v = (float)Mathf.Sqrt(
                        (float)(Valor("Vy") * Valor("Vy") +
                                Valor("Vz") * Valor("Vz")));
                    return v / EdificioManager.VScale;
            }
        }

        // ---------- coloracion y seleccion ----------
        public void Pintar(DiagramaRenderer.TipoDiagrama tipo)
        {
            var target = Heat(EscalaDiagrama(tipo));
            GetComponent<Renderer>().sharedMaterial.color = target;
        }
        public void RestaurarColor() =>
            GetComponent<Renderer>().sharedMaterial.color = _base;

        public void Resaltar(bool on)
        {
            var m = GetComponent<Renderer>().sharedMaterial;
            m.color = on ? SelColor : _base;
        }

        float Ancho()
        {
            var s = EdificioManager.Current.BuscarSeccion(Datos.sectionTag);
            return s != null ? (float)s.b_m : 0.5f;
        }
        float Alto()
        {
            var s = EdificioManager.Current.BuscarSeccion(Datos.sectionTag);
            return s != null ? (float)s.h_m
                : (Datos.type == "viga" ? 0.8f : 0.7f);
        }
    }
}