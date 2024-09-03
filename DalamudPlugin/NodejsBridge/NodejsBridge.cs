using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Microsoft.JavaScript.NodeApi.Runtime;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace NodejsBridge;

public class NodejsBridge : IDalamudPlugin
{
    public const string LibnodeKey = "ProximityVoiceChat-libnode";
    public static NodejsPlatform NodejsPlatform = null!;

    [Serializable]
    public class IntPtrRef
    {
        public IntPtr ptr;
    }

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private GCHandle handle;

    public NodejsBridge()
    {
        PluginInterface.GetOrCreateData(LibnodeKey, () =>
        {
            var baseDir = PluginInterface.AssemblyLocation.DirectoryName!;
            string libnodePath = Path.Combine(baseDir, "libnode.dll");
            NodejsPlatform = new NodejsPlatform(libnodePath);
            return (object)NodejsPlatform;
            //handle = GCHandle.Alloc(NodejsPlatform, GCHandleType.Pinned);
            //return new IntPtrRef { ptr = GCHandle.ToIntPtr(handle) };
        });
        
        Log.Info("Nodejs Bridge loaded.");
    }

    public void Dispose()
    {
        PluginInterface.RelinquishData(LibnodeKey);
        //handle.Free();
    }
}
