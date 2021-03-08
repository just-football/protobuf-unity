using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace E7.Protobuf
{
    [InitializeOnLoad]
    internal static class ProtoPrefs
    {
        private static readonly Thread MainThread;
        private static readonly ConcurrentQueue<Action> mainThreadQueue = new ConcurrentQueue<Action>();

        static ProtoPrefs()
        {
            MainThread = Thread.CurrentThread;
            EditorApplication.update += Update;
            UpdateCachedVariables();
        }

        static void Update ()
        {
            while (mainThreadQueue.TryDequeue(out var a))
                a();
        }

        internal static void QueueOnMainThread(Action a) => mainThreadQueue.Enqueue(a);

        internal static void UpdateCachedVariables()
        {
            //Caches preferences
            var o = typeof(ProtoPrefs)
            .GetFields(BindingFlags.Static)
            .Select(func => func.GetValue(null))
            .ToList();
        }

        private static T UnityCacheHelper<T>(ref T localVariable, Func<T> unityMainThreadAction)
        {
            if (Thread.CurrentThread == MainThread)
                return localVariable = unityMainThreadAction();
            return localVariable;
        }

        internal static readonly string prefProtocEnable = "ProtobufUnity_Enable";
        internal static readonly string prefProtocExecutable = "ProtobufUnity_ProtocExecutable";
        internal static readonly string prefGrpcPath = "ProtobufUnity_GrpcPath";
        internal static readonly string prefLogError = "ProtobufUnity_LogError";
        internal static readonly string prefLogStandard = "ProtobufUnity_LogStandard";

        internal static bool _lastEnabled;
        internal static bool enabled
        {
            get => UnityCacheHelper(ref _lastEnabled, () => EditorPrefs.GetBool(prefProtocEnable, true));
            set => EditorPrefs.SetBool(prefProtocEnable, value);
        }
        
        internal static bool _lastLogError;
        internal static bool logError
        {
            get => UnityCacheHelper(ref _lastEnabled, () => EditorPrefs.GetBool(prefLogError, true));
            set => EditorPrefs.SetBool(prefLogError, value);
        }
        
        internal static bool _lastLogStandard;
        internal static bool logStandard
        {
            get => UnityCacheHelper(ref _lastEnabled, () => EditorPrefs.GetBool(prefLogStandard, false));
            set => EditorPrefs.SetBool(prefLogStandard, value);
        }

        private static string DesiredProcessorArchitecture => Environment.Is64BitOperatingSystem ? "x64" : "x86";

        private static string _packageDirectory;
        internal static string packageDirectory
        {
            get
            {
                if (_packageDirectory != null) return _packageDirectory;
                var packageFolder = new DirectoryInfo(Path.GetFullPath("Library/PackageCache"))
                    .GetDirectories("com.e7.protobuf-unity*", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault();

                if (packageFolder == null || !packageFolder.Exists)
                {
                    throw new DirectoryNotFoundException("Could not find PackageCache for com.e7.protobuf-unity, is the package installed correctly?");
                }
                return _packageDirectory = packageFolder.FullName;
            }
        }

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

        internal static string _lastFileExtension = null;

        internal static string fileExtension
        {
            get
            {
                if (_lastFileExtension != null) return _lastFileExtension;
                return _lastFileExtension = Application.platform is RuntimePlatform.WindowsEditor ? ".exe" : "";
            }
        }

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

                if (GUILayout.Button(new GUIContent("Force Compilation")))
                {
                    UpdateCachedVariables();
                    ProtobufUnityCompiler.CompileAllInProject();
                }

                EditorGUI.EndDisabledGroup();

                if (EditorGUI.EndChangeCheck())
                {
                }
            } 
        }
    }
}