using System.Collections.Generic;
using UnityEngine;
#if PIANOVR_XR_HANDS
using UnityEngine.XR.Hands;
#endif

namespace PianoVR.Hands
{
    public enum Finger { Thumb = 0, Index = 1, Middle = 2, Ring = 3, Little = 4 }
    public enum Side { Left = 0, Right = 1 }

    /// <summary>
    /// Acceso simple a las puntas de los dedos con el paquete XR Hands (com.unity.xr.hands).
    /// Si el paquete no está instalado todo devuelve "no rastreado" y el resto de la app sigue funcionando.
    /// Las posiciones vienen en el espacio de tracking y se pasan al mundo con <see cref="TrackingSpace"/>.
    /// </summary>
    public static class HandInput
    {
        /// <summary>Normalmente "Camera Offset" del XR Origin (lo asigna PianoLayout).</summary>
        public static Transform TrackingSpace;

        public static bool Available
        {
            get
            {
#if PIANOVR_XR_HANDS
                return Subsystem() != null;
#else
                return false;
#endif
            }
        }

        public static bool IsTracked(Side side)
        {
#if PIANOVR_XR_HANDS
            var s = Subsystem();
            if (s == null) return false;
            return (side == Side.Left ? s.leftHand : s.rightHand).isTracked;
#else
            return false;
#endif
        }

        public static bool TryGetFingerTip(Side side, Finger finger, out Vector3 world)
        {
#if PIANOVR_XR_HANDS
            return TryGetJoint(side, TipId(finger), out world);
#else
            world = default;
            return false;
#endif
        }

        public static bool TryGetPalm(Side side, out Vector3 world, out Vector3 normal)
        {
            normal = Vector3.down;
#if PIANOVR_XR_HANDS
            if (TryGetJointPose(side, XRHandJointID.Palm, out var pose))
            {
                world = ToWorld(pose.position);
                // En XR Hands el "up" de la pose de la palma apunta hacia el dorso de la mano.
                normal = -(TrackingSpace ? TrackingSpace.rotation * pose.up : pose.up);
                return true;
            }
#endif
            world = default;
            return false;
        }

        /// <summary>Distancia entre pulgar e índice (para el gesto de pellizco).</summary>
        public static bool TryGetPinchDistance(Side side, out float distance)
        {
            distance = float.MaxValue;
            if (!TryGetFingerTip(side, Finger.Thumb, out var t)) return false;
            if (!TryGetFingerTip(side, Finger.Index, out var i)) return false;
            distance = Vector3.Distance(t, i);
            return true;
        }

        static Vector3 ToWorld(Vector3 trackingPos) => TrackingSpace ? TrackingSpace.TransformPoint(trackingPos) : trackingPos;

#if PIANOVR_XR_HANDS
        static XRHandSubsystem _cached;
        static readonly List<XRHandSubsystem> _list = new List<XRHandSubsystem>();

        static XRHandSubsystem Subsystem()
        {
            if (_cached != null && _cached.running) return _cached;
            _cached = null;
            _list.Clear();
            SubsystemManager.GetSubsystems(_list);
            foreach (var s in _list)
                if (s.running) { _cached = s; break; }
            return _cached;
        }

        static XRHandJointID TipId(Finger f)
        {
            switch (f)
            {
                case Finger.Thumb: return XRHandJointID.ThumbTip;
                case Finger.Index: return XRHandJointID.IndexTip;
                case Finger.Middle: return XRHandJointID.MiddleTip;
                case Finger.Ring: return XRHandJointID.RingTip;
                default: return XRHandJointID.LittleTip;
            }
        }

        static bool TryGetJointPose(Side side, XRHandJointID id, out Pose pose)
        {
            pose = default;
            var s = Subsystem();
            if (s == null) return false;
            var hand = side == Side.Left ? s.leftHand : s.rightHand;
            if (!hand.isTracked) return false;
            return hand.GetJoint(id).TryGetPose(out pose);
        }

        static bool TryGetJoint(Side side, XRHandJointID id, out Vector3 world)
        {
            if (TryGetJointPose(side, id, out var pose))
            {
                world = ToWorld(pose.position);
                return true;
            }
            world = default;
            return false;
        }
#endif
    }
}
