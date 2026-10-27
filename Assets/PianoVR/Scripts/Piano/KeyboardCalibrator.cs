using System;
using PianoVR.Hands;
using UnityEngine;
using UnityEngine.XR;

namespace PianoVR.Piano
{
    /// <summary>
    /// Calibración en dos toques. Con control: la punta del control derecho en el borde frontal de la
    /// tecla MÁS GRAVE + gatillo, y luego la MÁS AGUDA + gatillo. Con las manos: deja la punta del índice
    /// derecho quieta en cada punto ~1,5 s. En modo mesa marcas dónde quieres los extremos del piano virtual.
    /// </summary>
    public class KeyboardCalibrator : MonoBehaviour
    {
        public enum Step { Idle, Lowest, Highest }

        [SerializeField] PianoLayout layout;
        [SerializeField] XRNode controller = XRNode.RightHand;
        [Tooltip("Desde el centro de tracking del control hasta la punta con la que tocas la tecla (local).")]
        [SerializeField] Vector3 tipOffset = new Vector3(0f, -0.02f, 0.05f);
        [SerializeField] float markerSize = 0.012f;

        [Header("Con las manos")]
        [Tooltip("Solo si la mano izquierda no se ve: segundos con el índice quieto para marcar.")]
        public float dwellSeconds = 2.5f;
        [Tooltip("Distancia pulgar-índice (m) para contar un pellizco.")]
        public float pinchOn = 0.02f;
        public float pinchOff = 0.04f;
        [Tooltip("Cuánto puede moverse el dedo (m) y seguir contando como quieto.")]
        public float dwellTolerance = 0.012f;

        public Step Current { get; private set; } = Step.Idle;
        public event Action<Step> StepChanged;
        /// <summary>0..1 mientras el dedo está quieto (solo con manos).</summary>
        public float DwellProgress { get; private set; }
        public bool UsingHands { get; private set; }
        /// <summary>True si la calibración actual es de mesa (texto distinto).</summary>
        public bool TableMode { get; set; }
        /// <summary>Mensaje si el último punto no sirvió (por ejemplo, demasiado cerca del primero).</summary>
        public string LastError { get; private set; } = "";
        /// <summary>True si se confirma con pellizco de la mano izquierda.</summary>
        public bool UsingPinch { get; private set; }

        Transform _marker;
        Renderer _markerRenderer;
        Vector3 _lowPoint;
        bool _triggerWasDown;
        InputDevice _device;
        Vector3 _dwellAnchor;
        float _dwellTime;
        float _cooldown;
        bool _pinching;

        public string Instructions
        {
            get
            {
                string how = !UsingHands ? "toca con la punta del control derecho y aprieta el gatillo"
                    : UsingPinch ? "pon la punta del índice DERECHO ahí\ny PELLIZCA con la mano IZQUIERDA (pulgar + índice)"
                    : "deja la punta del índice derecho quieta 2 segundos";
                string where1 = TableMode ? "donde quieres el EXTREMO IZQUIERDO del piano (borde frontal)" : "el borde frontal de la tecla MÁS GRAVE (izquierda)";
                string where2 = TableMode ? "donde quieres el EXTREMO DERECHO" : "el borde frontal de la tecla MÁS AGUDA (derecha)";
                switch (Current)
                {
                    case Step.Lowest: return $"CALIBRAR 1/2\nMarca {where1}:\n{how}.{ErrorLine}";
                    case Step.Highest: return $"CALIBRAR 2/2\nAhora {where2}:\n{how}.{ErrorLine}";
                    default: return "";
                }
            }
        }

        void Awake()
        {
            if (layout == null) layout = FindFirstObjectByType<PianoLayout>();

            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "CalibrationTip";
            Destroy(go.GetComponent<Collider>());
            go.transform.localScale = Vector3.one * markerSize;
            _markerRenderer = go.GetComponent<Renderer>();
            _markerRenderer.material.color = new Color(1f, 0.8f, 0.1f);
            _marker = go.transform;
            _marker.gameObject.SetActive(false);
        }

        string ErrorLine => string.IsNullOrEmpty(LastError) ? "" : $"\n<color=#FF7777>{LastError}</color>";

        public void Begin() { LastError = ""; SetStep(Step.Lowest); }
        public void Cancel() => SetStep(Step.Idle);

