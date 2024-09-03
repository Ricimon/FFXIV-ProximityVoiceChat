using Dalamud.Plugin;
using Microsoft.JavaScript.NodeApi;
using Microsoft.JavaScript.NodeApi.Runtime;
using ProximityVoiceChat.Log;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace ProximityVoiceChat.WebRTC;

public class WebRTCManager_NodeApi : IDisposable
{
    public IReadOnlyDictionary<string, Peer> Peers => new Dictionary<string, Peer>();

    private NodejsPlatform? nodejsPlatform;
    private NodejsEnvironment? nodejs;

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ILogger logger;

    public WebRTCManager_NodeApi(IDalamudPluginInterface pluginInterface, ILogger logger)
    {
        this.pluginInterface = pluginInterface;
        this.logger = logger;

        //var baseDir = pluginInterface.AssemblyLocation.DirectoryName!;
        //this.logger.Debug("Assembly directory: {0}", baseDir);
        //string libnodePath = Path.Combine(baseDir, "libnode.dll");
        //this.nodejsPlatform = new NodejsPlatform(libnodePath);
        //this.nodejs = nodejsPlatform.CreateEnvironment(baseDir);

        //if (Debugger.IsAttached)
        //{
        //    var pid = Process.GetCurrentProcess().Id;
        //    var inspectionUri = this.nodejs.StartInspector();
        //    this.logger.Info("Node.js ({0}) inspector listening at {1}", pid, inspectionUri);
        //}
    }

    public void Dispose()
    {
        this.logger.Debug("Disposing NodeJS runtime");
        this.nodejs?.Dispose();
        //this.nodejsPlatform?.Dispose();
        this.pluginInterface.RelinquishData(NodejsBridge.NodejsBridge.LibnodeKey);
    }

    public void Test()
    {
        this.logger.Debug(NodejsPlatform.Current?.ToString() ?? "null");
        this.logger.Debug(NodejsBridge.NodejsBridge.NodejsPlatform?.ToString() ?? "null");

        //var obj = this.pluginInterface.GetData<object>(NodejsBridge.NodejsBridge.LibnodeKey);
        //this.logger.Debug("Obj: {0}", obj?.ToString() ?? "null");

        //var handle = GCHandle.FromIntPtr(intptr.ptr);
        var baseDir = pluginInterface.AssemblyLocation.DirectoryName!;

        this.nodejsPlatform = (NodejsPlatform)this.pluginInterface.GetOrCreateData(NodejsBridge.NodejsBridge.LibnodeKey, () =>
        {
            var baseDir = this.pluginInterface.AssemblyLocation.DirectoryName!;
            string libnodePath = Path.Combine(baseDir, "libnode.dll");
            var nodejsPlatform = new NodejsPlatform(libnodePath);
            return (object)nodejsPlatform;
        });
        //var nodejsBridgeAssemblyPath = Path.Combine(baseDir, "NodejsBridge.dll");
        //var assembly = AppDomain.CurrentDomain.Load(AssemblyName.GetAssemblyName(nodejsBridgeAssemblyPath));

        this.logger.Debug(this.nodejsPlatform?.ToString() ?? "null");

        return;

        //this.nodejs = this.nodejsPlatform.CreateEnvironment(this.assemblyDirectory);


        this.nodejs?.Run(() =>
        {
            var jsString = (string)JSValue.CreateStringUtf8(Encoding.UTF8.GetBytes("JS String!"));
            this.logger.Debug(jsString);
        });
    }
}
