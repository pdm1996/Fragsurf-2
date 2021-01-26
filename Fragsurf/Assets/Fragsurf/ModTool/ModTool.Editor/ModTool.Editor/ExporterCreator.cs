﻿using System.Collections.Generic;
using System.Reflection;
using System.IO;
using UnityEngine;
using UnityEditor;
using ModTool.Shared;
using ModTool.Shared.Editor;

//Note: ModTool uses an old version of Mono.Cecil in the editor
#pragma warning disable CS0618

namespace ModTool.Editor
{
    internal class ExporterCreator
    {

        private static List<string> _builtInPaths = new List<string>()
        {
            "Assets\\Fragsurf\\Export",
            "Assets\\Fragsurf\\RealtimeCSG",
            "Assets\\Fragsurf\\Game\\Plugins\\Facepunch.Steamworks\\redistributable_bin",
            "Assets\\Fragsurf\\Game\\Plugins\\FMOD\\x86",
            "Assets\\Fragsurf\\Game\\Plugins\\FMOD\\x86_64",
        };

        /// <summary>
        /// Create a mod exporter package for this game.
        /// </summary>
        [MenuItem("Fragsurf/ModTool/Create Exporter")]
        public static void CreateExporter()
        {
            CreateExporter(Directory.GetCurrentDirectory(), true);
        }

        /// <summary>
        /// Create a mod exporter package after building the game.
        /// </summary>
        [UnityEditor.Callbacks.PostProcessBuild]
        public static void CreateExporterPostBuild(BuildTarget target, string pathToBuiltProject)
        {
            pathToBuiltProject = Path.GetDirectoryName(pathToBuiltProject);

            CreateExporter(pathToBuiltProject);
        }
        
        private static void CreateExporter(string path, bool revealPackage = false)
        {
            LogUtility.LogInfo("Creating Exporter");

            UpdateSettings();

            var codeSettings = CodeSettings.instance;
            var modToolSettings = ModToolSettings.instance;

            string modToolDirectory = AssetUtility.GetModToolDirectory();
            string exporterPath = Path.Combine(modToolDirectory, Path.Combine("Editor", "ModTool.Exporting.Editor.dll"));
            string fileName = Path.Combine(path, Application.productName + " Mod Tools.unitypackage");
            string projectSettingsDirectory = "ProjectSettings";

            List<string> assetPaths = new List<string>
            {
                AssetDatabase.GetAssetPath(codeSettings),
                AssetDatabase.GetAssetPath(modToolSettings),
                Path.Combine(modToolDirectory, Path.Combine("Editor", "ModTool.Exporting.Editor.dll")),
                Path.Combine(modToolDirectory, Path.Combine("Editor", "ModTool.Shared.Editor.dll")),
                Path.Combine(modToolDirectory, "ModTool.Shared.dll"),
                Path.Combine(modToolDirectory, "ModTool.Shared.xml"),
                Path.Combine(modToolDirectory, "ModTool.Interface.dll"),
                Path.Combine(modToolDirectory, "ModTool.Interface.xml"),
                Path.Combine(projectSettingsDirectory, "InputManager.asset"),
                Path.Combine(projectSettingsDirectory, "TagManager.asset"),
                Path.Combine(projectSettingsDirectory, "Physics2DSettings.asset"),
                Path.Combine(projectSettingsDirectory, "DynamicsManager.asset")
            };

            assetPaths.AddRange(CodeSettings.customPaths);
            assetPaths.AddRange(_builtInPaths);

            SetPluginEnabled(exporterPath, true);

            List<string> assemblyPaths = new List<string>();

            GetApiAssemblies("Assets", assemblyPaths);
            GetApiAssemblies("Library", assemblyPaths);

            assetPaths.AddRange(assemblyPaths);

            AssetDatabase.ExportPackage(assetPaths.ToArray(), fileName, ExportPackageOptions.Default | ExportPackageOptions.Recurse);

            foreach (string assemblyPath in assemblyPaths)
                AssetDatabase.DeleteAsset(assemblyPath);

            SetPluginEnabled(exporterPath, false);

            if(revealPackage)
                EditorUtility.RevealInFinder(fileName);
        }

        private static void SetPluginEnabled(string pluginPath, bool enabled)
        {
            PluginImporter pluginImporter = AssetImporter.GetAtPath(pluginPath) as PluginImporter;

            if (!pluginImporter || pluginImporter.GetCompatibleWithEditor() == enabled)
                return;

            pluginImporter.SetCompatibleWithEditor(enabled);
            pluginImporter.SaveAndReimport();
        }

        private static void GetApiAssemblies(string path, List<string> assemblies)
        {
            List<string> assemblyPaths = AssemblyUtility.GetAssemblies(path, AssemblyFilter.ApiAssemblies | AssemblyFilter.ModToolAssemblies);
            
            string modToolDirectory = AssetUtility.GetModToolDirectory();
            
            foreach(string assemblyPath in assemblyPaths)
            {
                string fileName = Path.GetFileName(assemblyPath);
                string newPath = Path.Combine(modToolDirectory, fileName);

                File.Copy(assemblyPath, newPath, true);
                AssetDatabase.ImportAsset(newPath);

                assemblies.Add(newPath);
            }            
        }   
        
        private static void UpdateSettings()
        {
            if (string.IsNullOrEmpty(ModToolSettings.productName) || ModToolSettings.productName != Application.productName)
            {
                typeof(ModToolSettings).GetField("_productName", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(ModToolSettings.instance, Application.productName);
            }

            if (string.IsNullOrEmpty(ModToolSettings.unityVersion) || ModToolSettings.unityVersion != Application.unityVersion)
            {
                typeof(ModToolSettings).GetField("_unityVersion", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(ModToolSettings.instance, Application.unityVersion);
            }

            EditorUtility.SetDirty(ModToolSettings.instance);
        }
    }
}