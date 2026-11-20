using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Mandala.Game.Editor
{
    public static class SandDesignImporter
    {
        const string MaskFolder = "Assets/Mandala/Designs/Masks";
        const string LibraryFolder = "Assets/Mandala/Resources/" + SandDesign.ResourceFolder;

        [MenuItem("Tools/Sand Painting/Import Design PNG…")]
        static void Import()
        {
            string source = EditorUtility.OpenFilePanel("Import sand design mask", "", "png");
            if (string.IsNullOrEmpty(source)) return;
            try
            {
                Selection.activeObject = Import(source, Path.GetFileNameWithoutExtension(source), "Unsorted");
                EditorGUIUtility.PingObject(Selection.activeObject);
            }
            catch (Exception error)
            {
                EditorUtility.DisplayDialog("Could not import design", error.Message, "OK");
            }
        }

        public static SandDesign Import(string source, string title, string collection, string id = null)
        {
            byte[] png = File.ReadAllBytes(source);
            var validation = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!validation.LoadImage(png)) throw new InvalidDataException("The file could not be decoded.");
                SandDesign.ReadMask(validation);
            }
            finally { UnityEngine.Object.DestroyImmediate(validation); }

            Directory.CreateDirectory(MaskFolder);
            Directory.CreateDirectory(LibraryFolder);
            AssetDatabase.Refresh();
            string filename = Path.GetFileNameWithoutExtension(source);
            string maskPath = AssetDatabase.GenerateUniqueAssetPath($"{MaskFolder}/{filename}.png");
            string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{LibraryFolder}/{filename}.asset");
            SandDesign design = null;
            try
            {
                File.WriteAllBytes(maskPath, png);
                AssetDatabase.ImportAsset(maskPath, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(maskPath);
                importer.isReadable = true;
                importer.alphaIsTransparency = true;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.mipmapEnabled = true;
                importer.filterMode = FilterMode.Trilinear;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
                var mask = AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath);
                SandDesign.ReadMask(mask);

                design = ScriptableObject.CreateInstance<SandDesign>();
                design.Initialize(id ?? Guid.NewGuid().ToString("N"), title, collection, mask);
                AssetDatabase.CreateAsset(design, assetPath);
                AssetDatabase.SaveAssets();
                return design;
            }
            catch
            {
                if (AssetDatabase.Contains(design)) AssetDatabase.DeleteAsset(assetPath);
                else if (design != null) UnityEngine.Object.DestroyImmediate(design);
                if (!AssetDatabase.DeleteAsset(maskPath) && File.Exists(maskPath)) File.Delete(maskPath);
                throw;
            }
        }
    }
}
