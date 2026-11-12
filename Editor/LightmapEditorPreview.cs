using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RuntimeLightmaps.Editor
{
    [InitializeOnLoad]
    public static class LightmapEditorPreview
    {
        static bool s_guard;

        static LightmapEditorPreview()
        {
            EditorSceneManager.sceneOpened += OnSceneOpened;
            PrefabStage.prefabStageOpened += OnPrefabStageOpened;
            PrefabStage.prefabStageClosing += OnPrefabStageClosing;
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            EditorApplication.delayCall += () => Refresh(resetRegistry: true);
        }

        static void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            EditorApplication.delayCall += () => Refresh(resetRegistry: true);
        }

        static void OnPrefabStageOpened(PrefabStage stage)
        {
            EditorApplication.delayCall += () => Refresh(resetRegistry: true);
        }

        static void OnPrefabStageClosing(PrefabStage stage)
        {
            EditorApplication.delayCall += () => Refresh(resetRegistry: true);
        }

        static void OnHierarchyChanged()
        {
            if (s_guard || Application.isPlaying) return;
            EditorApplication.delayCall += () => Refresh(resetRegistry: false);
        }

        static bool IsAuthoringBakeScene()
        {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null)
            {
                return false;
            }

            return SceneManager.GetActiveScene().path == "Assets/Scenes/hongbei.unity";
        }

        static void Refresh(bool resetRegistry)
        {
            if (s_guard || Application.isPlaying || IsAuthoringBakeScene()) return;
            s_guard = true;
            try
            {
                if (resetRegistry)
                    RuntimeLightmapRegistry.Reset();

                var all = Object.FindObjectsByType<LightmapPrefabBinding>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                for (int i = 0; i < all.Length; i++)
                {
                    var binding = all[i];
                    if (binding == null || binding.bundle == null) continue;
                    if (resetRegistry || !binding.IsApplied)
                        binding.Reapply();
                }

                var stage = PrefabStageUtility.GetCurrentPrefabStage();
                if (stage != null && stage.prefabContentsRoot != null)
                {
                    var staged = stage.prefabContentsRoot.GetComponentsInChildren<LightmapPrefabBinding>(true);
                    for (int i = 0; i < staged.Length; i++)
                    {
                        var binding = staged[i];
                        if (binding == null || binding.bundle == null) continue;
                        if (resetRegistry || !binding.IsApplied)
                            binding.Reapply();
                    }
                }

                SceneView.RepaintAll();
            }
            finally
            {
                s_guard = false;
            }
        }
    }
}
