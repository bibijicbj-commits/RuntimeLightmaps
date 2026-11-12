using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace RuntimeLightmaps.Editor
{
    public static class LightmapBundleExporter
    {
        [MenuItem("Tools/RuntimeLightmaps/Export Bundle From Selected Root")]
        public static void ExportFromSelected()
        {
            if (Selection.activeGameObject == null)
            {
                EditorUtility.DisplayDialog("Export LightmapBundle", "Please select the prefab root GameObject in the baked scene.", "OK");
                return;
            }

            var root = Selection.activeGameObject.transform;

            // 1) Capture scene lightmap table (color/dir)
            var sceneLMs = LightmapSettings.lightmaps;
            if (sceneLMs == null || sceneLMs.Length == 0)
            {
                EditorUtility.DisplayDialog("Export LightmapBundle", "Current scene has no LightmapSettings.lightmaps. Did you bake?", "OK");
                return;
            }

            Texture2D[] colorLMs = new Texture2D[sceneLMs.Length];
            Texture2D[] dirLMs = new Texture2D[sceneLMs.Length];
            for (int i = 0; i < sceneLMs.Length; i++)
            {
                colorLMs[i] = sceneLMs[i]?.lightmapColor;
                dirLMs[i] = sceneLMs[i]?.lightmapDir;
            }

            // 2) Capture renderer bindings under root
            var renderers = root.GetComponentsInChildren<Renderer>(includeInactive: true);
            var bindings = new List<LightmapBundle.RendererBinding>(renderers.Length);

            foreach (var r in renderers)
            {
                if (r == null) continue;
                if (r.lightmapIndex < 0) continue; // not lightmapped

                string path = GetPathFromRoot(root, r.transform);
                bindings.Add(new LightmapBundle.RendererBinding
                {
                    path = path,
                    bakedLightmapIndex = r.lightmapIndex,
                    bakedScaleOffset = r.lightmapScaleOffset
                });
            }

            if (bindings.Count == 0)
            {
                EditorUtility.DisplayDialog("Export LightmapBundle", "No lightmapped renderers found under selected root.", "OK");
                return;
            }

            // 3) Create asset
            string dir = "Assets/RuntimeLightmapBundles";
            if (!AssetDatabase.IsValidFolder(dir))
            {
                AssetDatabase.CreateFolder("Assets", "RuntimeLightmapBundles");
            }

            string defaultName = root.name + "_LightmapBundle.asset";
            string pathOut = EditorUtility.SaveFilePanelInProject(
                "Save LightmapBundle",
                defaultName,
                "asset",
                "Choose location for LightmapBundle asset",
                dir
            );

            if (string.IsNullOrEmpty(pathOut))
                return;

            var bundle = ScriptableObject.CreateInstance<LightmapBundle>();
            bundle.colorLightmaps = colorLMs;
            bundle.lightmapDirs = dirLMs;
            bundle.bindings = bindings.ToArray();

            AssetDatabase.CreateAsset(bundle, pathOut);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("Export LightmapBundle", $"Exported:\n{pathOut}\nBindings: {bundle.bindings.Length}\nLightmaps: {bundle.colorLightmaps.Length}", "OK");
            Selection.activeObject = bundle;
        }

        private static string GetPathFromRoot(Transform root, Transform t)
        {
            if (t == root) return string.Empty;

            var stack = new Stack<string>(16);
            var cur = t;
            while (cur != null && cur != root)
            {
                stack.Push(cur.name);
                cur = cur.parent;
            }
            return string.Join("/", stack);
        }
    }
}
