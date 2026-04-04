// DmzsFixUrpMaterials.cs
// Editor utilities:
//   Tools > Fix URP Materials    -- converts Standard shader materials to URP Lit
//   Tools > Import URDF (Safe)   -- imports a URDF file with VHACD/collision disabled
using UnityEditor;
using UnityEngine;
using Unity.Robotics.UrdfImporter;

public static class DmzsFixUrpMaterials
{
    // ------------------------------------------------------------------
    // URP material fixer
    // ------------------------------------------------------------------
    [MenuItem("Tools/Fix URP Materials")]
    public static void FixAll()
    {
        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLit == null)
        {
            Debug.LogError("[DmzsFixUrpMaterials] URP Lit shader not found. Is URP installed?");
            return;
        }

        var fixedMaterials = new System.Collections.Generic.Dictionary<Material, Material>();
        int fixedCount = 0;

        string fixedDir = "Assets/FixedMaterials";
        if (!AssetDatabase.IsValidFolder(fixedDir))
        {
            AssetDatabase.CreateFolder("Assets", "FixedMaterials");
        }

        // Pass 1: Clone materials on active scene renderers to sever ties with auto-resetting embedded STLs
        foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var mats = renderer.sharedMaterials;
            bool modified = false;
            for (int i = 0; i < mats.Length; i++)
            {
                var mat = mats[i];
                if (mat == null) continue;
                
                // If we already cloned this material in this run, just assign it
                if (fixedMaterials.TryGetValue(mat, out var replacement))
                {
                    mats[i] = replacement;
                    modified = true;
                    continue;
                }

                if (mat.shader.name.Contains("Universal Render Pipeline") || mat.shader.name.Contains("Unlit")) 
                    continue;

                // Create a completely new persistent material
                Material newMat = new Material(urpLit);
                newMat.name = mat.name + "_URP";
                
                // Safely transfer properties if they exist
                Color albedo = Color.white;
                if (!mat.shader.name.Contains("Decal") && mat.HasProperty("_Color")) 
                {
                    try { albedo = mat.GetColor("_Color"); } catch { }
                }

                if (newMat.HasProperty("_BaseColor")) newMat.SetColor("_BaseColor", albedo);
                
                if (mat.HasProperty("_MainTex") && newMat.HasProperty("_BaseMap"))
                {
                    try 
                    { 
                        Texture tex = mat.GetTexture("_MainTex");
                        if (tex != null) newMat.SetTexture("_BaseMap", tex);
                    } catch { }
                }

                // Save to disk so Unity cannot reset it!
                string assetName = newMat.name.Replace(":", "_").Replace("/", "_") + ".mat";
                string newPath = AssetDatabase.GenerateUniqueAssetPath(fixedDir + "/" + assetName);
                AssetDatabase.CreateAsset(newMat, newPath);

                fixedMaterials[mat] = newMat;
                mats[i] = newMat;
                modified = true;
                fixedCount++;
            }
            if (modified)
            {
                renderer.sharedMaterials = mats;
                EditorUtility.SetDirty(renderer.gameObject);
            }
        }

        // Pass 2 aborted: We only fix scene renderers explicitly to avoid touching locked files

        AssetDatabase.SaveAssets();
        Debug.Log($"[DmzsFixUrpMaterials] Generated and assigned {fixedCount} robust URP material(s).");
    }

    // ------------------------------------------------------------------
    // Safe URDF importer -- forces convex decomposer to None to avoid
    // the VHACD NullReferenceException on complex Niryo meshes.
    // ------------------------------------------------------------------
    [MenuItem("Tools/Import URDF (Safe - No VHACD)")]
    public static void ImportUrdfSafe()
    {
        string path = EditorUtility.OpenFilePanel("Select URDF file", "Assets", "urdf");
        if (string.IsNullOrEmpty(path)) return;

        // Convert absolute path to project-relative
        string dataPath = Application.dataPath;
        if (!path.StartsWith(dataPath))
        {
            Debug.LogError("[ImportUrdfSafe] File must be inside the Assets folder.");
            return;
        }
        string relativePath = "Assets" + path.Substring(dataPath.Length);

        ImportSettings settings = new ImportSettings
        {
            // Use Unity's built-in decomposer instead of VHACD -- avoids the NullReferenceException
            // that VHACD throws on complex Niryo meshes. Valid values: unity | vhacd.
            convexMethod = ImportSettings.convexDecomposer.unity,
        };

        UrdfRobotExtensions.Create(relativePath, settings);
        Debug.Log($"[ImportUrdfSafe] Importing: {relativePath}  (convex=none)");
    }
}
