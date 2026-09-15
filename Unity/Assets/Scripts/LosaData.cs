using UnityEngine;

namespace EdificioUnity
{
    public class LosaData : MonoBehaviour
    {
        public int nivel;
        public string nombre;
        public float z_m;
        public float area_m2;
        public float espesor_m;
        public float ppLosakNm2;
        public float qGkNm2;

        public void Seleccionar()
        {
            EdificioManager.Current?.SeleccionarLosa(this);
        }
    }
}
