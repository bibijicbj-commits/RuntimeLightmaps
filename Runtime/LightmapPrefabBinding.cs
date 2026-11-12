using System.Collections.Generic;
using UnityEngine;

namespace RuntimeLightmaps
{
    public class LightmapPrefabBinding : MonoBehaviour
    {
        private static LightmapBundle s_defaultBundle;
        private static event System.Action<LightmapBundle> s_onDefaultBundleReady;

        public LightmapBundle bundle;
        public bool applyOnAwake = false;
        [SerializeField] private bool runtimeDebugLogs = false;
        [SerializeField] private bool forceLightmapKeywords = false;

        private int[] _remap;
        private bool _applied;
        private readonly Dictionary<string, Renderer> _rendererCache = new Dictionary<string, Renderer>(256);

        private void Awake()
        {
            if (applyOnAwake) Apply();
        }

        private void OnEnable()
        {
            if (bundle == null && s_defaultBundle != null)
            {
                bundle = s_defaultBundle;
                if (applyOnAwake && !_applied) Apply();
            }
            else if (bundle == null)
            {
                s_onDefaultBundleReady += OnDefaultBundleReady;
            }
        }

        private void OnDisable()
        {
            s_onDefaultBundleReady -= OnDefaultBundleReady;
        }

        private void OnDestroy()
        {
            s_onDefaultBundleReady -= OnDefaultBundleReady;
            if (!Application.isPlaying) return;
            if (_applied && bundle != null)
                RuntimeLightmapRegistry.Release(bundle);
        }

        private void OnDefaultBundleReady(LightmapBundle defaultBundle)
        {
            if (bundle != null) return;

            bundle = defaultBundle;
            if (applyOnAwake && !_applied) Apply();
        }

        public static void SetDefaultBundle(LightmapBundle defaultBundle)
        {
            s_defaultBundle = defaultBundle;
            s_onDefaultBundleReady?.Invoke(defaultBundle);
        }

        public bool IsApplied => _applied;

        public void Reapply()
        {
            _applied = false;
            _rendererCache.Clear();
            Apply();
        }

