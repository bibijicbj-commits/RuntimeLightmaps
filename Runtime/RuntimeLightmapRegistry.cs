using System;
using System.Collections.Generic;
using UnityEngine;

namespace RuntimeLightmaps
{
    /// <summary>
    /// Global registry that merges lightmaps into LightmapSettings.lightmaps
    /// and provides bakedIndex -> runtimeIndex remapping for bundles.
    /// Only handles color lightmaps (baked indirect).
    /// </summary>
    public static class RuntimeLightmapRegistry
    {
        private struct Key : IEquatable<Key>
        {
            public int colorId;
            public int dirId;

            public bool Equals(Key other) => colorId == other.colorId && dirId == other.dirId;
            public override bool Equals(object obj) => obj is Key other && Equals(other);
            public override int GetHashCode()
            {
                unchecked
                {
                    int h = 17;
                    h = h * 31 + colorId;
                    h = h * 31 + dirId;
                    return h;
                }
            }
        }

        private class Entry
        {
            public Texture2D color;
            public Texture2D dir;
            public int runtimeIndex;      // index in LightmapSettings.lightmaps[]
            public int refCount;
        }

        // Map (color/dir/mask) -> entry
        private static readonly Dictionary<Key, Entry> s_entries = new Dictionary<Key, Entry>();

        // runtime lightmaps table cache (we never reorder; allow holes if needed)
        private static readonly List<LightmapData> s_runtimeTable = new List<LightmapData>();
        private static bool s_inited;

        public static void Reset()
        {
            s_entries.Clear();
            s_runtimeTable.Clear();
            s_inited = false;
        }

