using System;
using UnityEngine;

namespace PianoVR.Piano
{
    public enum KeyboardSize { Keys49, Keys61, Keys76, Keys88 }

    /// <summary>
    /// Geometría del piano real en el mundo. Se define con dos puntos calibrados: el borde
    /// frontal (centro) de la tecla más grave y el de la más aguda. A partir de ahí calcula
    /// dónde está cada tecla, para alinear las notas que caen y los resaltados.
    /// </summary>
    public class PianoLayout : MonoBehaviour
    {
        [Header("Teclado")]
        [SerializeField] KeyboardSize size = KeyboardSize.Keys61;

        [Header("Medidas físicas (metros)")]
        [Tooltip("Profundidad visible de una tecla blanca.")]
        public float whiteKeyDepth = 0.15f;
        [Tooltip("Profundidad de una tecla negra.")]
        public float blackKeyDepth = 0.095f;
        [Tooltip("Altura de las teclas negras sobre las blancas.")]
        public float blackKeyHeight = 0.012f;

        [Header("Calibración (espacio de tracking)")]
        [Tooltip("Normalmente 'Camera Offset' del XR Origin. Los puntos se guardan relativos a él.")]
        public Transform trackingSpace;

        public event Action Changed;

        public int LowestPitch { get; private set; }
        public int HighestPitch { get; private set; }
        public bool IsCalibrated { get; private set; }
        public float WhiteKeyWidth { get; private set; } = 0.0235f;

        /// <summary>Borde frontal de la tecla más grave, sobre la superficie de las teclas blancas.</summary>
        public Vector3 Origin { get; private set; }
        /// <summary>Hacia las notas agudas.</summary>
        public Vector3 Right { get; private set; } = Vector3.right;
        /// <summary>Hacia el fondo del piano (lejos del usuario).</summary>
        public Vector3 Forward { get; private set; } = Vector3.forward;
        public Vector3 Up => Vector3.up;
        public Quaternion Rotation => Quaternion.LookRotation(Forward, Up);

        public KeyboardSize Size
        {
            get => size;
            set { size = value; ApplySize(); if (IsCalibrated) SetCalibration(_lowLocal, _highLocal, false); }
        }

        Vector3 _lowLocal, _highLocal;
        const string PrefKey = "PianoVR.Calibration";

        void Awake()
        {
            if (trackingSpace == null && Camera.main != null) trackingSpace = Camera.main.transform.parent;
            ApplySize();
            Load();
        }

        void ApplySize()
        {
            switch (size)
            {
                case KeyboardSize.Keys49: LowestPitch = 36; HighestPitch = 84; break;
                case KeyboardSize.Keys61: LowestPitch = 36; HighestPitch = 96; break;
                case KeyboardSize.Keys76: LowestPitch = 28; HighestPitch = 103; break;
                default:                  LowestPitch = 21; HighestPitch = 108; break;
            }
        }

        public int WhiteKeyCount => WhitesBelow(HighestPitch) + 1;

        public static bool IsBlack(int pitch)
        {
            int pc = ((pitch % 12) + 12) % 12;
            return pc == 1 || pc == 3 || pc == 6 || pc == 8 || pc == 10;
        }

        public bool Contains(int pitch) => pitch >= LowestPitch && pitch <= HighestPitch;

        /// <summary>Cantidad de teclas blancas entre la más grave (incluida) y <paramref name="pitch"/> (excluida).</summary>
        public int WhitesBelow(int pitch)
        {
            int count = 0;
            for (int p = LowestPitch; p < pitch; p++) if (!IsBlack(p)) count++;
            return count;
        }

        /// <summary>Distancia lateral (m) desde el centro de la tecla más grave hasta el centro de esta tecla.</summary>
        public float LateralOffset(int pitch)
        {
            int below = WhitesBelow(pitch);
            return (IsBlack(pitch) ? below - 0.5f : below) * WhiteKeyWidth;
        }

        public float KeyWidth(int pitch) => IsBlack(pitch) ? WhiteKeyWidth * 0.55f : WhiteKeyWidth * 0.9f;
        public float KeyDepth(int pitch) => IsBlack(pitch) ? blackKeyDepth : whiteKeyDepth;

