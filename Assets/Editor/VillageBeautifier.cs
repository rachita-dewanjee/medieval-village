using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Makes the Slavic World village look brighter and fuller, in the style of
/// a colourful stylized village (blue sky, warm sun, saturated colours).
///
/// Menu: Tools > Village
///   1. Beautify Lighting and Colour  - sun, ambient light, fog, post-processing
///   2. Add Trees and Props           - extra trees/bushes, props around houses
///   3. Add Floating Sparkles         - small glowing particles around the player
///   Remove Everything Added          - deletes all of the above, restores lighting
///
/// Everything added is named "Beautify_..." so it is easy to find and delete.
/// Save the scene (Ctrl+S) afterwards to keep the changes.
/// </summary>
public static class VillageBeautifier
{
    const string AddedRootName = "Beautify_Added";
    const string VolumeName = "Beautify_Volume";
    const string SparklesName = "Beautify_Sparkles";

    const string SettingsFolder = "Assets/Settings";
    const string ProfilePath = SettingsFolder + "/VillageBeautifyProfile.asset";
    const string SparkleMaterialPath = SettingsFolder + "/VillageSparkle.mat";
    const string SparkleTexturePath = SettingsFolder + "/VillageSparkle_Tex.asset";
    const string LightingBackupPath = SettingsFolder + "/VillageBeautify_OriginalLighting.json";

    const string PrefabRoot = "Assets/EmaceArt/Slavic World Free/Prefabs/";

    // How far from the player's start position decorations are added (metres).
    const float DecorateRadius = 75f;

    static readonly string[] TreePrefabs =
    {
        "Nature/Tree/EA03_Nature_Tree_01b_PRE.prefab",
        "Nature/Tree/EA03_Nature_Tree_02b_PRE.prefab",
        "Nature/Tree/EA03_Nature_Tree_02c_PRE.prefab",
        "Nature/Tree/EA03_Nature_Tree_03b_PRE.prefab",
        "Nature/Tree/EA03_Nature_Tree_03c_PRE.prefab",
        "Nature/Tree/EA03_Nature_Tree_05a_PRE.prefab",
        "Nature/Tree/EA03_Nature_Tree_06b_PRE.prefab",
    };

    static readonly string[] BushPrefabs =
    {
        "Nature/Bushes/EA03_Nature_Bush_01a_PRE.prefab",
        "Nature/Bushes/EA03_Nature_Bush_01c_PRE.prefab",
        "Nature/Bushes/EA03_Nature_Bush_02a_PRE.prefab",
        "Nature/Bushes/EA03_Nature_Bush_04a_PRE.prefab",
        "Nature/Bushes/EA03_Nature_Bush_05a_PRE.prefab",
        "Nature/Bushes/EA03_Nature_Bush_05b_PRE.prefab",
        "Nature/Plant/EA03_Nature_SweetFlag_01a_PRE.prefab",
        "Nature/Plant/EA03_Nature_SweetFlag_02a_PRE.prefab",
        "Nature/Plant/EA03_Nature_TrunkPlant_01a_PRE.prefab",
    };

    static readonly string[] YardPropPrefabs =
    {
        "Prop/Container/EA03_Prop_Container_Barrel_01d_PRE.prefab",
        "Prop/Container/EA03_Prop_Container_Crate_01a_PRE.prefab",
        "Prop/Container/EA03_Prop_Container_Crate_02a_PRE.prefab",
        "Prop/Container/EA03_Prop_Container_Crate_03a_PRE.prefab",
        "Prop/Container/EA03_Prop_Container_Bale_01a_PRE.prefab",
        "Prop/Container/EA03_Prop_Container_Bag_02a_PRE.prefab",
        "Prop/Container/EA03_Prop_Container_Basket_01a_PRE.prefab",
        "Prop/Container/EA03_Prop_Vegetable_Basket_01a_PRE.prefab",
        "Prop/Container/EA03_Prop_Vegetable_Bag_02_PRE.prefab",
        "Prop/Carpentry/EA03_Prop_Brushwood_03a_PRE.prefab",
        "Prop/Carpentry/EA03_Prop_Tool_Stump_02a_PRE.prefab",
        "Prop/Village/EA03_Prop_Village_Whell_01d_PRE.prefab",
        "Prop/Furniture/EA03_Prop_Town_Bench_01b_PRE.prefab",
        "Items/House/EA03_Items_House_Firewood_01a_PRE.prefab",
    };

    // Ground pieces with these words in their name are paths/water, not grass.
    static readonly string[] NotGrassWords =
        { "road", "cobble", "mud", "sand", "river", "water", "pier", "bridge", "wooden", "stair", "step", "platform" };

