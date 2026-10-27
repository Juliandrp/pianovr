using System;
using System.Collections.Generic;
using PianoVR.Hands;
using PianoVR.Piano;
using UnityEngine;
using UnityEngine.XR;

namespace PianoVR.UI
{
    /// <summary>
    /// Botones flotantes que se presionan con la punta del dedo o del control (no hace falta apuntar).
    /// Se colocan en una columna a la izquierda del piano. El controlador del juego define los botones.
    /// </summary>
    public class GameMenu : MonoBehaviour
    {
        [SerializeField] PianoLayout layout;
        [SerializeField] KeyboardCalibrator calibrator;
        [SerializeField] Material material;

        public float buttonWidth = 0.11f;
        public float buttonHeight = 0.028f;
        public float gap = 0.006f;
        [Tooltip("Inclinación hacia atrás de la columna (grados).")]
        public float tilt = 25f;

        public Color idle = new Color(0.12f, 0.13f, 0.18f, 0.9f);
        public Color highlight = new Color(0.25f, 0.45f, 0.85f, 0.95f);
        public Color pressedColor = new Color(0.4f, 0.9f, 1f, 1f);

        class Button
        {
            public Func<string> label;
            public Action onPress;
            public Func<bool> visible;
            public Func<bool> highlighted;
            public int row, col, cols;
            public Transform t;
            public Renderer r;
            public TextMesh text;
            public float pressedAt = -10f;
            public bool[] wasInside = new bool[4];
        }

        readonly List<Button> _buttons = new List<Button>();
        Transform _root;
        Transform _head;
        Font _font;
        MaterialPropertyBlock _mpb;
        readonly Vector3[] _pointers = new Vector3[4];
        readonly bool[] _pointerValid = new bool[4];
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        void Awake()
        {
            _root = new GameObject("MenuRoot").transform;
            _root.SetParent(transform, false);
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _mpb = new MaterialPropertyBlock();
        }

        void Start()
        {
            if (layout == null) layout = FindFirstObjectByType<PianoLayout>();
            if (calibrator == null) calibrator = FindFirstObjectByType<KeyboardCalibrator>();
            _head = Camera.main != null ? Camera.main.transform : transform;
        }

        /// <param name="row">fila (0 = arriba)</param>
        /// <param name="col">columna dentro de la fila</param>
        /// <param name="cols">cuántas columnas tiene la fila (1 = botón ancho)</param>
        public void Add(int row, int col, int cols, Func<string> label, Action onPress,
                        Func<bool> visible = null, Func<bool> highlighted = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Button";
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(_root, false);
            var r = go.GetComponent<Renderer>();
            if (material) r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var textGo = new GameObject("Label");
            textGo.transform.SetParent(_root, false);
            var tm = textGo.AddComponent<TextMesh>();
            tm.font = _font;
            textGo.GetComponent<MeshRenderer>().sharedMaterial = _font.material;
            tm.fontSize = 60;
            tm.characterSize = 0.0024f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = Color.white;

            _buttons.Add(new Button
            {
                label = label, onPress = onPress, visible = visible, highlighted = highlighted,
                row = row, col = col, cols = Mathf.Max(1, cols), t = go.transform, r = r, text = tm
            });
        }

        void LateUpdate()
        {
            bool calibrating = calibrator != null && calibrator.Current != KeyboardCalibrator.Step.Idle;
            _root.gameObject.SetActive(!calibrating);
            if (calibrating) return;

            PlaceRoot();
            Layout();
            GatherPointers();

            foreach (var b in _buttons)
            {
                bool vis = b.visible == null || b.visible();
                b.t.gameObject.SetActive(vis);
                b.text.gameObject.SetActive(vis);
                if (!vis) { Array.Clear(b.wasInside, 0, b.wasInside.Length); continue; }

                b.text.text = b.label();

                bool hover = false;
                for (int i = 0; i < _pointers.Length; i++)
                {
                    bool inside = _pointerValid[i] && Inside(b, _pointers[i], out bool near);
                    hover |= inside;
                    if (inside && !b.wasInside[i] && Time.time - b.pressedAt > 0.35f)
                    {
                        b.pressedAt = Time.time;
                        b.onPress?.Invoke();
                    }
                    b.wasInside[i] = inside;
                }

                float sincePress = Time.time - b.pressedAt;
                Color c = sincePress < 0.2f ? pressedColor
                    : (b.highlighted != null && b.highlighted()) ? highlight
                    : idle;
                if (hover) c = Color.Lerp(c, Color.white, 0.25f);
                Paint(b.r, c);

                // El botón se "hunde" un poco al presionarlo.
                float push = sincePress < 0.2f ? 0.006f : 0f;
                var lp = b.t.localPosition;
                b.t.localPosition = new Vector3(lp.x, lp.y, push);
                b.text.transform.localPosition = new Vector3(lp.x, lp.y, push - 0.0055f);
            }
        }

        void Layout()
        {
            // Calcula filas visibles para que no queden huecos.
            var rowIndex = new SortedDictionary<int, int>();
            foreach (var b in _buttons)
                if (b.visible == null || b.visible()) rowIndex[b.row] = 0;
            int k = 0;
            var keys = new List<int>(rowIndex.Keys);
            foreach (var row in keys) rowIndex[row] = k++;

            foreach (var b in _buttons)
            {
                if (!rowIndex.TryGetValue(b.row, out int visualRow)) continue;
                float w = (buttonWidth - gap * (b.cols - 1)) / b.cols;
                float x = -buttonWidth * 0.5f + w * 0.5f + b.col * (w + gap);
                float y = -visualRow * (buttonHeight + gap);
                b.t.localPosition = new Vector3(x, y, b.t.localPosition.z);
                b.t.localScale = new Vector3(w, buttonHeight, 0.01f);
                b.text.transform.localPosition = new Vector3(x, y, -0.0055f);
                b.text.characterSize = b.cols > 1 ? 0.002f : 0.0024f;
            }
        }

