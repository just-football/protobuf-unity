using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace E7.Protobuf
{
    internal static class ProtoPrefs
    {
        internal static readonly string prefProtocEnable = "ProtobufUnity_Enable";
        internal static readonly string prefProtocExecutable = "ProtobufUnity_ProtocExecutable";
        internal static readonly string prefGrpcPath = "ProtobufUnity_GrpcPath";
        internal static readonly string prefLogError = "ProtobufUnity_LogError";
        internal static readonly string prefLogStandard = "ProtobufUnity_LogStandard";

        internal static bool enabled
        {
            get => EditorPrefs.GetBool(prefProtocEnable, true);
            set => EditorPrefs.SetBool(prefProtocEnable, value);
        }

        internal static bool logError
        {
            get => EditorPrefs.GetBool(prefLogError, true);
            set => EditorPrefs.SetBool(prefLogError, value);
        }

        internal static bool logStandard
        {
            get => EditorPrefs.GetBool(prefLogStandard, false);
            set => EditorPrefs.SetBool(prefLogStandard, value);
        }

        private static string DesiredProcessorArchitecture => Environment.Is64BitOperatingSystem ? "x64" : "x86";

        private static string _packageDirectory;
        internal static string packageDirectory => _packageDirectory ?? 
                                                   (_packageDirectory = 
                                                       (new DirectoryInfo(Path.GetFullPath("Library/PackageCache"))
                                                           .GetDirectories("com.e7.protobuf-unity*", SearchOption.TopDirectoryOnly)
                                                           .FirstOrDefault() ?? 
                                                        throw new DirectoryNotFoundException("Could not find PackageCache for com.e7.protobuf-unity, is the package installed correctly?"))
                                                       .FullName);

        internal static string protoIncludePath => Path.Combine(packageDirectory, "Editor", ".protoc", "include");

        private static string _toolsFolder;
        internal static string toolsFolder
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_toolsFolder)) return _toolsFolder;
                string folder;
                switch (Application.platform)
                {
                    case RuntimePlatform.OSXEditor:
                        folder = "macosx";
                        break;
                    case RuntimePlatform.WindowsEditor:
                        folder = "windows";
                        break;
                    case RuntimePlatform.LinuxEditor:
                        folder = "linux";
                        break;
                    default:
                        throw new NotImplementedException(
                            $"Platform {Application.platform} is not supported by gRPC Tools");
                }

                return _toolsFolder = Path.Combine(packageDirectory, "Editor", ".protoc", "bin", $"{folder}_{DesiredProcessorArchitecture}");
            }
        }

        internal static string fileExtension => Application.platform is RuntimePlatform.WindowsEditor ? ".exe" : "";

        internal static string excPath => Path.Combine(toolsFolder, $"protoc{fileExtension}");

        internal static string grpcPath => Path.Combine(toolsFolder, $"grpc_csharp_plugin{fileExtension}");

        public class ProtoPrefsWindow : EditorWindow
        {
            internal static ProtoPrefsWindow instance = null;

            [MenuItem(@"Tools/Protobuf")]
            static void Init()
            {
                instance = GetWindow(typeof(ProtoPrefsWindow)) as ProtoPrefsWindow;
                instance.titleContent = new GUIContent("Protobuf Settings");
                instance.Show();
            }

            void OnGUI()
            {
                if (instance == null)
                {
                    Init();
                }

                EditorGUI.BeginChangeCheck();

                GUILayout.Label("Protobuf Settings", EditorStyles.boldLabel);
                EditorGUILayout.Space();

                enabled = EditorGUILayout.Toggle(new GUIContent("Enable Protobuf", ""), enabled);

                EditorGUI.BeginDisabledGroup(!enabled);

                EditorGUILayout.Space();

                logError = EditorGUILayout.Toggle(
                    new GUIContent("Log Error Output", "Log compilation errors from protoc command."), logError);

                logStandard =
                    EditorGUILayout.Toggle(new GUIContent("Log Standard Output", "Log compilation completion messages."),
                        logStandard);

                EditorGUILayout.Space();

                if (GUILayout.Button(new GUIContent("Force Compilation"))) ProtobufUnityCompiler.CompileAllInProject();

                EditorGUI.EndDisabledGroup();

                if (EditorGUI.EndChangeCheck())
                {
                }
            } 
        }
    }
}