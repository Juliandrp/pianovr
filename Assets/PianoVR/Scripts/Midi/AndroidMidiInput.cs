using UnityEngine;

namespace PianoVR.Midi
{
    /// <summary>
    /// Recibe notas de un teclado MIDI conectado por USB-C a las Quest (Android MIDI API).
    /// En el editor no hace nada; ahí se usa <see cref="EditorKeyboardInput"/>.
    /// </summary>
    public class AndroidMidiInput : MonoBehaviour
    {
        [SerializeField] MidiInputHub hub;

#if UNITY_ANDROID && !UNITY_EDITOR
        const string BridgeClass = "com.pianovr.midi.MidiBridge";

        class Listener : AndroidJavaProxy
        {
            readonly MidiInputHub _hub;
            public Listener(MidiInputHub hub) : base(BridgeClass + "$Listener") { _hub = hub; }

            // Se llaman desde un hilo de Java: solo encolamos, nada de API de Unity aquí.
            public void onMidi(int status, int data1, int data2) => _hub.Enqueue(status, data1, data2);
            public void onStatus(string message) => _hub.ReportStatus(message);
        }

        Listener _listener;

        void Start()
        {
            if (hub == null) hub = FindFirstObjectByType<MidiInputHub>();
            if (hub == null) { Debug.LogError("[PianoVR] Falta MidiInputHub en la escena."); return; }

            try
            {
                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                using var context = activity.Call<AndroidJavaObject>("getApplicationContext");
                using var bridge = new AndroidJavaClass(BridgeClass);
                _listener = new Listener(hub);
                bool ok = bridge.CallStatic<bool>("start", context, _listener);
                if (!ok) hub.ReportStatus("MIDI no disponible en este dispositivo");
            }
            catch (System.Exception e)
            {
                hub.ReportStatus("Error iniciando MIDI: " + e.Message);
            }
        }

        void OnDestroy()
        {
            try
            {
                using var bridge = new AndroidJavaClass(BridgeClass);
                bridge.CallStatic("stop");
            }
            catch { /* la app se está cerrando */ }
        }
#endif
    }
}
