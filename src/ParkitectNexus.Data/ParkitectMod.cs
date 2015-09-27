﻿// ParkitectNexusClient
// Copyright 2015 Parkitect, Tim Potze

using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Microsoft.CSharp;
using Newtonsoft.Json;

namespace ParkitectNexus.Data
{
    [JsonObject(MemberSerialization.OptIn)]
    public class ParkitectMod
    {
        private static readonly string[] SystemAssemblies =
        {
            "System", "System.Core", "System.Data", "System.Xml",
            "System.Xml.Linq", "Microsoft.CSharp", "System.Data.DataSetExtensions", "System.Net.Http"
        };

        [JsonProperty]
        public string AssetBundleDir { get; set; }

        [JsonProperty]
        public string AssetBundlePrefix { get; set; }

        [JsonProperty]
        public string BaseDir { get; set; }

        [JsonProperty]
        public string ClassName { get; set; }

        [JsonProperty]
        public string CompilerVersion { get; set; } = "v4.0";

        [JsonProperty]
        public IList<string> CodeFiles { get; set; } = new List<string>();

        [JsonProperty]
        public string DevelopmentBuildPath { get; set; }

        [JsonProperty]
        public bool IsDevelopment { get; set; }

        [JsonProperty]
        public bool IsEnabled { get; set; }

        public bool IsInstalled
            => !string.IsNullOrWhiteSpace(Path) && File.Exists(System.IO.Path.Combine(Path, "mod.json"));

        [JsonProperty]
        public string Name { get; set; }

        [JsonProperty]
        public string NameSpace { get; set; }

        public string Path { get; set; }

        [JsonProperty]
        public string Project { get; set; }

        [JsonProperty]
        public string Tag { get; set; }

        [JsonProperty]
        public IList<string> ReferencedAssemblies { get; set; } = new List<string>();

        [JsonProperty]
        public string Repository { get; set; }

        private string ResolveAssembly(Parkitect parkitect, string assemblyName)
        {
            if (parkitect == null) throw new ArgumentNullException(nameof(parkitect));
            if (assemblyName == null) throw new ArgumentNullException(nameof(assemblyName));

            var dllName = $"{assemblyName}.dll";
            if (SystemAssemblies.Contains(assemblyName))
                return dllName;

            if (parkitect.ManagedAssemblyNames.Contains(dllName))
                return System.IO.Path.Combine(parkitect.ManagedDataPath, dllName);

            var modPath = System.IO.Path.Combine(Path, BaseDir ?? "", dllName);
            if (File.Exists(System.IO.Path.Combine(modPath)))
                return modPath;

            throw new Exception($"Failed to resolve referenced assembly '{assemblyName}'");
        }

        #region Overrides of Object

        /// <summary>
        ///     Returns a string that represents the current object.
        /// </summary>
        /// <returns>
        ///     A string that represents the current object.
        /// </returns>
        public override string ToString()
        {
            return Name;
        }

        #endregion

        public StreamWriter OpenLog()
        {
            return !IsInstalled ? null : File.AppendText(System.IO.Path.Combine(Path, "mod.log"));
        }

        public void Save()
        {
            if (!IsInstalled)
                throw new Exception("Not installed");

            File.WriteAllText(System.IO.Path.Combine(Path, "mod.json"), JsonConvert.SerializeObject(this));
        }

        public void Delete()
        {
            if (!IsInstalled) throw new Exception("mod not installed");
            DeleteFileSystemInfo(new DirectoryInfo(Path));
        }

        private static void DeleteFileSystemInfo(FileSystemInfo fileSystemInfo)
        {
            var directoryInfo = fileSystemInfo as DirectoryInfo;
            if (directoryInfo != null)
            {
                foreach (var childInfo in directoryInfo.GetFileSystemInfos())
                {
                    DeleteFileSystemInfo(childInfo);
                }
            }

            fileSystemInfo.Attributes = FileAttributes.Normal;
            fileSystemInfo.Delete();
        }

        public bool CopyAssetBundles(Parkitect parkitect)
        {
            if (parkitect == null) throw new ArgumentNullException(nameof(parkitect));
            if (!IsInstalled) throw new Exception("mod not installed");
            if (AssetBundleDir == null) return true;
            if (AssetBundlePrefix == null) throw new Exception("AssetBundlePrefix is required when an AssetBundleDir is set");

            using (var logFile = OpenLog())
            {
                try
                {
                    string modAssetBundlePath = System.IO.Path.Combine(parkitect.InstallationPath,
                        "Parkitect_Data/StreamingAssets/mods", AssetBundlePrefix);

                    // Delete existing compiled file if compilation is forced.
                    if (Directory.Exists(System.IO.Path.Combine(modAssetBundlePath)))
                    {
                        if (IsDevelopment)
                            Directory.Delete(System.IO.Path.Combine(modAssetBundlePath), true);
                        else return true;
                    }

                    if (AssetBundleDir != null)
                    {
                        if (!Directory.Exists(modAssetBundlePath))
                            Directory.CreateDirectory(modAssetBundlePath);

                        foreach (
                            string assetBundleFile in Directory.GetFiles(System.IO.Path.Combine(Path, AssetBundleDir)))
                        {
                            File.Copy(assetBundleFile,
                                System.IO.Path.Combine(modAssetBundlePath, System.IO.Path.GetFileName(assetBundleFile)));
                        }
                    }
                }
                catch (Exception e)
                {
                    logFile.Log(e.Message, LogLevel.Error);
                    return false;
                }
            }

            return true;
        }

