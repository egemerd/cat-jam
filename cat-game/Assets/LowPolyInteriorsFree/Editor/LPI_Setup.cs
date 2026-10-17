// Low Poly Interiors - one-click setup.
//
// Materials bind automatically on import (each FBX is set to find an
// existing material by name, searching up), so this tool exists for the two
// things Unity cannot do from a .meta: turning every imported model into a
// prefab, and assembling a demo scene.
//
//   Tools > Low Poly Interiors > Build Prefabs
//   Tools > Low Poly Interiors > Build Demo Scene
//
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LowPolyInteriors
{
    public static class LPI_Setup
    {
        const string Root      = "Assets/LowPolyInteriorsFree";
        const string ModelsDir = Root + "/Models";
        const string PrefabDir = Root + "/Prefabs";
        const string SceneDir  = Root + "/Scenes";
        const string RoomsDir      = Root + "/Demo/Rooms";
        const string RoomPrefabDir = PrefabDir + "/Rooms";

        [MenuItem("Tools/Low Poly Interiors/Build Prefabs", false, 10)]
        public static void BuildPrefabs()
        {
            // TWO ROOTS. Models/ mirrors into Prefabs/, and the assembled
            // rooms and flats under Demo/Rooms get Prefabs/Rooms - kept
            // apart so the library folders still hold only library pieces.
            //
            // Demo/Rooms used to be skipped entirely, so all sixteen rooms
            // and layouts shipped with no prefab and Unity's validator
            // reported them under "Check Mesh Prefabs: each mesh should
            // have a corresponding prefab".
            var jobs = new List<KeyValuePair<string, string>>();
            foreach (var pair in new[] { new[] { ModelsDir, PrefabDir },
                                         new[] { RoomsDir,  RoomPrefabDir } })
            {
                if (!AssetDatabase.IsValidFolder(pair[0])) continue;
                foreach (var g in AssetDatabase.FindAssets("t:Model", new[] { pair[0] }))
                {
                    var ap  = AssetDatabase.GUIDToAssetPath(g);
                    var sb  = Path.GetDirectoryName(
                        ap.Substring(pair[0].Length + 1)).Replace("\\", "/");
                    jobs.Add(new KeyValuePair<string, string>(
                        ap, string.IsNullOrEmpty(sb) ? pair[1] : pair[1] + "/" + sb));
                }
            }
            if (jobs.Count == 0)
            {
                Debug.LogWarning("[LPI] No models found under " + ModelsDir);
                return;
            }

            EnsureFolder(PrefabDir);

            // EVERY OUTPUT FOLDER FIRST, BEFORE THE BATCH.
            //
            // StartAssetEditing defers AssetDatabase work until the matching
            // Stop, and that includes CreateFolder. Called inside the batch,
            // EnsureFolder's IsValidFolder check never saw the folder it had
            // just asked for, so it asked again for the next model - and
            // Unity, told to create a folder that already exists on disk,
            // makes "01_Structure 1", then "01_Structure 2". Building the
            // prefabs produced 2,299 numbered junk folders instead of 22.
            var dirs = new HashSet<string>();
            foreach (var j in jobs) dirs.Add(j.Value);
            foreach (var d in dirs) EnsureFolder(d);
            AssetDatabase.Refresh();

            int made = 0, skipped = 0;

            try
            {
                AssetDatabase.StartAssetEditing();
                for (int i = 0; i < jobs.Count; i++)
                {
                    var path = jobs[i].Key;
                    var src  = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (src == null) { skipped++; continue; }

                    // The folder structure is mirrored so a buyer can find
                    // the prefab for a model without hunting through 800 of
                    // them in one flat directory.
                    var outDir = jobs[i].Value;

                    var outPath = outDir + "/" + Path.GetFileNameWithoutExtension(path) + ".prefab";
                    if (File.Exists(outPath)) { skipped++; continue; }

                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
                    inst.transform.position = Vector3.zero;
                    inst.transform.rotation = Quaternion.identity;

                    // Unpack before saving: saving a prefab INSTANCE of the
                    // FBX writes a variant whose every property is an
                    // override of the model, and the buyer cannot then edit
                    // it without the changes fighting the importer.
                    PrefabUtility.UnpackPrefabInstance(
                        inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

                    AddSpinIfFan(inst);

                    foreach (var mf in inst.GetComponentsInChildren<MeshFilter>())
                    {
                        var mc = mf.gameObject.GetComponent<MeshCollider>();
                        if (mc == null) mc = mf.gameObject.AddComponent<MeshCollider>();
                        mc.sharedMesh = mf.sharedMesh;
                    }

                    PrefabUtility.SaveAsPrefabAsset(inst, outPath);
                    Object.DestroyImmediate(inst);
                    made++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            // A folder count that is not the category count means the
            // deferred-AssetDatabase trap above has come back.
            var folders = AssetDatabase.GetSubFolders(PrefabDir);
            Debug.Log($"[LPI] Prefabs: {made} created, {skipped} already "
                    + $"present, in {folders.Length} folders.");
            if (folders.Length > dirs.Count)
                Debug.LogError($"[LPI] {folders.Length} prefab folders but "
                             + $"only {dirs.Count} categories - duplicates "
                             + "were created.");
        }

        [MenuItem("Tools/Low Poly Interiors/Create Fire Particle System", false, 13)]
        public static void CreateFireParticles()
        {
            BuildFirePrefab();
        }

        /// Fans that don't turn are just ceiling decorations. Everything on
        /// a ceiling fan except the blades is a cylinder or a dome on the
        /// spin axis, so turning the whole prefab is identical to turning
        /// the blades - no split mesh, no rig, no animation clip.
        static void AddSpinIfFan(GameObject go)
        {
            var n = go.name.ToLowerInvariant();
            float speed;
            if (n.Contains("ceilingfan")) speed = 140f;
            else if (n.Contains("extractfan")) speed = 320f;
            else return;
            if (go.GetComponent<LowPolyInteriors.LPI_Spin>() != null) return;
            var sp = go.AddComponent<LowPolyInteriors.LPI_Spin>();
            sp.speed = speed;
            sp.axis  = LowPolyInteriors.LPI_Spin.SpinAxis.Y;
        }

        [MenuItem("Tools/Low Poly Interiors/Spin Ceiling Fans In Scene", false, 14)]
        public static void SpinFansInScene()
        {
            int n = 0;
            foreach (var t in Object.FindObjectsByType<Transform>(
                         FindObjectsSortMode.None))
            {
                var nm = t.name.ToLowerInvariant();
                if (!nm.Contains("ceilingfan") && !nm.Contains("extractfan"))
                    continue;
                if (t.GetComponent<LowPolyInteriors.LPI_Spin>() != null) continue;
                var sp = t.gameObject.AddComponent<LowPolyInteriors.LPI_Spin>();
                sp.speed = nm.Contains("extractfan") ? 320f : 140f;
                n++;
            }
            Debug.Log($"[LPI] LPI_Spin added to {n} fan(s) in the open scene.");
        }

        public static GameObject BuildFirePrefab(bool keepAuthoringObject = false)
        {
            const string FxDir = Root + "/Prefabs/FX";
            EnsureFolder(FxDir);

            var go  = new GameObject("FX_LPI_Fire");
            var ps  = go.AddComponent<ParticleSystem>();
            var psr = go.GetComponent<ParticleSystemRenderer>();

            // Stop before editing: several modules refuse writes while the
            // system is playing and the edits are silently dropped.
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration        = 2.0f;
            main.loop            = true;
            main.startLifetime   = new ParticleSystem.MinMaxCurve(0.55f, 1.15f);
            main.startSpeed      = new ParticleSystem.MinMaxCurve(0.35f, 0.85f);
            main.startColor      = new ParticleSystem.MinMaxGradient(
                                       new Color(1.00f, 0.58f, 0.19f),
                                       new Color(1.00f, 0.70f, 0.28f));
            // RECTANGLES, not squares: 3D start size with different x and y.
            main.startSize3D     = true;
            main.startSizeX      = new ParticleSystem.MinMaxCurve(0.045f, 0.085f);
            main.startSizeY      = new ParticleSystem.MinMaxCurve(0.070f, 0.130f);
            main.startSizeZ      = new ParticleSystem.MinMaxCurve(0.045f, 0.085f);
            main.startRotation   = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = -0.10f;          // they RISE
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles    = 130;
            main.playOnAwake     = true;

            var emission = ps.emission;
            emission.enabled      = true;
            emission.rateOverTime = 34f;

            var shape = ps.shape;
            shape.enabled               = true;
            shape.shapeType             = ParticleSystemShapeType.Box;
            shape.scale                 = new Vector3(0.30f, 0.04f, 0.16f);
            shape.randomDirectionAmount = 0.10f;

            // straight up, with a little wander
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space   = ParticleSystemSimulationSpace.Local;
            vel.x = new ParticleSystem.MinMaxCurve(-0.12f, 0.12f);
            vel.y = new ParticleSystem.MinMaxCurve(0.35f, 0.75f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.10f, 0.10f);

            // SPIN while rising - this is what stops a column of rectangles
            // reading as a stack of static cards.
            var rot = ps.rotationOverLifetime;
            rot.enabled    = true;
            rot.separateAxes = false;
            rot.z = new ParticleSystem.MinMaxCurve(-2.6f, 2.6f);

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    // WARM ORANGE the whole way. The first ramp ran from
                    // yellow through orange to deep red, which reads as
                    // three different fires stacked on top of each other.
                    new GradientColorKey(new Color(1.00f, 0.66f, 0.24f), 0.00f),
                    new GradientColorKey(new Color(1.00f, 0.55f, 0.17f), 0.50f),
                    new GradientColorKey(new Color(0.94f, 0.43f, 0.11f), 1.00f),
                },
                new[]
                {
                    new GradientAlphaKey(0.00f, 0.00f),
                    new GradientAlphaKey(1.00f, 0.14f),
                    new GradientAlphaKey(0.92f, 0.58f),
                    new GradientAlphaKey(0.00f, 1.00f),
                });
            col.color = new ParticleSystem.MinMaxGradient(grad);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(
                1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.20f));

            // Billboard, so a rectangle always reads as a rectangle. A quad
            // rotating in 3D vanishes edge-on twice a turn, which flickers.
            psr.renderMode    = ParticleSystemRenderMode.Billboard;
            psr.alignment     = ParticleSystemRenderSpace.View;
            psr.sortMode      = ParticleSystemSortMode.YoungestInFront;

            var mat = new Material(
                Shader.Find("Particles/Standard Unlit")
                ?? Shader.Find("Mobile/Particles/Additive")
                ?? Shader.Find("Sprites/Default"));
            mat.name = "M_LPI_FireParticle";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(
                Root + "/Textures/LPI_Particle.png");
            if (tex != null) mat.SetTexture("_MainTex", tex);
            // ALPHA BLENDING, NOT ADDITIVE - and this is the whole reason
            // the fire is the colour it is meant to be.
            //
            // Additive blending sums what overlaps. Two warm-orange quads
            // at (1.00, 0.66, 0.24) add to (1.00, 1.00, 0.48): the red
            // channel clips, the green keeps climbing, and the middle of
            // the flame - exactly where the particles pile up - comes out
            // lemon yellow. The brief is warm orange and nothing else.
            // Dimming the particles to stop it just produced a brown fire.
            //
            // Alpha blending cannot do that. An overlapped quad occludes
            // the one behind instead of adding to it, so the colour on
            // screen is the colour in the gradient no matter how many
            // particles stack up, and it can stay at full brightness. The
            // glow the fire throws into the room comes from the point
            // light below, which is where that job belongs anyway.
            mat.SetFloat("_Mode", 2f);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend",
                       (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 3000;
            var matPath = FxDir + "/M_LPI_FireParticle.mat";
            AssetDatabase.DeleteAsset(matPath);
            AssetDatabase.CreateAsset(mat, matPath);
            psr.sharedMaterial = mat;

            // the glow the fire throws into the room
            var lightGo = new GameObject("Firelight");
            lightGo.transform.SetParent(go.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 0.24f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type      = LightType.Point;
            light.color     = new Color(1f, 0.62f, 0.28f);
            light.intensity = 2.6f;
            light.range     = 4.5f;
            light.shadows   = LightShadows.Soft;

            var path = FxDir + "/FX_LPI_Fire.prefab";
            PrefabUtility.SaveAsPrefabAsset(go, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[LPI] Fire particle system written to {path}");

            // SaveAsPrefabAsset does NOT consume the object it was given.
            // Left alive it stays in whatever scene happens to be open, and
            // BuildDemoRooms then saves it into that scene - which is how a
            // loose ParticleSystem ended up in Demo_Bathroom, a room with no
            // fireplace in it at all. Unity's validator reports it as a
            // particle system not belonging to any prefab, and it is right.
            if (!keepAuthoringObject)
            {
                Object.DestroyImmediate(go);
                return AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
            return go;
        }

        /// Render a still of the particle system so it can be checked
        /// without opening the editor. Simulates to a fixed time first, so
        /// the same call always produces the same picture.
        public static void PreviewFire()
        {
            // the preview needs the live object, not the asset
            var go = BuildFirePrefab(true);
            go.transform.position = Vector3.zero;

            var ps = go.GetComponent<ParticleSystem>();
            ps.Simulate(3.0f, true, true);
            ps.Pause();

            var camGo = new GameObject("PreviewCam");
            var cam = camGo.AddComponent<Camera>();
            cam.transform.position = new Vector3(0f, 0.45f, -1.30f);
            cam.transform.LookAt(new Vector3(0f, 0.38f, 0f));
            cam.fieldOfView = 40f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.10f, 0.09f, 0.11f);

            var rt = new RenderTexture(1000, 800, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var img = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            img.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            img.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;

            var outPath = System.Environment.GetEnvironmentVariable("LPI_PREVIEW_OUT");
            if (string.IsNullOrEmpty(outPath)) outPath = "/tmp/lpi_fire_preview.png";
            System.IO.File.WriteAllBytes(outPath, img.EncodeToPNG());
            Debug.Log($"[LPI] particle count={ps.particleCount} preview={outPath}");

            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(go);
        }

        [MenuItem("Tools/Low Poly Interiors/Build Demo Rooms", false, 11)]
        public static void BuildDemoRooms()
        {
            var guids = AssetDatabase.FindAssets("t:Model", new[] { RoomsDir });
            if (guids.Length == 0)
            {
                Debug.LogWarning("[LPI] No room models under " + RoomsDir);
                return;
            }
            EnsureFolder(SceneDir);

            var prefabs = PrefabMap();
            if (prefabs.Count == 0)
                Debug.LogWarning("[LPI] No prefabs found - run Build Prefabs "
                               + "first, or the demo rooms will be welded "
                               + "meshes you cannot take apart.");

            int made = 0, swapped = 0, kept = 0;
            foreach (var g in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                var src  = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (src == null) continue;

                var scene = EditorSceneManager.NewScene(
                    NewSceneSetup.EmptyScene, NewSceneMode.Single);

                var name = Path.GetFileNameWithoutExtension(path)
                               .Replace("SM_LPI_Room_", "");

                var room = (GameObject)PrefabUtility.InstantiatePrefab(src);
                room.transform.position = Vector3.zero;
                room.transform.rotation = Quaternion.identity;
                PrefabUtility.UnpackPrefabInstance(
                    room, PrefabUnpackMode.Completely,
                    InteractionMode.AutomatedAction);

                var root = new GameObject(name);
                int s2 = 0, k2 = 0;
                Modularise(room, root.transform, prefabs, ref s2, ref k2);
                Object.DestroyImmediate(room);
                swapped += s2;
                kept += k2;

                AddRoomLighting(root);
                AddFireToFireplaces(root);

                var scenePath = SceneDir + "/Demo_" + name + ".unity";
                EditorSceneManager.MarkSceneDirty(scene);
                // Saved explicitly. A scene that is built but never written
                // is a scene the buyer opens to find empty.
                EditorSceneManager.SaveScene(scene, scenePath);
                made++;
                Debug.Log($"[LPI] Demo_{name}: {s2} prefab instances, "
                        + $"{k2} room-specific meshes");
            }
            AssetDatabase.Refresh();
            Debug.Log($"[LPI] {made} demo room scene(s) written to {SceneDir}"
                    + $" - {swapped} prefab instances, {kept} shell meshes.");
        }

        /// Every prefab in the pack, by model name.
        static Dictionary<string, GameObject> PrefabMap()
        {
            var map = new Dictionary<string, GameObject>();
            if (!AssetDatabase.IsValidFolder(PrefabDir)) return map;
            foreach (var g in AssetDatabase.FindAssets("t:Prefab",
                                                       new[] { PrefabDir }))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                var n = Path.GetFileNameWithoutExtension(p);
                if (!map.ContainsKey(n))
                    map[n] = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            }
            return map;
        }

        /// Blender names duplicates "Foo.001". Unity keeps that in the FBX
        /// hierarchy, so the name has to be reduced to the model it is a
        /// copy of before it can be matched to a prefab.
        static string ModelName(string n)
        {
            n = n.Trim();
            int dot = n.LastIndexOf('.');
            if (dot > 0 && n.Length - dot >= 2)
            {
                bool digits = true;
                for (int i = dot + 1; i < n.Length; i++)
                    if (!char.IsDigit(n[i])) { digits = false; break; }
                if (digits) n = n.Substring(0, dot);
            }
            return n;
        }

        /// "SM_LPI_Seat_Sofa_02" -> "Seat". Used only to group the
        /// hierarchy so a 118-object room is navigable in the Hierarchy
        /// window instead of one flat list.
        static string CategoryOf(string modelName)
        {
            const string pre = "SM_LPI_";
            if (!modelName.StartsWith(pre)) return "Misc";
            var rest = modelName.Substring(pre.Length);
            int us = rest.IndexOf('_');
            return us > 0 ? rest.Substring(0, us) : "Misc";
        }

        /// Replace each welded child of an imported room with a linked
        /// PREFAB INSTANCE standing in exactly the same place.
        ///
        /// The room FBX exists because exporting an assembled room lets the
        /// FBX importer do the Blender-to-Unity axis conversion, which is
        /// the one part of this nobody should be doing by hand. But what
        /// that produces is a single welded model: the buyer can look at
        /// the room and cannot take a chair out of it, swap a sofa or move
        /// a lamp.
        ///
        /// So the conversion is still the importer's, and the transforms
        /// are read back off the objects Unity has ALREADY placed
        /// correctly. No coordinates are recomputed here and no sign can be
        /// wrong: each child hands over its own world position, rotation
        /// and scale, and the matching prefab is instantiated at it.
        static void Modularise(GameObject room, Transform root,
                               Dictionary<string, GameObject> prefabs,
                               ref int swapped, ref int kept)
        {
            var groups = new Dictionary<string, Transform>();
            Transform Group(string cat)
            {
                if (!groups.TryGetValue(cat, out var t))
                {
                    var go = new GameObject(cat);
                    go.transform.SetParent(root, false);
                    t = go.transform;
                    groups[cat] = t;
                }
                return t;
            }

            var children = new List<Transform>();
            foreach (Transform c in room.transform) children.Add(c);

            foreach (var child in children)
            {
                var model = ModelName(child.name);

                // The shell - walls, ceiling, glazing - is built for this
                // room's dimensions and has no library prefab. It stays as
                // it is, under its own group.
                // camera markers: kept aside for AddRoomLighting, which
                // reads them and then deletes them
                if (model.StartsWith("SM_LPI_RoomCam"))
                {
                    child.SetParent(Group("__Camera"), true);
                    continue;
                }

                if (model.StartsWith("SM_LPI_Room_") ||
                    !prefabs.TryGetValue(model, out var prefab) ||
                    prefab == null)
                {
                    child.SetParent(Group("Shell"), true);
                    kept++;
                    continue;
                }

                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                inst.name = child.name;
                inst.transform.SetParent(Group(CategoryOf(model)), false);
                inst.transform.SetPositionAndRotation(
                    child.position, child.rotation);
                inst.transform.localScale = child.lossyScale;
                Object.DestroyImmediate(child.gameObject);
                swapped++;
            }
        }

        /// Put the real ParticleSystem into any fireplace in the room.
        ///
        /// The fireplace MODEL carries a low-poly flame so that the FBX
        /// looks like a lit fire on its own, in the library scene and in a
        /// thumbnail. In a demo scene the buyer is meant to see the thing
        /// they were sold: a Unity particle system, warm orange, moving.
        /// It goes in as a CHILD of the fireplace, so dragging the
        /// fireplace takes its fire with it.
        ///
        /// The offset is the flame's own position in the model, in metres
        /// from the fireplace's bottom-centre pivot: 170 mm up, centred,
        /// which is inside the firebox on every surround in the pack
        /// because they are all built from the same firebox proportions.
        static void AddFireToFireplaces(GameObject root)
        {
            var firePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                Root + "/Prefabs/FX/FX_LPI_Fire.prefab");
            if (firePrefab == null)
            {
                BuildFirePrefab();
                firePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    Root + "/Prefabs/FX/FX_LPI_Fire.prefab");
            }
            if (firePrefab == null) return;

            int n = 0;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                var nm = ModelName(t.name);
                if (!nm.Contains("Fire_Surround") && !nm.Contains("Fire_Stove"))
                    continue;
                var fx = (GameObject)PrefabUtility.InstantiatePrefab(firePrefab);
                fx.name = "Fire";
                fx.transform.SetParent(t, false);
                fx.transform.localPosition = new Vector3(0f, 0.17f, 0f);
                fx.transform.localRotation = Quaternion.identity;
                n++;
            }
            if (n > 0) Debug.Log($"[LPI]   + {n} fire particle system(s)");
        }

        static void AddRoomLighting(GameObject root)
        {
            // A room with no lights in it is a black rectangle. These
            // values are a plain warm interior key plus sky fill, not an
            // attempt to reproduce the marketing renders.
            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.93f, 0.83f);
            sun.intensity = 1.15f;
            sun.shadows = LightShadows.Soft;
            sunGo.transform.rotation = Quaternion.Euler(48f, -38f, 0f);

            // A single key light leaves the underside of the ceiling and
            // the shaded wall in the dark, so the first thing the buyer
            // sees on opening a demo scene is a gloomy box. This is a
            // shadowless bounce fill from the opposite side - what the
            // interior renders get from their bounce card.
            var fillGo = new GameObject("Fill");
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = new Color(0.86f, 0.88f, 1f);
            fill.intensity = 0.42f;
            fill.shadows = LightShadows.None;
            fillGo.transform.rotation = Quaternion.Euler(24f, 152f, 0f);

            var camGo = new GameObject("Demo Camera");
            var cam = camGo.AddComponent<Camera>();

            // The room ships two marker cubes: where the camera stands and
            // what it looks at, both already converted into Unity's frame
            // by the FBX importer. Guessing instead - "just outside the
            // corner with the largest coordinates" - put the camera
            // outside the CLOSED corner of every room, looking at the back
            // of a blank wall.
            Transform eye = null, aim = null;
            var markers = root.transform.Find("__Camera");
            if (markers != null)
                foreach (Transform m in markers)
                {
                    if (m.name.Contains("CamEye")) eye = m;
                    else if (m.name.Contains("CamAim")) aim = m;
                }

            if (eye != null && aim != null)
            {
                camGo.transform.position = eye.position;
                camGo.transform.LookAt(aim.position);
                cam.fieldOfView = FovFromName(eye.name, 58f);
            }
            else
            {
                var b = LocalBounds(root);
                cam.fieldOfView = 58f;
                camGo.transform.position = new Vector3(
                    b.max.x + 1.0f, 1.68f, b.max.z + 1.0f);
                camGo.transform.LookAt(
                    new Vector3(b.center.x, 1.05f, b.center.z));
            }
            if (markers != null) Object.DestroyImmediate(markers.gameObject);

            RenderSettings.ambientMode =
                UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor     = new Color(0.78f, 0.80f, 0.84f);
            RenderSettings.ambientEquatorColor = new Color(0.69f, 0.66f, 0.61f);
            RenderSettings.ambientGroundColor  = new Color(0.47f, 0.42f, 0.37f);
        }

        [MenuItem("Tools/Low Poly Interiors/Build Library Scene", false, 12)]
        public static void BuildDemoScene()
        {
            EnsureFolder(SceneDir);
            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabDir });
            if (guids.Length == 0)
            {
                Debug.LogWarning("[LPI] Build Prefabs first.");
                return;
            }

            var byCat = new SortedDictionary<string, List<GameObject>>();
            foreach (var g in guids)
            {
                var p  = AssetDatabase.GUIDToAssetPath(g);
                // The library scene lays out the PIECES. Prefabs/Rooms holds
                // whole assembled rooms and flats - dropping a 773-object
                // apartment into the parts grid buries the shelf it lands on.
                if (p.StartsWith(RoomPrefabDir + "/")) continue;
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                if (go == null) continue;
                var parts = go.name.Split('_');
                var cat   = parts.Length > 2 ? parts[2] : "Misc";
                if (!byCat.TryGetValue(cat, out var list))
                    byCat[cat] = list = new List<GameObject>();
                list.Add(go);
            }

            float z = 0f;
            foreach (var kv in byCat)
            {
                var row    = new GameObject("LPI_" + kv.Key);
                row.transform.position = new Vector3(0f, 0f, z);
                float x    = 0f;
                float depth = 0f;
                foreach (var prefab in kv.Value.OrderBy(o => o.name))
                {
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    inst.transform.SetParent(row.transform, false);

                    var b = LocalBounds(inst);
                    inst.transform.localPosition = new Vector3(x + b.size.x * 0.5f, 0f, 0f);
                    x     += b.size.x + 0.25f;
                    depth  = Mathf.Max(depth, b.size.z);
                }
                z += depth + 0.9f;
            }

            var scenePath = SceneDir + "/LPI_Library.unity";
            EditorSceneManager.MarkSceneDirty(scene);
            // Saved explicitly. A scene that is built but never written is a
            // scene the buyer opens to find empty.
            EditorSceneManager.SaveScene(scene, scenePath);
            AssetDatabase.Refresh();
            Debug.Log($"[LPI] Demo scene written to {scenePath}");
        }

        /// The eye marker carries the lens in its name: "..._F68".
        static float FovFromName(string n, float fallback)
        {
            int i = n.LastIndexOf("_F");
            if (i < 0) return fallback;
            var digits = n.Substring(i + 2);
            float v;
            return float.TryParse(digits, out v) && v > 5f && v < 160f
                 ? v : fallback;
        }

        static Bounds LocalBounds(GameObject go)
        {
            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return new Bounds(Vector3.zero, Vector3.one * 0.5f);
            var b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            return b;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace("\\", "/");
            var leaf   = Path.GetFileName(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
