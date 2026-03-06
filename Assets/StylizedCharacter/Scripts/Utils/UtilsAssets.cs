using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace NHance.Assets.Scripts.Utils
{
    public static class UtilsAssets
    {
        #if UNITY_EDITOR
        public static List<T> FindAssetsByType<T>() where T : UnityEngine.Object
        {
            List<T> assets = new List<T>();
            string[] guids = AssetDatabase.FindAssets("t:prefab");
            for (int i = 0; i < guids.Length; i++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                T asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
                if (asset != null)
                {
                    assets.Add(asset);
                }
            }
            return assets;
        }
        
        public static List<T> LoadPrefabsContaining<T>() where T : UnityEngine.Component
        {
            List<T> result = new List<T>();

            var allFiles = FindAssetsByType<GameObject>();
            foreach (var obj in allFiles)
            {
                if (obj is GameObject)
                {
                    var component = obj.GetComponent<T>();
                    if (component != null)
                    {
                        result.Add(component);
                    }
                }
            }
            return result;
        }
        
        public static T[] GetAtPath<T>(string path)
        {
            var al = new ArrayList();
            var fileEntries = Directory.GetFiles(path);

            foreach (var fileName in fileEntries)
            {
                var temp = fileName.Replace("\\", "/");
                var index = temp.LastIndexOf("/");
                var localPath = path;

                if (index > 0)
                    localPath += temp.Substring(index);

                var t = AssetDatabase.LoadAssetAtPath(localPath, typeof(T));

                if (t != null)
                    al.Add(t);
            }

            var result = new T[al.Count];

            for (int i = 0; i < al.Count; i++)
                result[i] = (T) al[i];

            return result;
        }
        
        public static bool PathIsDirectory (string absolutePath)
        {
            FileAttributes attr = File.GetAttributes(absolutePath);
            if ((attr & FileAttributes.Directory) == FileAttributes.Directory)
                return true;
            else
                return false;
        }

        public static string AssetsRelativePath (string absolutePath)
        {
            if (absolutePath.StartsWith(Application.dataPath)) {
                return "Assets" + absolutePath.Substring(Application.dataPath.Length);
            }
            else {
                throw new System.ArgumentException("Full path does not contain the current project's Assets folder", "absolutePath");
            }
        }

        public static string[] GetResourcesDirectories ()
        {
            List<string> result = new List<string>();
            Stack<string> stack = new Stack<string>();
            // Add the root directory to the stack
            stack.Push(Application.dataPath);
            // While we have directories to process...
            while (stack.Count > 0) {
                // Grab a directory off the stack
                string currentDir = stack.Pop();
                try {
                    foreach (string dir in Directory.GetDirectories(currentDir)) {
                        if (Path.GetFileName(dir).Equals("Resources")) {
                            // If one of the found directories is a Resources dir, add it to the result
                            result.Add(dir);
                        }
                        // Add directories at the current level into the stack
                        stack.Push(dir);
                    }
                }
                catch {
                    Debug.LogError("Directory " + currentDir + " couldn't be read from.");
                }
            }
            return result.ToArray();
        }
        
        #endif
    }
}