#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace RLGames.Editor
{
    /// <summary>
    /// Bootstrap: creates <see cref="MainAudioMixerAssetPath"/> via the editor's Create menu, adds child buses,
    /// and exposes attenuation volumes for <see cref="AudioMixerController"/>.
    /// </summary>
    public static class MainAudioMixerBootstrap
    {
        public const string MainAudioMixerAssetPath = "Assets/RLCore/Audio/MainAudioMixer.mixer";

        const string ExposedMaster = "MasterVolume";
        const string ExposedSfx = "SfxVolume";
        const string ExposedMusic = "MusicVolume";
        const string ExposedUi = "UiVolume";

        [MenuItem("RLCore/Audio/Generate Main Audio Mixer")]
        public static void GenerateMainAudioMixerMenu() => GenerateMainAudioMixerInternal();

        /// <summary>For <c>-executeMethod RLGames.Editor.MainAudioMixerBootstrap.GenerateMainAudioMixerBatch</c>.</summary>
        public static void GenerateMainAudioMixerBatch()
        {
            GenerateMainAudioMixerInternal();
            EditorApplication.Exit(0);
        }

        static void GenerateMainAudioMixerInternal()
        {
            if (File.Exists(MainAudioMixerAssetPath))
            {
                Debug.Log($"[MainAudioMixerBootstrap] Exists, skipping: {MainAudioMixerAssetPath}");
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(MainAudioMixerAssetPath)!);

            var before = CollectMixerPaths();
            if (!EditorApplication.ExecuteMenuItem("Assets/Create/Audio Mixer"))
            {
                Debug.LogError("[MainAudioMixerBootstrap] ExecuteMenuItem failed: Assets/Create/Audio Mixer");
                return;
            }

            AssetDatabase.Refresh();
            var created = CollectMixerPaths().Except(before).ToList();
            if (created.Count != 1)
            {
                Debug.LogError($"[MainAudioMixerBootstrap] Expected one new .mixer, got {created.Count}: {string.Join(", ", created)}");
                return;
            }

            var moveErr = AssetDatabase.MoveAsset(created[0], MainAudioMixerAssetPath);
            if (!string.IsNullOrEmpty(moveErr))
            {
                Debug.LogError($"[MainAudioMixerBootstrap] MoveAsset: {moveErr}");
                return;
            }

            AssetDatabase.Refresh();
            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MainAudioMixerAssetPath);
            if (mixer == null)
            {
                Debug.LogError($"[MainAudioMixerBootstrap] Load: {MainAudioMixerAssetPath}");
                return;
            }

            Undo.RecordObject(mixer, "Configure MainAudioMixer");
            ConfigureMixer(mixer);
            EditorUtility.SetDirty(mixer);
            AssetDatabase.SaveAssets();
            Debug.Log($"[MainAudioMixerBootstrap] Ready: {MainAudioMixerAssetPath}");
        }

        static HashSet<string> CollectMixerPaths()
        {
            var set = new HashSet<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:AudioMixer"))
                set.Add(AssetDatabase.GUIDToAssetPath(guid));
            return set;
        }

        static void ConfigureMixer(AudioMixer mixer)
        {
            var so = new SerializedObject(mixer);
            var views = so.FindProperty("m_AudioMixerGroupViews");
            if (views == null || views.arraySize == 0)
            {
                Debug.LogError("[MainAudioMixerBootstrap] m_AudioMixerGroupViews missing.");
                return;
            }

            var view0 = views.GetArrayElementAtIndex(0);

            // Unity 6+: groups list is typically under "groups" on each view.
            var groupsProp = view0.FindPropertyRelative("groups");
            SerializedProperty nameToGuid = null;
            if (groupsProp == null || groupsProp.arraySize == 0)
                nameToGuid = view0.FindPropertyRelative("nameToGuid");

            if (groupsProp != null && groupsProp.arraySize > 0)
                ConfigureFromGroupsArray(mixer, so, groupsProp);
            else if (nameToGuid != null)
                Debug.LogWarning("[MainAudioMixerBootstrap] Unexpected mixer view layout (nameToGuid). Add SFX/Music/UI in the Audio Mixer window, then re-run RLCore/Audio/Generate.");
            else
                Debug.LogError("[MainAudioMixerBootstrap] Could not find group list on mixer view.");
        }

        static void ConfigureFromGroupsArray(AudioMixer mixer, SerializedObject so, SerializedProperty groupsProp)
        {
            SerializedProperty masterGuidProp = null;
            for (var i = 0; i < groupsProp.arraySize; i++)
            {
                var g = groupsProp.GetArrayElementAtIndex(i);
                var name = g.FindPropertyRelative("name")?.stringValue;
                if (name == "Master")
                {
                    masterGuidProp = g.FindPropertyRelative("guid");
                    break;
                }
            }

            if (masterGuidProp == null || string.IsNullOrEmpty(masterGuidProp.stringValue))
            {
                Debug.LogError("[MainAudioMixerBootstrap] Master group entry not found.");
                return;
            }

            EnsureChildGroup(groupsProp, masterGuidProp.stringValue, "SFX");
            EnsureChildGroup(groupsProp, masterGuidProp.stringValue, "Music");
            EnsureChildGroup(groupsProp, masterGuidProp.stringValue, "UI");
            so.ApplyModifiedProperties();
            so.Update();

            var sfx = FindRuntimeGroup(mixer, "SFX");
            var music = FindRuntimeGroup(mixer, "Music");
            var ui = FindRuntimeGroup(mixer, "UI");
            var master = FindRuntimeGroup(mixer, "Master");

            if (master != null)
                ExposeParameter(mixer, master, ExposedMaster);
            if (sfx != null)
                ExposeParameter(mixer, sfx, ExposedSfx);
            if (music != null)
                ExposeParameter(mixer, music, ExposedMusic);
            if (ui != null)
                ExposeParameter(mixer, ui, ExposedUi);
        }

        static void EnsureChildGroup(SerializedProperty groupsProp, string parentGuidN, string childName)
        {
            for (var i = 0; i < groupsProp.arraySize; i++)
            {
                var g = groupsProp.GetArrayElementAtIndex(i);
                if (g.FindPropertyRelative("name")?.stringValue == childName)
                    return;
            }

            var parentN = parentGuidN.Replace("-", "").ToLowerInvariant();
            var insertAt = groupsProp.arraySize;
            for (var i = 0; i < groupsProp.arraySize; i++)
            {
                var g = groupsProp.GetArrayElementAtIndex(i);
                var pg = g.FindPropertyRelative("parentGuid")?.stringValue;
                if (string.IsNullOrEmpty(pg))
                    continue;
                var pgn = pg.Replace("-", "").ToLowerInvariant();
                if (pgn == parentN)
                    insertAt = i + 1;
            }

            var newG = System.Guid.NewGuid().ToString("N");
            groupsProp.InsertArrayElementAtIndex(insertAt);
            var el = groupsProp.GetArrayElementAtIndex(insertAt);
            el.FindPropertyRelative("name").stringValue = childName;
            el.FindPropertyRelative("guid").stringValue = newG;
            el.FindPropertyRelative("parentGuid").stringValue = parentN;
        }

        static AudioMixerGroup FindRuntimeGroup(AudioMixer mixer, string name)
        {
            var all = mixer.FindMatchingGroups(string.Empty);
            if (all == null)
                return null;
            foreach (var g in all)
            {
                if (g != null && g.name == name)
                    return g;
            }
            return null;
        }

        static void ExposeParameter(AudioMixer mixer, AudioMixerGroup group, string exposedName)
        {
            var so = new SerializedObject(mixer);
            var exposed = so.FindProperty("m_ExposedParameters");
            if (exposed == null)
                return;

            var guid = FindAttenuationVolumeGuid(so, group);
            if (string.IsNullOrEmpty(guid))
            {
                Debug.LogWarning($"[MainAudioMixerBootstrap] No Volume guid for '{group.name}'.");
                return;
            }

            for (var i = 0; i < exposed.arraySize; i++)
            {
                var e = exposed.GetArrayElementAtIndex(i);
                var g = e.FindPropertyRelative("guid");
                if (g != null && g.stringValue == guid)
                {
                    e.FindPropertyRelative("name").stringValue = exposedName;
                    so.ApplyModifiedProperties();
                    return;
                }
            }

            exposed.InsertArrayElementAtIndex(exposed.arraySize);
            var ne = exposed.GetArrayElementAtIndex(exposed.arraySize - 1);
            ne.FindPropertyRelative("guid").stringValue = guid;
            ne.FindPropertyRelative("name").stringValue = exposedName;
            so.ApplyModifiedProperties();
        }

        static string FindAttenuationVolumeGuid(SerializedObject mixerSo, AudioMixerGroup group)
        {
            var effects = mixerSo.FindProperty("m_Effects");
            if (effects == null)
                return null;

            for (var i = 0; i < effects.arraySize; i++)
            {
                var eff = effects.GetArrayElementAtIndex(i);
                var groupProp = eff.FindPropertyRelative("m_AudioMixerGroup");
                if (groupProp == null || groupProp.objectReferenceValue != group)
                    continue;

                var paramsProp = eff.FindPropertyRelative("m_Parameters");
                if (paramsProp == null)
                    continue;

                for (var p = 0; p < paramsProp.arraySize; p++)
                {
                    var param = paramsProp.GetArrayElementAtIndex(p);
                    var nameProp = param.FindPropertyRelative("m_ParameterName");
                    if (nameProp != null && nameProp.stringValue == "Volume")
                    {
                        var idProp = param.FindPropertyRelative("m_GUID");
                        return idProp?.stringValue;
                    }
                }
            }

            return null;
        }
    }
}
#endif
