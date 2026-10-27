using UnityEngine;
using UnityEngine.Rendering;

namespace PianoVR.Game
{
    public static class MaterialUtil
    {
        /// <summary>
        /// Copia de un material sin iluminación configurada como transparente (para desvanecer notas).
        /// Funciona con URP/Unlit; con otros shaders intenta "Unlit/Transparent".
        /// </summary>
        public static Material TransparentCopy(Material source)
        {
            if (source == null) return null;
            var m = new Material(source) { name = source.name + " (fade)" };
            if (m.shader != null && m.shader.name.StartsWith("Universal Render Pipeline"))
            {
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)RenderQueue.Transparent;
            }
            else
            {
                var t = Shader.Find("Unlit/Transparent");
                if (t != null) m.shader = t;
            }
            return m;
        }
    }
}
