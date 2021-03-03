using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Callbacks;
using Debug = UnityEngine.Debug;

namespace E7.Protobuf
{
    public class ProtobufUnityCompiler : AssetPostprocessor
    {
        [DidReloadScripts]
        public static void OnScriptsReloaded()
        {
            CompileAllInProject();
        }

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

        /// <summary>
        ///     The files that will be compiled, applies filtering to remove undesirable files such as packages (readonly) or include files
        /// </summary>
        public static IEnumerable<string> GetCompiledFiles(IEnumerable<string> protoFiles)
        {
            var packageCachePath = Path.GetFullPath("Library/PackageCache");
            return protoFiles.Where(s => !s.StartsWith(packageCachePath));
        }

        /// <summary>
        ///     A parent folder of all protobuf files found in your Unity project collected together.
        ///     This means all .proto files in Unity could import each other freely even if they are far apart.
        /// </summary>
        public static IEnumerable<string> GetIncludePaths(IEnumerable<string> protoFiles)
        {
            return protoFiles.Select(Path.GetDirectoryName)
                .Concat(new[] {ProtoPrefs.protoIncludePath}).Distinct();
        }

        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets,
            string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (ProtoPrefs.enabled == false) return;

            var compiledAssets = importedAssets
                .Where(f => Path.GetExtension(f) == ".proto")
                .Select(Path.GetFullPath);
            compiledAssets = GetCompiledFiles(compiledAssets);
            var compiledFilesList = compiledAssets.ToList();
            if(!compiledFilesList.Any()) return;

            var includePaths = GetIncludePaths(AllProtoFiles).ToList();

            var logStandard = ProtoPrefs.logStandard;
            var logError = ProtoPrefs.logError;
            CompileAssets(compiledFilesList, includePaths, logStandard, logError);

            /*
            for (int i = 0; i < movedAssets.Length; i++)
            {
                CompileProtobufAssetPath(movedAssets[i]);
            }
            */

            Debug.Log("Compiled changed .proto files");
            AssetDatabase.Refresh();
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
            if (ProtoPrefs.logStandard) Debug.Log("Protobuf Unity : Compiling all .proto files in the project...");

            var files = AllProtoFiles.ToList();
            var includePaths = GetIncludePaths(files).ToList();
            var compiledFiles = GetCompiledFiles(files).ToList();
            
            var logStandard = ProtoPrefs.logStandard;
            var logError = ProtoPrefs.logError;
            CompileAssets(compiledFiles, includePaths, logStandard, logError);

            Debug.Log("Compiled all .proto files");
            AssetDatabase.Refresh();
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
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
						CreateNoWindow = true
                    }
                };
                proc.Start();
                proc.WaitForExit();

                var output = proc.StandardOutput.ReadToEnd();
                var error = proc.StandardError.ReadToEnd();

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