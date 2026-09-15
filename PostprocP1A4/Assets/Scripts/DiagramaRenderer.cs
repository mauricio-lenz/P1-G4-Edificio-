using UnityEngine;

// ============================================================================
//  Enum de diagramas + barra de magnitud del elemento seleccionado.
//  El mapa de colores global (momento / axial / corte) lo aplica
//  EdificioManager sobre todos los ElementoView (ver ElementoView.Heat).
// ============================================================================
namespace EdificioUnity
{
    public enum TipoDiagrama { Momento, Axial, Corte }

    public class DiagramaRenderer : MonoBehaviour
    {
        LineRenderer _barra;
        Etiqueta3D _et;

        public static DiagramaRenderer Create(Transform parent)
        {
            var go = new GameObject("Diagrama");
            go.transform.SetParent(parent, false);
            var dr = go.AddComponent<DiagramaRenderer>();

            var bar = new GameObject("Barra");
            bar.transform.SetParent(go.transform, false);
            dr._barra = bar.AddComponent<LineRenderer>();
            dr._barra.useWorldSpace = true;
            dr._barra.material = new Material(Shader.Find("Sprites/Default"));
            dr._barra.positionCount = 2;
            dr._barra.startWidth = dr._barra.endWidth = 0.25f;
            dr._barra.enabled = false;

            dr._et = Etiqueta3D.Crear(go.transform, Vector3.zero, "", 1.1f);
            return dr;
        }

        public void Dibujar(ElementoView el, TipoDiagrama tipo, string combo)
        {
            Vector3 a = el.Datos.A;
            Vector3 b = el.Datos.B;
            Vector3 medio = (a + b) * 0.5f;
            Vector3 ex = (b - a).normalized;
            Vector3 dir = Vector3.Cross(ex, el.Datos.AZ.normalized);
            if (dir.sqrMagnitude < 1e-6f) dir = el.Datos.AY.normalized;
            dir.Normalize();

            float mag = el.EscalaDiagrama(tipo);
            Vector3 fin = medio + dir * Mathf.Min(mag, 60f);
            double valor = ValorDe(el, tipo);

            _barra.enabled = true;
            _barra.SetPosition(0, medio);
            _barra.SetPosition(1, fin);
            _barra.startColor = _barra.endColor =
                mag > 1f ? new Color(1f, 0.15f, 0.1f)
                         : new Color(0.85f, 0.8f, 0.7f);

            if (_et != null)
                _et.Mostrar($"{Etiqueta(tipo)} = {valor:0.#} [{combo}]",
                            fin + dir * 2f);
        }

        public void Ocultar()
        {
            _barra.enabled = false;
            if (_et != null) _et.Ocultar();
        }

        static double ValorDe(ElementoView el, TipoDiagrama t)
        {
            switch (t)
            {
                case TipoDiagrama.Momento:
                    return Mathf.Sqrt((float)(el.Valor("My") * el.Valor("My") +
                                              el.Valor("Mz") * el.Valor("Mz")));
                case TipoDiagrama.Axial:
                    return Mathf.Abs((float)el.Valor("N"));
                default:
                    return Mathf.Sqrt((float)(el.Valor("Vy") * el.Valor("Vy") +
                                              el.Valor("Vz") * el.Valor("Vz")));
            }
        }

        static string Etiqueta(TipoDiagrama t) =>
            t == TipoDiagrama.Momento ? "Momento |M|"
            : t == TipoDiagrama.Axial ? "Axial N"
            : "Corte |V|";
    }
}