using System.IO;
using Newtonsoft.Json;
using UnityEngine;

// ============================================================================
//  Lee edificio_para_unity.json (generado por exportar_unity.py) y produce el
//  objeto RaizEdificio tipado.
//
//  Requisito: paquete "Newtonsoft Json" (com.unity.nuget.newtonsoft-json),
//  disponible por defecto en Unity 2021+.2 en adelante (Package Manager).
// ============================================================================
namespace EdificioUnity
{
    public static class EdificioJsonLoader
    {
        public const string DefaultPath =
            "Assets/../edificio_para_unity.json";   // raiz del repositorio

        public static RaizEdificio Cargar(string ruta = null)
        {
            string path = ruta ?? DefaultPath;
            if (!File.Exists(path))
            {
                // Fallback: pide el archivo en StreamingAssets
                string alt = Path.Combine(Application.streamingAssetsPath,
                                          "edificio_para_unity.json");
                if (File.Exists(alt)) path = alt;
                else throw new FileNotFoundException(
                    "No se encontro edificio_para_unity.json. Ejecutar " +
                    "exportar_unity.py en el repositorio.", path);
            }
            string json = File.ReadAllText(path);
            var raiz = JsonConvert.DeserializeObject<RaizEdificio>(json);
            Debug.Log($"[EdificioJsonLoader] {raiz.elementos.Count} elementos, " +
                      $"{raiz.nodos.Count} nodos, {raiz.apoyos.Count} apoyos, " +
                      $"{raiz.deformada.Count} deformadas, combos=" +
                      $"{string.Join(",", raiz.combos())}");
            return raiz;
        }
    }

    public static class RaizExt
    {
        public static List<string> combos(this RaizEdificio r)
        {
            if (r._meta == null) return new List<string> {"U3"};
            var c = r._meta["combos"];
            var lista = new List<string>();
            if (c != null) foreach (var k in c) lista.Add((string)k);
            if (lista.Count == 0) lista.Add("U3");
            return lista;
        }
    }
}