        /// <summary>Centro de la superficie superior de la tecla.</summary>
        public Vector3 KeyTopCenter(int pitch)
        {
            bool black = IsBlack(pitch);
            // Las negras empiezan más atrás: su frente está a (blanca - negra) del borde frontal.
            float depthCenter = black ? whiteKeyDepth - blackKeyDepth * 0.5f : whiteKeyDepth * 0.5f;
            return Origin
                   + Right * LateralOffset(pitch)
                   + Forward * depthCenter
                   + Up * (black ? blackKeyHeight : 0f);
        }

        /// <summary>Punto donde "aterrizan" las notas que caen: el fondo de la tecla.</summary>
        public Vector3 LandingPoint(int pitch)
        {
            return Origin + Right * LateralOffset(pitch) + Forward * (whiteKeyDepth * 0.92f)
                   + Up * (IsBlack(pitch) ? blackKeyHeight : 0f);
        }

        /// <summary>Calibra con dos puntos en coordenadas de mundo.</summary>
        public void CalibrateWorld(Vector3 lowestKeyFront, Vector3 highestKeyFront)
        {
            Vector3 lo = trackingSpace ? trackingSpace.InverseTransformPoint(lowestKeyFront) : lowestKeyFront;
            Vector3 hi = trackingSpace ? trackingSpace.InverseTransformPoint(highestKeyFront) : highestKeyFront;
            SetCalibration(lo, hi, true);
        }

        /// <summary>Coloca un piano virtual frente a la cabeza (para probar en el editor sin calibrar).</summary>
        public void PlaceInFrontOf(Transform head, float distance = 0.55f, float belowHead = 0.45f)
        {
            Vector3 fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            float span = (WhiteKeyCount - 1) * 0.0235f;
            Vector3 front = head.position + fwd * distance - Vector3.up * belowHead;
            CalibrateWorld(front - right * span * 0.5f, front + right * span * 0.5f);
        }

        void SetCalibration(Vector3 lowLocal, Vector3 highLocal, bool save)
        {
            _lowLocal = lowLocal;
            _highLocal = highLocal;

            Vector3 lo = trackingSpace ? trackingSpace.TransformPoint(lowLocal) : lowLocal;
            Vector3 hi = trackingSpace ? trackingSpace.TransformPoint(highLocal) : highLocal;

            Vector3 flat = Vector3.ProjectOnPlane(hi - lo, Vector3.up);
            if (flat.sqrMagnitude < 0.01f) { Debug.LogWarning("[PianoVR] Puntos de calibración demasiado cerca."); return; }

            Right = flat.normalized;
            Forward = Vector3.Cross(Right, Vector3.up); // Unity es de mano izquierda: right × up = hacia el fondo
            Origin = new Vector3(lo.x, (lo.y + hi.y) * 0.5f, lo.z); // promedia la altura de ambos puntos
            WhiteKeyWidth = flat.magnitude / Mathf.Max(1, WhiteKeyCount - 1);
            IsCalibrated = true;

            transform.SetPositionAndRotation(Origin, Rotation);

            if (save) Save();
            Changed?.Invoke();
        }

        void Save()
        {
            var d = new CalibrationData { size = (int)size, low = _lowLocal, high = _highLocal };
            PlayerPrefs.SetString(PrefKey, JsonUtility.ToJson(d));
            PlayerPrefs.Save();
        }

        void Load()
        {
            if (!PlayerPrefs.HasKey(PrefKey)) return;
            var d = JsonUtility.FromJson<CalibrationData>(PlayerPrefs.GetString(PrefKey));
            size = (KeyboardSize)d.size;
            ApplySize();
            SetCalibration(d.low, d.high, false);
        }

        public void ClearSaved()
        {
            PlayerPrefs.DeleteKey(PrefKey);
            IsCalibrated = false;
        }

        [Serializable]
        struct CalibrationData { public int size; public Vector3 low; public Vector3 high; }

        void OnDrawGizmos()
        {
            if (!IsCalibrated) return;
            for (int p = LowestPitch; p <= HighestPitch; p++)
            {
                Gizmos.color = IsBlack(p) ? Color.black : Color.white;
                Gizmos.matrix = Matrix4x4.TRS(KeyTopCenter(p), Rotation, Vector3.one);
                Gizmos.DrawWireCube(Vector3.zero, new Vector3(KeyWidth(p), 0.005f, KeyDepth(p)));
            }
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