        private static string RandomString(int length)
        {
            const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
            var random = new Random();
            return new string(Enumerable.Range(0, length).Select(i => chars[random.Next(chars.Length)]).ToArray());
        }

        public bool Compile(Parkitect parkitect)
        {
            if (parkitect == null) throw new ArgumentNullException(nameof(parkitect));
            if (!IsInstalled) throw new Exception("mod not installed");


            using (var logFile = OpenLog())
            {
                try
                {
                    // Delete old builds.
                    var binPath = System.IO.Path.Combine(Path, "bin");
                    if (Directory.Exists(binPath))
                    {
                        foreach (var build in Directory.GetFiles(binPath, "build-*.dll"))
                            try
                            {
                                File.Delete(build);
                            }
                            catch
                            {
                            }
                    }

                    // Compute build path
                    var compilePath = "compile.dll";

                    if (IsDevelopment)
                    {
                        Directory.CreateDirectory(binPath);
                        do
                        {
                            DevelopmentBuildPath =
                                $"bin/build-{DateTime.Now.ToString("yyMMddHHmmss")}{RandomString(2)}.dll";
                        } while (File.Exists(DevelopmentBuildPath));

                        compilePath = DevelopmentBuildPath;
                        Save();
                    }

                    // Delete existing compiled file if compilation is forced.
                    if (File.Exists(compilePath))
                    {
                        return true;
                    }

                    logFile.Log($"Compiling {Name} to {compilePath}...");
                    logFile.Log($"Entry point: {NameSpace}.{ClassName}.");

                    var assemblyFiles = new List<string>();
                    var sourceFiles = new List<string>();

                    var csProjPath = Project == null ? null : System.IO.Path.Combine(Path, BaseDir ?? "", Project);

                    List<string> unresolvedAssemblyReferences;
                    List<string> unresolvedSourceFiles;

                    if (csProjPath != null)
                    {
                        // Load source files and referenced assemblies from *.csproj file.
                        logFile.Log($"Compiling from `{Project}`.");

                        // Open the .csproj file of the mod.
                        var document = new XmlDocument();
                        document.Load(csProjPath);

                        var manager = new XmlNamespaceManager(document.NameTable);
                        manager.AddNamespace("x", "http://schemas.microsoft.com/developer/msbuild/2003");

                        // List the referenced assemblies of the mod.
                        unresolvedAssemblyReferences = document.SelectNodes("//x:Reference", manager)
                            .Cast<XmlNode>()
                            .Select(node => node.Attributes["Include"])
                            .Select(name => name.Value.Split(',').FirstOrDefault()).ToList();

                        // List the source files of the mod.
                        unresolvedSourceFiles = document.SelectNodes("//x:Compile", manager)
                            .Cast<XmlNode>()
                            .Select(node => node.Attributes["Include"].Value).ToList();
                    }
                    else
                    {
                        // Load source files and referenced assemblies from mod.json file.
                        logFile.Log("Compiling from `mod.json`.");

                        unresolvedAssemblyReferences = ReferencedAssemblies.ToList();
                        unresolvedSourceFiles = CodeFiles.ToList();
                    }

                    // Resolve the assembly references.
                    foreach (var name in unresolvedAssemblyReferences)
                    {
                        var resolved = ResolveAssembly(parkitect, name);
                        assemblyFiles.Add(resolved);

                        logFile.Log($"Resolved assembly reference `{name}` to `{resolved}`");
                    }

                    // Resolve the source file paths.
                    logFile.Log($"Source files: {String.Join(", ", unresolvedSourceFiles)}.");
                    sourceFiles.AddRange(
                        unresolvedSourceFiles.Select(file => System.IO.Path.Combine(Path, BaseDir ?? "", file)));

                    // Compile.
                    var csCodeProvider =
                        new CSharpCodeProvider(new Dictionary<string, string> {{"CompilerVersion", CompilerVersion}});
                    var parameters = new CompilerParameters(assemblyFiles.ToArray(),
                        System.IO.Path.Combine(Path, compilePath));

                    var result = csCodeProvider.CompileAssemblyFromFile(parameters, sourceFiles.ToArray());

                    // Log errors.
                    foreach (var error in result.Errors.Cast<CompilerError>())
                        logFile.Log(
                            $"{error.ErrorNumber}: {error.Line}:{error.Column}: {error.ErrorText} in {error.FileName}",
                            LogLevel.Error);

                    return !result.Errors.HasErrors;
                }
                catch (Exception e)
                {
                    logFile.Log(e.Message, LogLevel.Error);
                    return false;
                }
            }
        }
    }
}