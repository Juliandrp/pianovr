using System;
using UnityEngine;
using UnityEngine.XR;

namespace PianoVR.Piano
{
    /// <summary>
    /// Calibración en dos toques: con la punta del control derecho toca el borde frontal de la
    /// tecla MÁS GRAVE y aprieta el gatillo; luego el de la MÁS AGUDA y vuelve a apretar.
    /// </summary>
    public class KeyboardCalibrator : MonoBehaviour
    {
        public enum Step { Idle, Lowest, Highest }

        [SerializeField] PianoLayout layout;
        [SerializeField] XRNode controller = XRNode.RightHand;
        [Tooltip("Desde el centro de tracking del control hasta la punta con la que tocas la tecla (local).")]
        [SerializeField] Vector3 tipOffset = new Vector3(0f, -0.02f, 0.05f);
        [SerializeField] float markerSize = 0.012f;

        public Step Current { get; private set; } = Step.Idle;
        public event Action<Step> StepChanged;
        public bool HasController => _device.isValid;

        Transform _marker;
        Vector3 _lowPoint;
        bool _triggerWasDown;
        InputDevice _device;

        public string Instructions
        {
            get
            {
                switch (Current)
                {
                    case Step.Lowest: return "CALIBRAR 1/2\nToca con la punta del control el borde\nfrontal de la tecla MÁS GRAVE (izquierda)\ny aprieta el gatillo.";
                    case Step.Highest: return "CALIBRAR 2/2\nAhora el borde frontal de la tecla\nMÁS AGUDA (derecha) y aprieta el gatillo.";
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
            var r = go.GetComponent<Renderer>();
            r.material.color = new Color(1f, 0.8f, 0.1f);
            _marker = go.transform;
            _marker.gameObject.SetActive(false);
        }

        public void Begin()
        {
            SetStep(Step.Lowest);
        }

        public void Cancel()
        {
            SetStep(Step.Idle);
        }

        void SetStep(Step s)
        {
            Current = s;
            _marker.gameObject.SetActive(s != Step.Idle);
            StepChanged?.Invoke(s);
        }

        void Update()
        {
            if (!_device.isValid) _device = InputDevices.GetDeviceAtXRNode(controller);
            if (Current == Step.Idle) return;

            if (!TryGetTip(out var tip)) return;
            _marker.position = tip;

            _device.TryGetFeatureValue(CommonUsages.triggerButton, out bool trigger);
            bool pressed = trigger && !_triggerWasDown;
            _triggerWasDown = trigger;
            if (!pressed) return;

            if (Current == Step.Lowest)
            {
                _lowPoint = tip;
                SetStep(Step.Highest);
            }
            else if (Current == Step.Highest)
            {
                layout.CalibrateWorld(_lowPoint, tip);
                SetStep(Step.Idle);
            }
        }

        /// <summary>Posición en el mundo de la punta del control.</summary>
        public bool TryGetTip(out Vector3 world)
        {
            world = default;
            if (!_device.isValid) return false;
            if (!_device.TryGetFeatureValue(CommonUsages.devicePosition, out var pos)) return false;
            _device.TryGetFeatureValue(CommonUsages.deviceRotation, out var rot);

            Vector3 local = pos + rot * tipOffset;
            Transform space = layout != null ? layout.trackingSpace : null;
            world = space ? space.TransformPoint(local) : local;
            return true;
        }
    }
}