        public void Apply()
        {
            if (_applied) return;
            if (bundle == null)
            {
                Debug.LogWarning($"[{nameof(LightmapPrefabBinding)}] Bundle is null on {name}", this);
                return;
            }

            _remap = RuntimeLightmapRegistry.Acquire(bundle);
            BuildRendererCacheIfNeeded();

            var binds = bundle.bindings;
            if (binds == null || binds.Length == 0)
            {
                Debug.LogWarning($"[{nameof(LightmapPrefabBinding)}] No bindings in bundle {bundle.name}", this);
                _applied = true;
                return;
            }

            int found = 0;
            int missing = 0;
            int applied = 0;
            if (runtimeDebugLogs)
            {
                Debug.Log($"[{nameof(LightmapPrefabBinding)}] Apply '{name}' bundle={bundle.name} bindings={binds.Length} remapCount={_remap?.Length} mode={LightmapSettings.lightmapsMode}", this);
            }

            for (int i = 0; i < binds.Length; i++)
            {
                var b = binds[i];
                if (string.IsNullOrEmpty(b.path)) continue;

                if (!_rendererCache.TryGetValue(b.path, out var r) || r == null)
                {
                    missing++;
                    continue;
                }
                found++;

                int bakedIndex = b.bakedLightmapIndex;
                int runtimeIndex = (bakedIndex >= 0 && bakedIndex < _remap.Length) ? _remap[bakedIndex] : -1;

                if (runtimeIndex >= 0)
                {
                    r.lightmapIndex = runtimeIndex;
                    r.lightmapScaleOffset = b.bakedScaleOffset;
                    applied++;
                }
            }

            _applied = true;

            if (runtimeDebugLogs)
            {
                Debug.Log($"[{nameof(LightmapPrefabBinding)}] Apply result: found={found} missing={missing} applied={applied} lmCount={(LightmapSettings.lightmaps != null ? LightmapSettings.lightmaps.Length : 0)}", this);

                var lmCount = LightmapSettings.lightmaps != null ? LightmapSettings.lightmaps.Length : 0;
                Debug.Log($"[{nameof(LightmapPrefabBinding)}] LightmapsMode={LightmapSettings.lightmapsMode} LightmapsCount={lmCount}", this);

                var anyRenderer = GetComponentInChildren<Renderer>(includeInactive: true);
                if (anyRenderer != null)
                {
                    Debug.Log($"[{nameof(LightmapPrefabBinding)}] SampleRenderer='{anyRenderer.name}' idx={anyRenderer.lightmapIndex} so={anyRenderer.lightmapScaleOffset}", this);
                    if (anyRenderer.sharedMaterial != null)
                    {
                        Debug.Log($"[{nameof(LightmapPrefabBinding)}] SampleRendererShader='{anyRenderer.sharedMaterial.shader.name}'", this);
                    }
                    var mf = anyRenderer.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null)
                    {
                        var uv2Count = mf.sharedMesh.uv2 != null ? mf.sharedMesh.uv2.Length : 0;
                        Debug.Log($"[{nameof(LightmapPrefabBinding)}] Mesh='{mf.sharedMesh.name}' uv2Len={uv2Count}", this);
#if UNITY_EDITOR
                        var meshPath = UnityEditor.AssetDatabase.GetAssetPath(mf.sharedMesh);
                        if (!string.IsNullOrEmpty(meshPath))
                        {
                            Debug.Log($"[{nameof(LightmapPrefabBinding)}] MeshAssetPath='{meshPath}'", this);
                        }
#endif
                    }
                }

                var lms = LightmapSettings.lightmaps;
                if (lms != null && lms.Length > 0 && lms[0] != null)
                {
                    var c = lms[0].lightmapColor;
                    if (c != null)
                    {
                        Debug.Log($"[{nameof(LightmapPrefabBinding)}] Lightmap0='{c.name}' {c.width}x{c.height} fmt={c.format}", this);
                    }
                    var d = lms[0].lightmapDir;
                    Debug.Log($"[{nameof(LightmapPrefabBinding)}] Lightmap0 dir={(d != null ? d.name : "null")}", this);
                }

                Debug.Log($"[{nameof(LightmapPrefabBinding)}] ColorSpace={QualitySettings.activeColorSpace}", this);

                // Optional pixel probe (requires Read/Write enabled on lightmap texture)
                var lmsProbe = LightmapSettings.lightmaps;
                if (lmsProbe != null && lmsProbe.Length > 0 && lmsProbe[0] != null)
                {
                    var c = lmsProbe[0].lightmapColor;
                    if (c != null && c.isReadable)
                    {
                        var p = c.GetPixel(0, 0);
                        Debug.Log($"[{nameof(LightmapPrefabBinding)}] Lightmap0 pixel(0,0)={p}", this);
                    }
                    else if (c != null)
                    {
                        Debug.Log($"[{nameof(LightmapPrefabBinding)}] Lightmap0 not readable (enable Read/Write to sample)", this);
                    }
                }
            }

            if (forceLightmapKeywords)
            {
                ForceLightmapKeywords();
            }

        }

        private void BuildRendererCacheIfNeeded()
        {
            if (_rendererCache.Count > 0) return;

            var renderers = GetComponentsInChildren<Renderer>(includeInactive: true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null) continue;

                string path = GetPathFromRoot(transform, r.transform);
                if (!_rendererCache.ContainsKey(path))
                    _rendererCache.Add(path, r);
            }
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

        private void ForceLightmapKeywords()
        {
            var renderers = GetComponentsInChildren<Renderer>(includeInactive: true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null) continue;
                var mats = r.sharedMaterials;
                if (mats == null) continue;
                for (int m = 0; m < mats.Length; m++)
                {
                    var mat = mats[m];
                    if (mat == null) continue;

                    // Built-in pipeline lightmap keywords
                    mat.EnableKeyword("LIGHTMAP_ON");
                    mat.EnableKeyword("DIRLIGHTMAP_COMBINED");
                }
            }
        }
    }
}
