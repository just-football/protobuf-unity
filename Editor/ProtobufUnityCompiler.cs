using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Callbacks;
using Debug = UnityEngine.Debug;

#pragma warning disable 162
// ReSharper disable HeuristicUnreachableCode

namespace E7.Protobuf
{
    public static class ProtoEnumerableExtensions
    {
        /// <summary>
        ///     The files that will be compiled, applies filtering to remove undesirable files such as packages (readonly) or include files
        /// </summary>
        public static IEnumerable<string> CompiledFiles(this IEnumerable<string> protoFiles) => protoFiles.Where(s => !s.StartsWith(Path.GetFullPath("Library/PackageCache")));

        /// <summary>
        ///     Filters files which have already been compiled before
        /// </summary>
        /// <remarks>
        ///     Does not check whether the compiled file is !latest, and thus does not check if the file needs to be re-compiled
        /// </remarks>
        public static IEnumerable<string> UncompiledFiles(this IEnumerable<string> compiledFiles) => compiledFiles.Where(f => !f.StartsWith(Path.GetFullPath("Library/PackageCache")) && !File.Exists(Path.Combine(Path.GetDirectoryName(f), Path.GetFileNameWithoutExtension(f) + ".cs")));

        
        /// <summary>
        ///     A parent folder of all protobuf files found in your Unity project collected together.
        ///     This means all .proto files in Unity could import each other freely even if they are far apart.
        /// </summary>
        public static IEnumerable<string> IncludePaths(this IEnumerable<string> protoFiles) => protoFiles.Select(Path.GetDirectoryName).Concat(new[] {ProtoPrefs.protoIncludePath}).Distinct();
    }
    
    public class ProtobufUnityCompiler : AssetPostprocessor
    {
#if UNITY_CLOUD_BUILD
        private const bool ShouldMultithreadUncompiledFiles = false;

        [DidReloadScripts]
        public static void OnScriptsReloaded()
        {
            if (ProtoPrefs.enabled == false) return;
            ProtoPrefs.UpdateCachedVariables();
            CompileUncompiledFilesOnly();
        }
#else
        private const bool ShouldMultithreadUncompiledFiles = true;
#endif

        /// <summary>
        ///     Path to the file of all protobuf files in your Unity folder.
        /// </summary>
        public static IEnumerable<string> AllProtoFiles
        {
            get
            {
                var protoIncludeFiles = Path.Combine("com.e7.protobuf-unity", "Editor", ".protoc");
                return Directory.GetFiles(Environment.CurrentDirectory, "*.proto", SearchOption.AllDirectories)
                    .Where(s => !s.Contains(protoIncludeFiles));
            }
        }

        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets,
            string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (ProtoPrefs.enabled == false) return;
            ProtoPrefs.UpdateCachedVariables();

            new Thread(() =>
            {
                var compiledFilesList = importedAssets
                    .Where(f => Path.GetExtension(f) == ".proto")
                    .Select(Path.GetFullPath)
                    .CompiledFiles()
                    .ToList();
                if(!compiledFilesList.Any()) return;

                var includePaths = AllProtoFiles.IncludePaths().ToList();

                var logStandard = ProtoPrefs.logStandard;
                var logError = ProtoPrefs.logError;
                CompileAssets(compiledFilesList, includePaths, logStandard, logError);
                Debug.Log("Compiled changed .proto files");
                ProtoPrefs.QueueOnMainThread(AssetDatabase.Refresh);
            }).Start();
        }

        private static void CompileAssets(IEnumerable<string> compiledFiles, List<string> includePaths, bool logStandard, bool logError)
        {
            Parallel.ForEach(compiledFiles, str =>
            {
                CompileProtobufSystemPath(str, includePaths, logStandard, logError);
            });
        }

