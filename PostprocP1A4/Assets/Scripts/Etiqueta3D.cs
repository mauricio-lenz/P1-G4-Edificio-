using UnityEngine;
using UnityEngine.UI;

// ============================================================================
//  Etiqueta 3D legible creada en runtime con uGUI (canvas world-space).
//  Sustituye a TextMesh, que ya NO esta disponible en Unity 6 (6000).
// ============================================================================
namespace EdificioUnity
{
    public class Etiqueta3D : MonoBehaviour
    {
        public Text Txt;
        static int _n;

        public static Etiqueta3D Crear(Transform parent, Vector3 pos,
                                       string texto, float escala = 1f)
        {
            var go = new GameObject("Etiqueta" + (_n++));
            go.transform.SetParent(parent, false);
            var e = go.AddComponent<Etiqueta3D>();

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 200;
            go.transform.localScale = Vector3.one * (0.025f * escala);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(6f, 1.2f);

            var bg = new GameObject("Fondo");
            bg.transform.SetParent(go.transform, false);
            bg.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.45f);
            var bgRt = (RectTransform)bg.transform;
            bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero; bgRt.offsetMax = Vector2.zero;

            var tg = new GameObject("Texto");
            tg.transform.SetParent(go.transform, false);
            e.Txt = tg.AddComponent<Text>();
            e.Txt.font = Font.CreateDynamicFontFromOSFont(
                new[] { "Arial", "Segoe UI", "Liberation Sans" }, 44);
            e.Txt.fontSize = 44;
            e.Txt.color = Color.white;
            e.Txt.alignment = TextAnchor.MiddleCenter;
            e.Txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            e.Txt.verticalOverflow = VerticalWrapMode.Overflow;
            var tRt = (RectTransform)tg.transform;
            tRt.anchorMin = Vector2.zero; tRt.anchorMax = Vector2.one;
            tRt.offsetMin = Vector2.zero; tRt.offsetMax = Vector2.zero;

            e.Mostrar(texto, pos);
            return e;
        }

        public void Mostrar(string texto, Vector3 pos)
        {
            if (Txt != null) Txt.text = texto ?? "";
            transform.position = pos;
        }

        public void Ocultar()
        {
            if (Txt != null) Txt.text = "";
        }
    }
}