        void SetStep(Step s)
        {
            Current = s;
            _marker.gameObject.SetActive(s != Step.Idle);
            _dwellTime = 0f;
            DwellProgress = 0f;
            _cooldown = 0.6f; // evita capturar el segundo punto con el mismo gesto
            StepChanged?.Invoke(s);
        }

        void Update()
        {
            if (!_device.isValid) _device = InputDevices.GetDeviceAtXRNode(controller);
            if (Current == Step.Idle) return;
            _cooldown -= Time.deltaTime;

            if (!TryGetTip(out var tip, out bool fromHand)) return;
            UsingHands = fromHand;
            _marker.position = tip;

            bool capture = false;
            UsingPinch = fromHand && HandInput.TryGetPinchDistance(Side.Left, out float pinch);
            if (UsingPinch)
            {
                // Confirmar con pellizco de la mano izquierda: más deliberado que esperar quieto.
                HandInput.TryGetPinchDistance(Side.Left, out pinch);
                bool now = _pinching ? pinch < pinchOff : pinch < pinchOn;
                capture = now && !_pinching && _cooldown <= 0f;
                _pinching = now;
                _markerRenderer.material.color = now ? new Color(0.2f, 1f, 0.4f) : new Color(1f, 0.8f, 0.1f);
                DwellProgress = 0f;
            }
            else if (fromHand)
            {
                if (Vector3.Distance(tip, _dwellAnchor) > dwellTolerance)
                {
                    _dwellAnchor = tip;
                    _dwellTime = 0f;
                }
                else
                {
                    _dwellTime += Time.deltaTime;
                }
                DwellProgress = Mathf.Clamp01(_dwellTime / dwellSeconds);
                _markerRenderer.material.color = Color.Lerp(new Color(1f, 0.8f, 0.1f), new Color(0.2f, 1f, 0.4f), DwellProgress);
                capture = DwellProgress >= 1f && _cooldown <= 0f;
                if (capture) tip = _dwellAnchor;
            }
            else
            {
                _device.TryGetFeatureValue(CommonUsages.triggerButton, out bool trigger);
                capture = trigger && !_triggerWasDown && _cooldown <= 0f;
                _triggerWasDown = trigger;
            }

            if (!capture) return;

            if (Current == Step.Lowest)
            {
                _lowPoint = tip;
                LastError = "";
                SetStep(Step.Highest);
            }
            else if (Current == Step.Highest)
            {
                float span = Vector3.ProjectOnPlane(tip - _lowPoint, Vector3.up).magnitude;
                if (span < 0.25f || span > 1.6f)
                {
                    LastError = span < 0.25f ? "Ese punto quedó muy cerca del primero. Empecemos de nuevo."
                                             : "Ese punto quedó muy lejos del primero. Empecemos de nuevo.";
                    SetStep(Step.Lowest);
                    return;
                }
                if (Vector3.Dot(tip - _lowPoint, Camera.main != null ? Camera.main.transform.right : Vector3.right) < 0f)
                {
                    LastError = "Marca primero la tecla de la IZQUIERDA (grave) y luego la de la DERECHA.";
                    SetStep(Step.Lowest);
                    return;
                }
                LastError = "";
                // En un piano real se detecta solo cuántas teclas tiene; en la mesa se respeta el tamaño elegido.
                layout.CalibrateWorld(_lowPoint, tip, detectSize: !TableMode);
                SetStep(Step.Idle);
            }
        }

        /// <summary>Punta del control derecho si está rastreado; si no, punta del índice derecho.</summary>
        public bool TryGetTip(out Vector3 world, out bool fromHand)
        {
            fromHand = false;
            if (TryGetControllerTip(out world)) return true;
            fromHand = true;
            return HandInput.TryGetFingerTip(Side.Right, Finger.Index, out world);
        }

        public bool TryGetControllerTip(out Vector3 world) => TryGetControllerTip(_device, out world);

        public bool TryGetControllerTip(InputDevice device, out Vector3 world)
        {
            world = default;
            if (!device.isValid) return false;
            if (device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && !tracked) return false;
            if (!device.TryGetFeatureValue(CommonUsages.devicePosition, out var pos)) return false;
            device.TryGetFeatureValue(CommonUsages.deviceRotation, out var rot);

            Vector3 local = pos + rot * tipOffset;
            Transform space = layout != null ? layout.trackingSpace : null;
            world = space ? space.TransformPoint(local) : local;
            return true;
        }
    }
}
