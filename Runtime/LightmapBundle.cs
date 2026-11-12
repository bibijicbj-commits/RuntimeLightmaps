using System;
using UnityEngine;

namespace RuntimeLightmaps
{
    [CreateAssetMenu(menuName = "RuntimeLightmaps/Lightmap Bundle", fileName = "LightmapBundle")]
    public class LightmapBundle : ScriptableObject
    {
        [Tooltip("Color lightmaps. Order must match baked scene LightmapSettings.lightmaps[].")]
        public Texture2D[] colorLightmaps;

        [Tooltip("Directional lightmaps (optional). Must match baked scene LightmapSettings.lightmaps[].")]
        public Texture2D[] lightmapDirs;

        [Tooltip("Renderer binding list captured from baked art scene.")]
        public RendererBinding[] bindings;

        [Serializable]
        public struct RendererBinding
        {
            [Tooltip("Transform path from prefab root, e.g. 'Room/Wall_A'")]
            public string path;

            [Tooltip("Lightmap index in baked art scene LightmapSettings.lightmaps[].")]
            public int bakedLightmapIndex;

            [Tooltip("Renderer.lightmapScaleOffset in baked art scene.")]
            public Vector4 bakedScaleOffset;
        }
    }
}
