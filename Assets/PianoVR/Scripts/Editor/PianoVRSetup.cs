using PianoVR.Audio;
using PianoVR.Game;
using PianoVR.Hands;
using PianoVR.Midi;
using PianoVR.Piano;
using PianoVR.UI;
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
            var existing = Object.FindFirstObjectByType<PianoVRController>();
            if (existing != null)
            {
                if (!EditorUtility.DisplayDialog("PianoVR",
                        "Ya hay un PianoVR en esta escena. ¿Reemplazarlo por la versión nueva?", "Reemplazar", "Cancelar"))
                    return;
                Undo.DestroyObjectImmediate(existing.gameObject);
            }

            var notesMat = CreateMaterial("PianoVR_Notes", transparent: false, lit: false);
            var glassMat = CreateMaterial("PianoVR_Transparent", transparent: true, lit: false);
            var keysMat = CreateMaterial("PianoVR_VirtualKeys", transparent: false, lit: true);

            var root = new GameObject("PianoVR");
            Undo.RegisterCreatedObjectUndo(root, "Agregar PianoVR");

            // Entrada
            var hub = root.AddComponent<MidiInputHub>();
            var android = root.AddComponent<AndroidMidiInput>();
            var pcKeys = root.AddComponent<EditorKeyboardInput>();
            var hands = root.AddComponent<HandKeyPresser>();

            // Piano
            var layout = Child(root, "Piano").AddComponent<PianoLayout>();
            var calibrator = root.AddComponent<KeyboardCalibrator>();
            var virtualKeys = Child(root, "VirtualKeyboard").AddComponent<VirtualKeyboard>();
            var highlighter = Child(root, "KeyHighlights").AddComponent<KeyHighlighter>();

            // Juego
            var player = root.AddComponent<SongPlayer>();
            var score = root.AddComponent<ScoreSystem>();
            var highway = Child(root, "NoteHighway").AddComponent<NoteHighway>();

            // Sonido
            var audioGo = Child(root, "Audio");
            audioGo.AddComponent<AudioSource>();
            var synth = audioGo.AddComponent<PianoSynth>();
            var gameAudio = audioGo.AddComponent<GameAudio>();

            // Interfaz
            var fx = Child(root, "Feedback").AddComponent<FeedbackFx>();
            var hud = Child(root, "Hud").AddComponent<GameHud>();
            var menu = Child(root, "Menu").AddComponent<GameMenu>();
            var controller = root.AddComponent<PianoVRController>();

            Transform tracking = Camera.main != null ? Camera.main.transform.parent : null;
            if (tracking == null)
                Debug.LogWarning("[PianoVR] No encontré la cámara del XR Origin. Asigna 'Tracking Space' en Piano > PianoLayout.");

            Set(android, "hub", hub);
            Set(pcKeys, "hub", hub);
            Set(hands, "layout", layout);
            Set(hands, "hub", hub);

            Set(layout, "trackingSpace", tracking);
            Set(calibrator, "layout", layout);
            Set(virtualKeys, "layout", layout);
            Set(virtualKeys, "hub", hub);
            Set(virtualKeys, "material", keysMat);
            Set(highlighter, "layout", layout);
            Set(highlighter, "player", player);
            Set(highlighter, "hub", hub);
            Set(highlighter, "material", glassMat);

            Set(player, "hub", hub);
            Set(score, "player", player);
            Set(highway, "player", player);
            Set(highway, "score", score);
            Set(highway, "layout", layout);
            Set(highway, "noteMaterial", notesMat);

            Set(gameAudio, "synth", synth);
            Set(gameAudio, "player", player);
            Set(gameAudio, "score", score);
            Set(gameAudio, "hub", hub);

            Set(fx, "score", score);
            Set(fx, "layout", layout);
            Set(fx, "flashMaterial", glassMat);
            Set(hud, "player", player);
            Set(hud, "score", score);
            Set(hud, "layout", layout);
            Set(hud, "material", glassMat);
            Set(menu, "layout", layout);
            Set(menu, "calibrator", calibrator);
            Set(menu, "material", glassMat);

            Set(controller, "player", player);
            Set(controller, "score", score);
            Set(controller, "layout", layout);
            Set(controller, "calibrator", calibrator);
            Set(controller, "hub", hub);
            Set(controller, "handPresser", hands);
            Set(controller, "virtualKeyboard", virtualKeys);
            Set(controller, "gameAudio", gameAudio);
            Set(controller, "menu", menu);

            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);
            Debug.Log("[PianoVR] Listo. Dale Play: aparece un piano frente a la cámara. Toca con A W S E D F..., Espacio = tocar, 8 = cambiar a piano virtual.");
        }

        static GameObject Child(GameObject parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            return go;
        }

        static void Set(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null) { Debug.LogWarning($"[PianoVR] Campo '{field}' no encontrado en {target.GetType().Name}"); return; }
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static Material CreateMaterial(string name, bool transparent, bool lit)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            if (!AssetDatabase.IsValidFolder(MaterialFolder))
                AssetDatabase.CreateFolder("Assets/PianoVR", "Materials");

            bool urp = GraphicsSettings.currentRenderPipeline != null;
            Shader shader = null;
            if (urp) shader = Shader.Find(lit ? "Universal Render Pipeline/Lit" : "Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find(lit ? "Standard" : transparent ? "Unlit/Transparent" : "Unlit/Color");
            if (shader == null) shader = Shader.Find("Standard");

            var mat = new Material(shader) { name = name };

            if (transparent && shader.name.StartsWith("Universal Render Pipeline"))
            {
                mat.SetFloat("_Surface", 1f); // Transparente
                mat.SetFloat("_Blend", 0f);   // Alpha
                mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_ZWrite", 0f);
                mat.SetFloat("_Cull", 0f);    // ambas caras (estrellas, textos de botones)
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = (int)RenderQueue.Transparent;
            }
            if (lit && shader.name.StartsWith("Universal Render Pipeline"))
                mat.SetFloat("_Smoothness", 0.6f);

            AssetDatabase.CreateAsset(mat, path);
            AssetDatabase.SaveAssets();
            return mat;
        }
    }
}