        /// <summary>
        ///     Force compiles all proto files and refreshes asset database
        /// </summary>
        public static void CompileAllInProject()
        {
            new Thread(() =>
            {
                if (ProtoPrefs.logStandard) Debug.Log("Protobuf Unity : Compiling all .proto files in the project...");

                var files = AllProtoFiles.ToList();
                var includePaths = files.IncludePaths().ToList();
                var compiledFiles = files.CompiledFiles().ToList();

                var logStandard = ProtoPrefs.logStandard;
                var logError = ProtoPrefs.logError;
                CompileAssets(compiledFiles, includePaths, logStandard, logError);

                Debug.Log("Compiled all .proto files");
                ProtoPrefs.QueueOnMainThread(AssetDatabase.Refresh);
            }).Start();
        }

        public static void CompileUncompiledFilesOnly()
        {
            void compileCode()
            {
                if (ProtoPrefs.logStandard) Debug.Log("Protobuf Unity : Compiling uncompiled .proto files in the project...");

                var files = AllProtoFiles.ToList();
                var includePaths = files.IncludePaths().ToList();
                var uncompiledFiles = files.UncompiledFiles().ToList();
                if (!uncompiledFiles.Any()) return;

                var logStandard = ProtoPrefs.logStandard;
                var logError = ProtoPrefs.logError;
                CompileAssets(uncompiledFiles, includePaths, logStandard, logError);

                Debug.Log("Compiled uncompiled .proto files");
                if (ShouldMultithreadUncompiledFiles)
                {
                    ProtoPrefs.QueueOnMainThread(AssetDatabase.Refresh);
                }
                else
                {
                    AssetDatabase.Refresh();
                }
            }

            if (ShouldMultithreadUncompiledFiles)
            {
                new Thread(compileCode).Start();
            }
            else
            {
                compileCode();
            }
        }

        private static bool CompileProtobufSystemPath(string protoFileSystemPath, IEnumerable<string> includePaths, bool logStandard, bool logError)
        {
            if (Path.GetExtension(protoFileSystemPath) == ".proto")
            {
                var outputPath = Path.GetDirectoryName(protoFileSystemPath);

                var options = " --csharp_out \"{0}\" ";
                foreach (var s in includePaths) options += $" --proto_path \"{s}\" ";

                // Checking if the user has set valid path (there is probably a better way)
                if (ProtoPrefs.grpcPath != "ProtobufUnity_GrpcPath" || ProtoPrefs.grpcPath != string.Empty)
                    options += $" --grpc_out={outputPath} --plugin=protoc-gen-grpc={ProtoPrefs.grpcPath}";
                //string combinedPath = string.Join(" ", optionFiles.Concat(new string[] { protoFileSystemPath }));

                var finalArguments = $"\"{protoFileSystemPath}\"" + string.Format(options, outputPath);
                
                if (logStandard) 
                    Debug.Log("Protobuf Unity : Final arguments :\n" + finalArguments);

                var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = ProtoPrefs.excPath,
                        Arguments = finalArguments,
                        UseShellExecute = false,
                        RedirectStandardOutput = false,
                        RedirectStandardError = false,
						CreateNoWindow = true
                    }
                };
                proc.Start();
                proc.WaitForExit();
                
                var output = "";
                var error = "";
                if (false)
                {
                    output = proc.StandardOutput.ReadToEnd();
                    error = proc.StandardError.ReadToEnd();
                }

                if (logStandard)
                {
                    if(proc.ExitCode == 0)
                        Debug.Log("Protobuf Unity : Compiled " + Path.GetFileName(protoFileSystemPath));
                    if (!string.IsNullOrWhiteSpace(output)) 
                        Debug.Log("Protobuf Unity : " + output);
                }

                if (logError)
                {
                    if(proc.ExitCode != 0)
                        Debug.LogError("[Error] Could not compile " + Path.GetFileName(protoFileSystemPath));
                    if(!string.IsNullOrWhiteSpace(error)) 
                        Debug.LogError(error);
                }
                return true;
            }

            return false;
        }
    }
}