        /// <summary>
        /// Ensure cache reflects current LightmapSettings.lightmaps (in case scene has pre-existing ones).
        /// Call once before first Acquire if your scene already has baked lightmaps.
        /// </summary>
        public static void InitializeFromSceneIfNeeded()
        {
            if (s_inited) return;
            s_inited = true;

            s_runtimeTable.Clear();
            s_entries.Clear();

            var existing = LightmapSettings.lightmaps;
            if (existing != null && existing.Length > 0)
            {
                for (int i = 0; i < existing.Length; i++)
                {
                    var lm = existing[i];
                    s_runtimeTable.Add(lm);

                    // track existing lightmaps (color/dir/mask)
                    if (lm != null && (lm.lightmapColor != null || lm.lightmapDir != null))
                    {
                        var key = new Key
                        {
                            colorId = lm.lightmapColor ? lm.lightmapColor.GetInstanceID() : 0,
                            dirId = lm.lightmapDir ? lm.lightmapDir.GetInstanceID() : 0
                        };
                        if (!s_entries.ContainsKey(key))
                        {
                            s_entries[key] = new Entry
                            {
                                color = lm.lightmapColor,
                                dir = lm.lightmapDir,
                                runtimeIndex = i,
                                refCount = 0 // scene-owned, we don't auto-release
                            };
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Acquire bundle textures into LightmapSettings and return remap table bakedIndex->runtimeIndex.
        /// </summary>
        public static int[] Acquire(LightmapBundle bundle)
        {
            if (bundle == null) throw new ArgumentNullException(nameof(bundle));
            InitializeFromSceneIfNeeded();

            var src = bundle.colorLightmaps;
            if (src == null) src = Array.Empty<Texture2D>();
            var srcDir = bundle.lightmapDirs;

            // remap length equals baked lightmap count
            int[] remap = new int[src.Length];

            for (int bakedIndex = 0; bakedIndex < src.Length; bakedIndex++)
            {
                var color = src[bakedIndex];
                if (color == null)
                {
                    remap[bakedIndex] = -1;
                    continue;
                }

                var dir = (srcDir != null && bakedIndex < srcDir.Length) ? srcDir[bakedIndex] : null;

                var key = new Key
                {
                    colorId = color ? color.GetInstanceID() : 0,
                    dirId = dir ? dir.GetInstanceID() : 0
                };

                if (s_entries.TryGetValue(key, out var entry))
                {
                    entry.refCount++;
                    remap[bakedIndex] = entry.runtimeIndex;
                }
                else
                {
                    // Append a new LightmapData entry (color/dir/shadowmask).
                    var lmData = new LightmapData
                    {
                        lightmapColor = color,
                        lightmapDir = dir
                    };

                    int runtimeIndex = AppendToGlobalTable(lmData);

                    entry = new Entry
                    {
                        color = color,
                        dir = dir,
                        runtimeIndex = runtimeIndex,
                        refCount = 1
                    };
                    s_entries[key] = entry;
                    remap[bakedIndex] = runtimeIndex;
                }
            }

            return remap;
        }

        /// <summary>
        /// Release bundle references. Does not reorder or shrink the global table (safe for multiple prefabs).
        /// </summary>
        public static void Release(LightmapBundle bundle)
        {
            if (bundle == null) return;
            InitializeFromSceneIfNeeded();

            var src = bundle.colorLightmaps;
            if (src == null) return;
            var srcDir = bundle.lightmapDirs;

            for (int i = 0; i < src.Length; i++)
            {
                var color = src[i];
                if (color == null) continue;

                var dir = (srcDir != null && i < srcDir.Length) ? srcDir[i] : null;

                var key = new Key
                {
                    colorId = color ? color.GetInstanceID() : 0,
                    dirId = dir ? dir.GetInstanceID() : 0
                };

                if (!s_entries.TryGetValue(key, out var entry)) continue;

                // Scene-owned entries have refCount=0, we leave them.
                if (entry.refCount <= 0) continue;

                entry.refCount--;
                if (entry.refCount <= 0)
                {
                    // Do NOT reorder global table. Optionally clear the slot's color to free GPU mem?
                    // In WebGL, clearing may not immediately free, but it can reduce references.
                    // We'll clear color only if slot is not scene-owned (refCount was managed by us).
                    int idx = entry.runtimeIndex;
                    if (idx >= 0 && idx < s_runtimeTable.Count)
                    {
                        var lm = s_runtimeTable[idx];
                        if (lm != null && lm.lightmapColor == color && lm.lightmapDir == dir)
                        {
                            lm.lightmapColor = null; // leave a hole
                            lm.lightmapDir = null;
                            s_runtimeTable[idx] = lm;
                            CommitGlobalTable();
                        }
                    }
                    s_entries.Remove(key);
                }
            }
        }

        private static int AppendToGlobalTable(LightmapData lmData)
        {
            // Find a hole first (optional optimization). We keep it simple and append.
            // If you want to reuse holes, scan for lightmapColor==null.
            int hole = FindHoleIndex();
            if (hole >= 0)
            {
                s_runtimeTable[hole] = lmData;
                CommitGlobalTable();
                return hole;
            }

            s_runtimeTable.Add(lmData);
            CommitGlobalTable();
            return s_runtimeTable.Count - 1;
        }

        private static int FindHoleIndex()
        {
            for (int i = 0; i < s_runtimeTable.Count; i++)
            {
                var lm = s_runtimeTable[i];
                if (lm == null) return i;
                if (lm.lightmapColor == null) return i;
            }
            return -1;
        }

        private static void CommitGlobalTable()
        {
            LightmapSettings.lightmaps = s_runtimeTable.ToArray();
            UpdateLightmapsMode();
        }

        private static void UpdateLightmapsMode()
        {
            bool hasDir = false;
            for (int i = 0; i < s_runtimeTable.Count; i++)
            {
                var lm = s_runtimeTable[i];
                if (lm == null) continue;
                if (lm.lightmapDir != null) hasDir = true;
            }

            if (hasDir)
                LightmapSettings.lightmapsMode = LightmapsMode.CombinedDirectional;
            else
                LightmapSettings.lightmapsMode = LightmapsMode.NonDirectional;
        }
    }
}