    static readonly string[] WaterWords = { "river", "water", "pier", "bridge" };

    // =====================================================================
    // 1. LIGHTING AND COLOUR
    // =====================================================================
    [MenuItem("Tools/Village/1. Beautify Lighting and Colour")]
    static void BeautifyLighting()
    {
        SaveOriginalLightingOnce();

        // ---- Sun: warm, bright, soft shadows ----
        Light sun = FindSun();
        if (sun != null)
        {
            Undo.RecordObject(sun, "Beautify Sun");
            sun.useColorTemperature = false;
            sun.color = new Color(1f, 0.93f, 0.80f);
            sun.intensity = 1.6f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.8f;
            RenderSettings.sun = sun;
        }

        // ---- Ambient: blue from the sky, warm from the ground ----
        // (was a flat white colour, which made everything look washed out)
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.52f, 0.66f, 0.86f);
        RenderSettings.ambientEquatorColor = new Color(0.62f, 0.63f, 0.58f);
        RenderSettings.ambientGroundColor = new Color(0.33f, 0.30f, 0.25f);

        // ---- Light blue haze in the distance (gives depth) ----
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.70f, 0.82f, 0.93f);
        RenderSettings.fogStartDistance = 35f;
        RenderSettings.fogEndDistance = 220f;

        // ---- Post-processing: richer colours and a soft glow ----
        VolumeProfile profile = CreateVolumeProfile();

        GameObject volumeObject = GameObject.Find(VolumeName);
        if (volumeObject == null)
        {
            volumeObject = new GameObject(VolumeName);
            Undo.RegisterCreatedObjectUndo(volumeObject, "Create Beautify Volume");
        }

        Volume volume = volumeObject.GetComponent<Volume>();
        if (volume == null)
            volume = Undo.AddComponent<Volume>(volumeObject);

        volume.isGlobal = true;
        volume.priority = 10f;
        volume.sharedProfile = profile;

        // Make sure the player camera actually draws post-processing.
        Camera playerCamera = FindPlayerCamera();
        if (playerCamera != null)
        {
            var cameraData = playerCamera.GetUniversalAdditionalCameraData();
            Undo.RecordObject(cameraData, "Enable Post Processing");
            cameraData.renderPostProcessing = true;
        }

        MarkSceneDirty();
        EditorUtility.DisplayDialog("Village",
            "Lighting, fog and colour updated.\n\nPress Ctrl+S to save the scene.\n" +
            "Use Tools > Village > Remove Everything Added to undo.", "OK");
    }

    static VolumeProfile CreateVolumeProfile()
    {
        if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath) != null)
            AssetDatabase.DeleteAsset(ProfilePath);

        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, ProfilePath);

        var tonemapping = profile.Add<Tonemapping>(true);
        tonemapping.mode.value = TonemappingMode.Neutral;

        var colour = profile.Add<ColorAdjustments>(true);
        colour.postExposure.value = 0.15f;
        colour.contrast.value = 15f;
        colour.saturation.value = 25f;

        var whiteBalance = profile.Add<WhiteBalance>(true);
        whiteBalance.temperature.value = 6f;

        // Quarter-resolution bloom keeps it cheap on the phone.
        var bloom = profile.Add<Bloom>(true);
        bloom.threshold.value = 0.95f;
        bloom.intensity.value = 0.35f;
        bloom.scatter.value = 0.6f;
        bloom.downscale.value = BloomDownscaleMode.Quarter;

        foreach (VolumeComponent component in profile.components)
            AssetDatabase.AddObjectToAsset(component, profile);

        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
        return profile;
    }

    // =====================================================================
    // 2. TREES, BUSHES AND PROPS
    // =====================================================================
    [MenuItem("Tools/Village/2. Add Trees and Props")]
    static void AddDecorations()
    {
        if (GameObject.Find(AddedRootName) != null &&
            !EditorUtility.DisplayDialog("Village",
                "Decorations were already added. Add another batch?", "Add more", "Cancel"))
            return;

        Vector3 center = FindPlayerPosition();
        var random = new System.Random(806 + Random.Range(0, 100000));

        // The village meshes have no colliders, so make temporary hidden ones
        // to find the ground height and check that a spot is free.
        var colliderOwners = new Dictionary<Collider, GameObject>();
        var tempObjects = new List<GameObject>();

        try
        {
            CreateTemporaryColliders(colliderOwners, tempObjects);

            GameObject root = GameObject.Find(AddedRootName);
            if (root == null)
            {
                root = new GameObject(AddedRootName);
                Undo.RegisterCreatedObjectUndo(root, "Add Village Decorations");
            }

            var placed = new List<Vector3>();

            EditorUtility.DisplayProgressBar("Village", "Planting trees...", 0.4f);
            int trees = Scatter(TreePrefabs, 30, 1.6f, 7f, 0.9f, 1.3f,
                center, random, colliderOwners, placed, root.transform, NotGrassWords);

            EditorUtility.DisplayProgressBar("Village", "Planting bushes...", 0.6f);
            int bushes = Scatter(BushPrefabs, 45, 0.7f, 3f, 0.8f, 1.2f,
                center, random, colliderOwners, placed, root.transform, NotGrassWords);

            EditorUtility.DisplayProgressBar("Village", "Adding props around houses...", 0.8f);
            int props = AddYardProps(60, center, random, colliderOwners, placed, root.transform);

            MarkSceneDirty();
            EditorUtility.ClearProgressBar();
            EditorUtility.DisplayDialog("Village",
                $"Added {trees} trees, {bushes} bushes/plants and {props} props around houses " +
                $"(within {DecorateRadius:0} m of the player).\n\n" +
                $"They are all under \"{AddedRootName}\" in the Hierarchy - move or delete any you don't like.\n\n" +
                "Press Ctrl+S to save the scene.", "OK");
        }
        finally
        {
            foreach (GameObject temp in tempObjects)
                Object.DestroyImmediate(temp);
            EditorUtility.ClearProgressBar();
        }
    }

    static void CreateTemporaryColliders(Dictionary<Collider, GameObject> owners, List<GameObject> temps)
    {
        var filters = new List<MeshFilter>();
        foreach (GameObject sceneRoot in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (sceneRoot.name == AddedRootName)
                continue;
            filters.AddRange(sceneRoot.GetComponentsInChildren<MeshFilter>(false));
        }

        for (int i = 0; i < filters.Count; i++)
        {
            MeshFilter filter = filters[i];
            if (filter.sharedMesh == null)
                continue;

            // Grass does not block anything, skip it (also saves time).
            if (filter.name.ToLowerInvariant().Contains("grass"))
                continue;

            if (i % 200 == 0)
                EditorUtility.DisplayProgressBar("Village", "Reading the village layout...",
                    0.35f * i / filters.Count);

            Transform source = filter.transform;
            var temp = new GameObject("TempCollider") { hideFlags = HideFlags.HideAndDontSave };
            temp.transform.SetPositionAndRotation(source.position, source.rotation);
            temp.transform.localScale = source.lossyScale;

            var collider = temp.AddComponent<MeshCollider>();
            collider.sharedMesh = filter.sharedMesh;

            owners[collider] = filter.gameObject;
            temps.Add(temp);
        }

        Physics.SyncTransforms();
    }

    static int Scatter(string[] prefabPaths, int count, float clearRadius, float spacing,
        float minScale, float maxScale, Vector3 center, System.Random random,
        Dictionary<Collider, GameObject> owners, List<Vector3> placed, Transform parent,
        string[] forbiddenWords)
    {
        GameObject[] prefabs = LoadPrefabs(prefabPaths);
        if (prefabs.Length == 0)
            return 0;

        int added = 0, notGround = 0, notFree = 0;
        for (int attempt = 0; attempt < count * 40 && added < count; attempt++)
        {
            float angle = (float)random.NextDouble() * Mathf.PI * 2f;
            float distance = Mathf.Sqrt((float)random.NextDouble()) * DecorateRadius;
            var point = new Vector3(center.x + Mathf.Cos(angle) * distance, center.y,
                                    center.z + Mathf.Sin(angle) * distance);

            // Keep the player's start position clear.
            if (distance < 6f)
                continue;

            if (!FindGround(point, center.y + 150f, owners, forbiddenWords, out RaycastHit hit))
            {
                notGround++;
                continue;
            }

            if (!IsSpotFree(hit.point, clearRadius, spacing, owners, placed))
            {
                notFree++;
                continue;
            }

            GameObject prefab = prefabs[random.Next(prefabs.Length)];
            float scale = Mathf.Lerp(minScale, maxScale, (float)random.NextDouble());
            Place(prefab, hit.point, (float)random.NextDouble() * 360f, scale, parent);
            placed.Add(hit.point);
            added++;
        }

        Debug.Log($"Village: {prefabs[0].name} group - added {added}/{count}, " +
                  $"rejected {notGround} (not grass ground), {notFree} (spot taken)");
        return added;
    }

    static int AddYardProps(int count, Vector3 center, System.Random random,
        Dictionary<Collider, GameObject> owners, List<Vector3> placed, Transform parent)
    {
        GameObject[] prefabs = LoadPrefabs(YardPropPrefabs);
        GameObject buildingsGroup = GameObject.Find("Buildings");
        if (prefabs.Length == 0 || buildingsGroup == null)
            return 0;

        // Bounds of each building (child of the "Buildings" group).
        var buildings = new List<Bounds>();
        foreach (Transform child in buildingsGroup.transform)
        {
            Renderer[] renderers = child.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                continue;

            Bounds bounds = renderers[0].bounds;
            foreach (Renderer r in renderers)
                bounds.Encapsulate(r.bounds);

            Vector3 flatOffset = bounds.center - center;
            flatOffset.y = 0f;
            if (flatOffset.magnitude < DecorateRadius)
                buildings.Add(bounds);
        }

        if (buildings.Count == 0)
            return 0;

        int added = 0;
        for (int attempt = 0; attempt < count * 30 && added < count; attempt++)
        {
            Bounds house = buildings[random.Next(buildings.Count)];

            // A spot just outside the house walls.
            float angle = (float)random.NextDouble() * Mathf.PI * 2f;
            float houseRadius = Mathf.Max(house.extents.x, house.extents.z);
            float distance = houseRadius * 0.6f + 0.8f + (float)random.NextDouble() * 2.5f;
            var point = new Vector3(house.center.x + Mathf.Cos(angle) * distance, house.max.y,
                                    house.center.z + Mathf.Sin(angle) * distance);

            if (!FindGround(point, house.max.y + 5f, owners, WaterWords, out RaycastHit hit))
                continue;

            if (!IsSpotFree(hit.point, 0.55f, 1.8f, owners, placed))
                continue;

            // Face the prop roughly towards the house so it looks placed, not dropped.
            Vector3 toHouse = house.center - hit.point;
            float yaw = Mathf.Atan2(toHouse.x, toHouse.z) * Mathf.Rad2Deg + random.Next(-40, 40);

            GameObject prefab = prefabs[random.Next(prefabs.Length)];
            Place(prefab, hit.point, yaw, 1f, parent);
            placed.Add(hit.point);
            added++;
        }
        return added;
    }

    static bool FindGround(Vector3 point, float fromHeight, Dictionary<Collider, GameObject> owners,
        string[] forbiddenWords, out RaycastHit hit)
    {
        var origin = new Vector3(point.x, fromHeight, point.z);
        if (!Physics.Raycast(origin, Vector3.down, out hit, 400f) || hit.normal.y < 0.85f)
            return false;

        GameObject surface = Owner(hit.collider, owners);
        return IsGround(surface) && !NameContainsAny(surface, forbiddenWords) &&
               !HitMaterialContainsAny(surface, hit.triangleIndex, forbiddenWords);
    }

    // Our temporary colliders map back to their village object; a real
    // collider (like the one on the landscape mesh) belongs to its own object.
    static GameObject Owner(Collider collider, Dictionary<Collider, GameObject> owners)
    {
        return owners.TryGetValue(collider, out GameObject owner) ? owner : collider.gameObject;
    }

    // The landscape is one mesh with several materials (grass, mud, path...).
    // Find which material the hit triangle uses and check its name.
    static bool HitMaterialContainsAny(GameObject surface, int triangleIndex, string[] words)
    {
        var filter = surface.GetComponent<MeshFilter>();
        var renderer = surface.GetComponent<Renderer>();
        if (filter == null || renderer == null || filter.sharedMesh == null || triangleIndex < 0)
            return false;

        Mesh mesh = filter.sharedMesh;
        int index = triangleIndex * 3;
        for (int sub = 0; sub < mesh.subMeshCount && sub < renderer.sharedMaterials.Length; sub++)
        {
            var descriptor = mesh.GetSubMesh(sub);
            if (index < descriptor.indexStart || index >= descriptor.indexStart + descriptor.indexCount)
                continue;

            Material material = renderer.sharedMaterials[sub];
            if (material == null)
                return false;

            string name = material.name.ToLowerInvariant();
            foreach (string word in words)
                if (name.Contains(word))
                    return true;
            return false;
        }
        return false;
    }

    static bool IsSpotFree(Vector3 groundPoint, float clearRadius, float spacing,
        Dictionary<Collider, GameObject> owners, List<Vector3> placed)
    {
        foreach (Vector3 other in placed)
            if ((other - groundPoint).sqrMagnitude < spacing * spacing)
                return false;

        // Nothing except ground may touch a sphere sitting on this spot.
        Vector3 sphereCenter = groundPoint + Vector3.up * (clearRadius + 0.3f);
        foreach (Collider c in Physics.OverlapSphere(sphereCenter, clearRadius))
        {
            if (!IsGround(Owner(c, owners)))
                return false;
        }
        return true;
    }

    // Hierarchy groups in the village scene that hold objects, not ground.
    static readonly string[] ObjectGroups =
        { "Buildings", "Props", "Fences", "Rocks", "Bushes", "treeses", "Plants", "Grasses", AddedRootName };

    // Words in object names that mean "this is a thing standing on the ground".
    static readonly string[] ObjectWords =
        { "rock", "fence", "wall", "roof", "bush", "tree", "trunk", "sweetflag", "grass", "bld_", "village_",
          "town_", "prop_", "items_", "wooden_pin", "clif", "door", "step" };

    static bool IsGround(GameObject go)
    {
        for (Transform t = go.transform; t != null; t = t.parent)
        {
            foreach (string group in ObjectGroups)
                if (t.name == group)
                    return false;

            string name = t.name.ToLowerInvariant();
            foreach (string word in ObjectWords)
                if (name.Contains(word))
                    return false;
        }
        return true;
    }

    static bool NameContainsAny(GameObject go, string[] words)
    {
        // Check the object and its parents, since the prefab name is
        // usually on the parent of the mesh object.
        for (Transform t = go.transform; t != null; t = t.parent)
        {
            string name = t.name.ToLowerInvariant();
            foreach (string word in words)
                if (name.Contains(word))
                    return true;
        }
        return false;
    }

    static void Place(GameObject prefab, Vector3 position, float yaw, float scale, Transform parent)
    {
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        instance.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        instance.transform.localScale *= scale;
        Undo.RegisterCreatedObjectUndo(instance, "Add Village Decoration");
    }

    static GameObject[] LoadPrefabs(string[] paths)
    {
        var prefabs = new List<GameObject>();
        foreach (string path in paths)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabRoot + path);
            if (prefab != null)
                prefabs.Add(prefab);
            else
                Debug.LogWarning("Village: prefab not found: " + PrefabRoot + path);
        }
        return prefabs.ToArray();
    }

    [MenuItem("Tools/Village/Remove Added Trees and Props Only")]
    static void RemoveDecorations()
    {
        GameObject root = GameObject.Find(AddedRootName);
        if (root == null)
            return;

        Undo.DestroyObjectImmediate(root);
        MarkSceneDirty();
    }

    // =====================================================================
    // 4. EASY WALKING - small things stop blocking the player
    // =====================================================================

    // Surfaces you walk ON - their colliders are always kept.
    static readonly string[] WalkableWords =
        { "road", "cobble", "mud", "sand", "_flat", "stair", "step", "platform", "pier",
          "river", "field", "terrain", "moss", "bridge" };

    static readonly string[] VegetationWords = { "bush", "plant", "grass", "sweetflag" };

    static readonly string[] ClutterWords =
        { "items_", "prop_", "wooden_pin", "artefact", "desk_set" };

    [MenuItem("Tools/Village/4. Make Paths Easy to Walk")]
    static void ClearWalkways()
    {
        int changed = SetClutterColliders(false);
        MarkSceneDirty();
        EditorUtility.DisplayDialog("Village",
            $"{changed} small objects (bushes, small rocks, barrels, crates, baskets...) " +
            "no longer block the player - you walk through them.\n\n" +
            "Buildings, walls, fences, trees and big rocks still block.\n\n" +
            "Press Ctrl+S to save the scene.", "OK");
    }

    [MenuItem("Tools/Village/Undo Easy Walking (small objects block again)")]
    static void RestoreWalkways()
    {
        int changed = SetClutterColliders(true);
        MarkSceneDirty();
        EditorUtility.DisplayDialog("Village", $"{changed} colliders switched back on.", "OK");
    }

    static int SetClutterColliders(bool enable)
    {
        int changed = 0;
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
            {
                if (collider is CharacterController || collider.enabled == enable)
                    continue;

                // A switched-off collider has no size, so check it with the renderer.
                float height = collider.enabled ? collider.bounds.size.y : RendererHeight(collider.gameObject);
                if (!IsClutter(collider.gameObject, height))
                    continue;

                Undo.RecordObject(collider, enable ? "Restore Walkways" : "Clear Walkways");
                collider.enabled = enable;
                changed++;
            }
        }
        return changed;
    }

    static bool IsClutter(GameObject go, float height)
    {
        if (NameContainsAny(go, WalkableWords))
            return false;

        if (NameContainsAny(go, VegetationWords))
            return true;

        if (NameContainsAny(go, new[] { "rock" }))
            return height < 1.5f;

        if (NameContainsAny(go, ClutterWords) && !NameContainsAny(go, new[] { "door" }))
            return height < 1.6f;

        return false;
    }

    static float RendererHeight(GameObject go)
    {
        Renderer renderer = go.GetComponentInParent<Renderer>();
        if (renderer == null)
            renderer = go.GetComponentInChildren<Renderer>();
        return renderer != null ? renderer.bounds.size.y : 0f;
    }

    // =====================================================================
    // 5. HIDE FLOATING PATH STONES
    // =====================================================================

    // How far above the ground a stone's lowest point may be before it
    // counts as floating (metres).
    const float FloatingTolerance = 0.35f;

    [MenuItem("Tools/Village/5. Hide Floating Path Stones")]
    static void HideFloatingStones()
    {
        GameObject terrain = GameObject.Find("Terrain");
        if (terrain == null)
        {
            EditorUtility.DisplayDialog("Village", "Could not find the \"Terrain\" group.", "OK");
            return;
        }

        Physics.SyncTransforms();
        int hidden = 0, checkedCount = 0;

        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(false))
            {
                if (!t.name.Contains("RoadIsland") || !PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject))
                    continue;

                Renderer[] renderers = t.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0)
                    continue;

                Bounds bounds = renderers[0].bounds;
                foreach (Renderer r in renderers)
                    bounds.Encapsulate(r.bounds);

                checkedCount++;
                if (!LandscapeHeightAt(bounds.center, terrain.transform, out float groundY))
                    continue;

                if (bounds.min.y - groundY > FloatingTolerance)
                {
                    Undo.RecordObject(t.gameObject, "Hide Floating Stones");
                    t.gameObject.SetActive(false);
                    Debug.Log($"Village: hid floating stone {t.name} ({bounds.min.y - groundY:0.0} m above ground)", t.gameObject);
                    hidden++;
                }
            }
        }

        MarkSceneDirty();
        EditorUtility.DisplayDialog("Village",
            $"Checked {checkedCount} path stones, hid {hidden} that were floating above the ground.\n\n" +
            "They are only switched off (not deleted) - tick them on again in the Inspector if needed.\n\n" +
            "Press Ctrl+S to save the scene.", "OK");
    }

    // =====================================================================
    // 6. CLEAR STONES OFF THE PATHS
    // =====================================================================

    // Rocks lower than this (metres) count as small pebbles and are hidden.
    const float SmallStoneHeight = 0.8f;

    [MenuItem("Tools/Village/6. Clear Stones Off the Paths")]
    static void ClearPathStones()
    {
        int hidden = SetPathStonesActive(false);
        MarkSceneDirty();
        EditorUtility.DisplayDialog("Village",
            $"Hid {hidden} path stones and small pebbles. Big boulders are kept.\n\n" +
            "Use Tools > Village > Show Path Stones Again to bring them back.\n\n" +
            "Press Ctrl+S to save the scene.", "OK");
    }

    [MenuItem("Tools/Village/Show Path Stones Again")]
    static void ShowPathStones()
    {
        int shown = SetPathStonesActive(true);
        MarkSceneDirty();
        EditorUtility.DisplayDialog("Village", $"{shown} stones shown again.", "OK");
    }

    static int SetPathStonesActive(bool active)
    {
        int changed = 0;
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                GameObject go = t.gameObject;
                if (go.activeSelf == active || !PrefabUtility.IsOutermostPrefabInstanceRoot(go))
                    continue;

                if (!IsPathStone(go))
                    continue;

                Undo.RecordObject(go, active ? "Show Path Stones" : "Clear Path Stones");
                go.SetActive(active);
                changed++;
            }
        }
        return changed;
    }

    static bool IsPathStone(GameObject go)
    {
        string name = go.name;

        // Paving-stone pieces laid in the paths.
        if (name.Contains("RoadIsland"))
            return true;

        // Small rocks and pebbles (keep cliffs and flat rock slabs).
        if (!name.Contains("Rock") || name.Contains("Flat") || name.Contains("Slice") || name.Contains("Fence"))
            return false;

        return MeshHeight(go) < SmallStoneHeight;
    }

    // Height of an object's meshes, worked out from the mesh data so it
    // also works while the object is switched off.
    static float MeshHeight(GameObject go)
    {
        bool hasBounds = false;
        Bounds bounds = default;

        foreach (MeshFilter filter in go.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null)
                continue;

            Bounds local = filter.sharedMesh.bounds;
            Matrix4x4 matrix = filter.transform.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? local.min.x : local.max.x,
                    (i & 2) == 0 ? local.min.y : local.max.y,
                    (i & 4) == 0 ? local.min.z : local.max.z);
                Vector3 world = matrix.MultiplyPoint3x4(corner);

                if (!hasBounds)
                {
                    bounds = new Bounds(world, Vector3.zero);
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(world);
                }
            }
        }
        return hasBounds ? bounds.size.y : 0f;
    }

    // Height of the landscape mesh (under the "Terrain" group) below a point.
    static bool LandscapeHeightAt(Vector3 point, Transform terrain, out float groundY)
    {
        groundY = 0f;
        var origin = new Vector3(point.x, point.y + 100f, point.z);
        float nearest = float.MaxValue;
        bool found = false;

        foreach (RaycastHit hit in Physics.RaycastAll(origin, Vector3.down, 300f))
        {
            if (!hit.collider.transform.IsChildOf(terrain) || hit.distance >= nearest)
                continue;
            nearest = hit.distance;
            groundY = hit.point.y;
            found = true;
        }
        return found;
    }

    // =====================================================================
    // 3. FLOATING SPARKLES
    // =====================================================================
    [MenuItem("Tools/Village/3. Add Floating Sparkles")]
    static void AddSparkles()
    {
        Camera playerCamera = FindPlayerCamera();
        if (playerCamera == null)
        {
            EditorUtility.DisplayDialog("Village", "Could not find the player camera.", "OK");
            return;
        }

        Transform existing = playerCamera.transform.Find(SparklesName);
        if (existing != null)
            Undo.DestroyObjectImmediate(existing.gameObject);

        // Child of the camera so the sparkles follow the player,
        // but simulated in world space so they float in place.
        var sparkles = new GameObject(SparklesName);
        Undo.RegisterCreatedObjectUndo(sparkles, "Add Sparkles");
        sparkles.transform.SetParent(playerCamera.transform, false);
        sparkles.transform.localPosition = new Vector3(0f, 0.5f, 4f);

        var particles = sparkles.AddComponent<ParticleSystem>();

        var main = particles.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.25f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.10f);
        main.startColor = new Color(1f, 0.9f, 0.55f, 0.9f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 120;
        main.prewarm = true;

        var emission = particles.emission;
        emission.rateOverTime = 18f;

        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(22f, 5f, 22f);

        // Fade in and out instead of popping.
        var colourOverLife = particles.colorOverLifetime;
        colourOverLife.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f),
                    new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
        colourOverLife.color = gradient;

        // Gentle drifting.
        var noise = particles.noise;
        noise.enabled = true;
        noise.strength = 0.3f;
        noise.frequency = 0.4f;
        noise.quality = ParticleSystemNoiseQuality.Low;

        var renderer = sparkles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = CreateSparkleMaterial();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        MarkSceneDirty();
        EditorUtility.DisplayDialog("Village",
            "Floating sparkles added around the player.\n\nPress Ctrl+S to save the scene.", "OK");
    }

    static Material CreateSparkleMaterial()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(SparkleMaterialPath);
        if (existing != null)
            return existing;

        // Soft round dot texture.
        const int size = 64;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "VillageSparkle_Tex" };
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f;
                float dy = (y + 0.5f) / size * 2f - 1f;
                float alpha = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha * alpha));
            }
        }
        texture.Apply();
        AssetDatabase.CreateAsset(texture, SparkleTexturePath);

        // URP unlit particle material with additive blending (glows).
        var material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
        material.SetTexture("_BaseMap", texture);
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_Surface", 1f);   // Transparent
        material.SetFloat("_Blend", 2f);     // Additive
        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)BlendMode.One);
        material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        material.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
        material.SetFloat("_ZWrite", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = (int)RenderQueue.Transparent;

        AssetDatabase.CreateAsset(material, SparkleMaterialPath);
        AssetDatabase.SaveAssets();
        return material;
    }

    // =====================================================================
    // REMOVE
    // =====================================================================
    [MenuItem("Tools/Village/Remove Everything Added")]
    static void RemoveEverything()
    {
        if (!EditorUtility.DisplayDialog("Village",
                "Remove the added trees/props, sparkles and post-processing, " +
                "and put the original lighting back?", "Remove", "Cancel"))
            return;

        foreach (string name in new[] { AddedRootName, VolumeName })
        {
            GameObject go = GameObject.Find(name);
            if (go != null)
                Undo.DestroyObjectImmediate(go);
        }

        Camera playerCamera = FindPlayerCamera();
        Transform sparkles = playerCamera != null ? playerCamera.transform.Find(SparklesName) : null;
        if (sparkles != null)
            Undo.DestroyObjectImmediate(sparkles.gameObject);

        RestoreOriginalLighting();
        MarkSceneDirty();
        EditorUtility.DisplayDialog("Village", "Removed. Press Ctrl+S to save the scene.", "OK");
    }

    // =====================================================================
    // ORIGINAL LIGHTING BACKUP
    // =====================================================================
    [System.Serializable]
    class LightingBackup
    {
        public bool hasSun;
        public Color sunColor;
        public float sunIntensity;
        public bool sunUseTemperature;
        public LightShadows sunShadows;
        public float sunShadowStrength;

        public bool fog;
        public FogMode fogMode;
        public Color fogColor;
        public float fogStart, fogEnd, fogDensity;

        public AmbientMode ambientMode;
        public Color ambientSky, ambientEquator, ambientGround;
        public float ambientIntensity;
    }

    static void SaveOriginalLightingOnce()
    {
        if (File.Exists(LightingBackupPath))
            return;

        var backup = new LightingBackup
        {
            fog = RenderSettings.fog,
            fogMode = RenderSettings.fogMode,
            fogColor = RenderSettings.fogColor,
            fogStart = RenderSettings.fogStartDistance,
            fogEnd = RenderSettings.fogEndDistance,
            fogDensity = RenderSettings.fogDensity,
            ambientMode = RenderSettings.ambientMode,
            ambientSky = RenderSettings.ambientSkyColor,
            ambientEquator = RenderSettings.ambientEquatorColor,
            ambientGround = RenderSettings.ambientGroundColor,
            ambientIntensity = RenderSettings.ambientIntensity,
        };

        Light sun = FindSun();
        if (sun != null)
        {
            backup.hasSun = true;
            backup.sunColor = sun.color;
            backup.sunIntensity = sun.intensity;
            backup.sunUseTemperature = sun.useColorTemperature;
            backup.sunShadows = sun.shadows;
            backup.sunShadowStrength = sun.shadowStrength;
        }

        File.WriteAllText(LightingBackupPath, JsonUtility.ToJson(backup, true));
        AssetDatabase.ImportAsset(LightingBackupPath);
    }

    static void RestoreOriginalLighting()
    {
        if (!File.Exists(LightingBackupPath))
            return;

        var backup = JsonUtility.FromJson<LightingBackup>(File.ReadAllText(LightingBackupPath));

        RenderSettings.fog = backup.fog;
        RenderSettings.fogMode = backup.fogMode;
        RenderSettings.fogColor = backup.fogColor;
        RenderSettings.fogStartDistance = backup.fogStart;
        RenderSettings.fogEndDistance = backup.fogEnd;
        RenderSettings.fogDensity = backup.fogDensity;
        RenderSettings.ambientMode = backup.ambientMode;
        RenderSettings.ambientSkyColor = backup.ambientSky;
        RenderSettings.ambientEquatorColor = backup.ambientEquator;
        RenderSettings.ambientGroundColor = backup.ambientGround;
        RenderSettings.ambientIntensity = backup.ambientIntensity;

        Light sun = FindSun();
        if (sun != null && backup.hasSun)
        {
            Undo.RecordObject(sun, "Restore Sun");
            sun.color = backup.sunColor;
            sun.intensity = backup.sunIntensity;
            sun.useColorTemperature = backup.sunUseTemperature;
            sun.shadows = backup.sunShadows;
            sun.shadowStrength = backup.sunShadowStrength;
        }
    }

    // =====================================================================
    // HELPERS
    // =====================================================================
    static Light FindSun()
    {
        if (RenderSettings.sun != null)
            return RenderSettings.sun;

        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            foreach (Light light in root.GetComponentsInChildren<Light>(true))
                if (light.type == LightType.Directional)
                    return light;

        return null;
    }

    static Camera FindPlayerCamera()
    {
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            var movement = root.GetComponentInChildren<MobileTouchMovement>(true);
            if (movement != null && movement.TryGetComponent(out Camera cam))
                return cam;
        }
        return Camera.main;
    }

    static Vector3 FindPlayerPosition()
    {
        Camera cam = FindPlayerCamera();
        return cam != null ? cam.transform.position : Vector3.zero;
    }

    static void MarkSceneDirty()
    {
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }
}