        /// <summary>True mientras el menú está sobre la palma izquierda.</summary>
        public bool OnPalm { get; private set; }

        bool _placedOnce;

        /// <summary>Alto total de la columna de botones visibles.</summary>
        float MenuHeight()
        {
            var rows = new HashSet<int>();
            foreach (var b in _buttons) if (b.visible == null || b.visible()) rows.Add(b.row);
            return Mathf.Max(1, rows.Count) * (buttonHeight + gap);
        }

        void PlaceRoot()
        {
            float h = MenuHeight();
            Vector3 targetPos;
            Quaternion targetRot;

            // 1) Menú en la palma: voltea la palma izquierda hacia ti.
            bool palm = HandInput.TryGetPalm(Side.Left, out var palmPos, out var palmNormal);
            if (palm)
            {
                float facing = Vector3.Dot(palmNormal, (_head.position - palmPos).normalized);
                OnPalm = OnPalm ? facing > 0.35f : facing > 0.6f;
            }
            else OnPalm = false;

            if (OnPalm)
            {
                // La columna queda justo encima de la mano, mirando hacia ti.
                targetPos = palmPos + palmNormal * 0.04f + Vector3.up * (h + 0.05f);
                Vector3 toMenu = targetPos - _head.position;
                targetRot = Quaternion.LookRotation(Vector3.ProjectOnPlane(toMenu, Vector3.up).normalized, Vector3.up);
                _root.SetPositionAndRotation(targetPos, targetRot); // pegado a la mano, sin suavizado
                _placedOnce = true;
                return;
            }

            Vector3 fwd = Vector3.ProjectOnPlane(_head.forward, Vector3.up).normalized;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;

            if (layout.IsCalibrated)
            {
                // 2) A la izquierda del piano, girado hacia tu cabeza para leerlo cómodo.
                targetPos = layout.Origin - layout.Right * (buttonWidth * 0.5f + 0.07f)
                            + layout.Up * (h + 0.06f) - layout.Forward * 0.02f;
                Vector3 look = Vector3.ProjectOnPlane(targetPos - _head.position, Vector3.up).normalized;
                targetRot = Quaternion.LookRotation(look, Vector3.up) * Quaternion.Euler(tilt * 0.5f, 0f, 0f);
            }
            else
            {
                // 3) Sin piano calibrado: flota delante, pero solo se mueve si te giras mucho (no te persigue).
                Vector3 desired = _head.position + fwd * 0.5f + Vector3.up * (h * 0.5f - 0.2f);
                Vector3 current = _root.position;
                Vector3 toCurrent = Vector3.ProjectOnPlane(current - _head.position, Vector3.up);
                float angle = Vector3.Angle(fwd, toCurrent);
                float dist = toCurrent.magnitude;
                bool needsMove = !_placedOnce || angle > 35f || dist < 0.25f || dist > 0.9f;
                targetPos = needsMove ? desired : current;
                targetRot = Quaternion.LookRotation(Vector3.ProjectOnPlane(targetPos - _head.position, Vector3.up).normalized, Vector3.up);
            }

            if (!_placedOnce)
            {
                _root.SetPositionAndRotation(targetPos, targetRot);
                _placedOnce = true;
            }
            else
            {
                float k = 1f - Mathf.Exp(-6f * Time.deltaTime);
                _root.SetPositionAndRotation(Vector3.Lerp(_root.position, targetPos, k), Quaternion.Slerp(_root.rotation, targetRot, k));
            }
        }

        void GatherPointers()
        {
            // Con el menú en la palma izquierda, solo se toca con la mano derecha.
            _pointerValid[0] = !OnPalm && HandInput.TryGetFingerTip(Side.Left, Finger.Index, out _pointers[0]);
            _pointerValid[1] = HandInput.TryGetFingerTip(Side.Right, Finger.Index, out _pointers[1]);
            if (calibrator != null)
            {
                _pointerValid[2] = calibrator.TryGetControllerTip(InputDevices.GetDeviceAtXRNode(XRNode.LeftHand), out _pointers[2]);
                _pointerValid[3] = calibrator.TryGetControllerTip(InputDevices.GetDeviceAtXRNode(XRNode.RightHand), out _pointers[3]);
            }
        }

        bool Inside(Button b, Vector3 world, out bool near)
        {
            Vector3 local = _root.InverseTransformPoint(world) - new Vector3(b.t.localPosition.x, b.t.localPosition.y, 0f);
            float hw = b.t.localScale.x * 0.5f, hh = b.t.localScale.y * 0.5f;
            near = Mathf.Abs(local.x) < hw + 0.02f && Mathf.Abs(local.y) < hh + 0.02f && local.z > -0.05f && local.z < 0.03f;
            return Mathf.Abs(local.x) < hw && Mathf.Abs(local.y) < hh && local.z > -0.012f && local.z < 0.02f;
        }

        void Paint(Renderer r, Color c)
        {
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColor, c);
            _mpb.SetColor(ColorId, c);
            r.SetPropertyBlock(_mpb);
        }
    }
}
