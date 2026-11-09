using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.Client.Editor
{
    // The Main scene with a camera and the Game object (GameRoot), made from code so nobody wires it by hand. GameRoot
    // would make both itself in an empty scene too; the scene is there so the build has one and Play works at once.
    public static class SceneSetup
    {
        public const string ProjectFolder = "Assets/_Project";
        const string ScenePath = ProjectFolder + "/Scenes/Main.unity";

        [MenuItem("Game/Set Up Main Scene")]
        public static void SetUp()
        {
            // Saving the scene again renumbers its objects even when nothing changed, so an existing scene is kept.
            // Delete the scene file to have it rebuilt.
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null) CreateScene();
            AddToBuild();
            if (!Application.isBatchMode && EditorSceneManager.GetActiveScene().path != ScenePath
                && EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                EditorSceneManager.OpenScene(ScenePath);
            }
            Debug.Log($"Main scene ready at {ScenePath}. Press Play.");
        }

        static void CreateScene()
        {
            EnsureFolder(ProjectFolder, "Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraObject = new GameObject("Main Camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0, 0, -10);
            var camera = cameraObject.GetComponent<Camera>();
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.07f, 0.1f);

            new GameObject(GameRoot.ObjectName, typeof(GameRoot));
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        // First and enabled in the build list. Leaves the list alone when it already is.
        static void AddToBuild()
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.Count > 0 && scenes[0].path == ScenePath && scenes[0].enabled) return;
            scenes.RemoveAll(s => s.path == ScenePath);
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        internal static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
        }
    }
}
