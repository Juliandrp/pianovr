using PianoVR.Game;
using PianoVR.Midi;
using PianoVR.Piano;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace PianoVR.EditorTools
{
    /// <summary>Menú "PianoVR" para armar la escena en un clic.</summary>
    public static class PianoVRSetup
    {
        const string MaterialFolder = "Assets/PianoVR/Materials";

        [MenuItem("PianoVR/Agregar PianoVR a la escena")]
        public static void AddToScene()
        {
            if (Object.FindFirstObjectByType<PianoVRController>() != null)
            {
                EditorUtility.DisplayDialog("PianoVR", "Ya hay un PianoVR en esta escena.", "OK");
                return;
            }

            var noteMat = CreateMaterial("PianoVR_Notes", transparent: false);
            var keyMat = CreateMaterial("PianoVR_KeyHighlight", transparent: true);

            var root = new GameObject("PianoVR");
            Undo.RegisterCreatedObjectUndo(root, "Agregar PianoVR");

            var hub = root.AddComponent<MidiInputHub>();
            var android = root.AddComponent<AndroidMidiInput>();
            var pcKeys = root.AddComponent<EditorKeyboardInput>();

            var pianoGo = new GameObject("Piano");
            pianoGo.transform.SetParent(root.transform, false);
            var layout = pianoGo.AddComponent<PianoLayout>();
            var calibrator = root.AddComponent<KeyboardCalibrator>();

            var player = root.AddComponent<SongPlayer>();

            var highwayGo = new GameObject("NoteHighway");
            highwayGo.transform.SetParent(root.transform, false);
            var highway = highwayGo.AddComponent<NoteHighway>();

            var highlightGo = new GameObject("KeyHighlights");
            highlightGo.transform.SetParent(root.transform, false);
            var highlighter = highlightGo.AddComponent<KeyHighlighter>();

            var controller = root.AddComponent<PianoVRController>();

            Transform tracking = Camera.main != null ? Camera.main.transform.parent : null;
            if (tracking == null)
                Debug.LogWarning("[PianoVR] No encontré la cámara del XR Origin. Asigna 'Tracking Space' en Piano > PianoLayout.");

            Set(android, "hub", hub);
            Set(pcKeys, "hub", hub);
            Set(layout, "trackingSpace", tracking);
            Set(calibrator, "layout", layout);
            Set(player, "hub", hub);
            Set(highway, "player", player);
            Set(highway, "layout", layout);
            Set(highway, "noteMaterial", noteMat);
            Set(highlighter, "layout", layout);
            Set(highlighter, "player", player);
            Set(highlighter, "hub", hub);
            Set(highlighter, "material", keyMat);
            Set(controller, "player", player);
            Set(controller, "layout", layout);
            Set(controller, "calibrator", calibrator);
            Set(controller, "hub", hub);

            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);
            Debug.Log("[PianoVR] Listo. Dale Play: en el editor se crea un piano virtual frente a la cámara y puedes tocar con A W S E D F...");
        }

        static void Set(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null) { Debug.LogWarning($"[PianoVR] Campo '{field}' no encontrado en {target.GetType().Name}"); return; }
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static Material CreateMaterial(string name, bool transparent)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            if (!AssetDatabase.IsValidFolder(MaterialFolder))
                AssetDatabase.CreateFolder("Assets/PianoVR", "Materials");

            bool urp = GraphicsSettings.currentRenderPipeline != null;
            Shader shader = urp ? Shader.Find("Universal Render Pipeline/Unlit") : null;
            if (shader == null) shader = Shader.Find(transparent ? "Unlit/Transparent" : "Unlit/Color");
            if (shader == null) shader = Shader.Find("Standard");

            var mat = new Material(shader) { name = name };

            if (transparent && shader.name == "Universal Render Pipeline/Unlit")
            {
                mat.SetFloat("_Surface", 1f); // Transparente
                mat.SetFloat("_Blend", 0f);   // Alpha
                mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_ZWrite", 0f);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = (int)RenderQueue.Transparent;
            }

            AssetDatabase.CreateAsset(mat, path);
            AssetDatabase.SaveAssets();
            return mat;
        }
    }
}
