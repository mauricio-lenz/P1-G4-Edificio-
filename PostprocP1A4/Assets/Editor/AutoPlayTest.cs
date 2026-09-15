using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// ============================================================================
//  Utilidad de verificación para batchmode: crea Assets/Scenes/Main.unity,
//  agrega el EdificioManager, entra en Play y sale SOLO (a los ~8 s).
//  Permite comprobar que el postprocesador arranca sin excepciones.
//  Se ejecuta con: Unity -batchmode -executeMethod
//  EdificioUnity.AutoPlayTest.Run
// ============================================================================
namespace EdificioUnity
{
    public static class AutoPlayTest
    {
        static double _inicio;
        static bool _entradoPlay;

        [MenuItem("Tools/AutoPlayTest")]
        public static void Run()
        {
            var escena = EditorSceneManager.NewScene(
                NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            new GameObject("Edificio").AddComponent<EdificioManager>();
            System.IO.Directory.CreateDirectory(
                System.IO.Path.Combine(Application.dataPath, "Scenes"));
            EditorSceneManager.SaveScene(escena, "Assets/Scenes/Main.unity");
            _inicio = EditorApplication.timeSinceStartup;
            EditorApplication.update += Chequear;
            EditorApplication.isPlaying = true;
        }

        static void Chequear()
        {
            double t = EditorApplication.timeSinceStartup - _inicio;
            if (!EditorApplication.isPlaying && t > 1.5)
                EditorApplication.isPlaying = true;   // reintenta entrada a play
            if (EditorApplication.isPlaying) _entradoPlay = true;
            if (t > 8.0)
            {
                EditorApplication.update -= Chequear;
                EditorApplication.isPlaying = false;
                Debug.Log("[AutoPlayTest] fin: play=" + _entradoPlay +
                          " (" + t.ToString("0.0") + " s)");
                EditorApplication.Exit(0);
            }
        }
    }
}