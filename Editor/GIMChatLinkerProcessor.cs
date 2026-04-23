#if UNITY_EDITOR
using System.IO;
using UnityEditor.Build;
using UnityEngine;

namespace Gamania.GIMChat.Editor
{
    /// <summary>
    /// Injects GIMChat assembly preservation rules into the Unity linker during IL2CPP builds.
    /// Required because Unity does not automatically scan link.xml files inside UPM packages.
    /// </summary>
    internal class GIMChatLinkerProcessor : IUnityLinkerProcessor
    {
        int IOrderedCallback.callbackOrder => 0;

        public string GenerateAdditionalLinkXmlFile(UnityEditor.Build.Reporting.BuildReport report, UnityEditor.UnityLinker.UnityLinkerBuildPipelineData data)
        {
            var packageLinkXml = Path.GetFullPath("Packages/com.gamania.gim/link.xml");
            if (File.Exists(packageLinkXml))
            {
                Debug.Log($"[GIMChat] Generated link.xml at {packageLinkXml}");
                return packageLinkXml;
            }

            // Fallback: Assets/GIMChat (local/dev install)
            var assetsLinkXml = Path.GetFullPath("Assets/GIMChat/link.xml");
            if (File.Exists(assetsLinkXml))
            {
                Debug.Log($"[GIMChat] Generated link.xml at {assetsLinkXml}");
                return assetsLinkXml;
            }

            Debug.LogWarning("[GIMChat] link.xml not found, IL2CPP stripping may cause issues.");
            return null;
        }

        public void OnBeforeRun(UnityEditor.Build.Reporting.BuildReport report, UnityEditor.UnityLinker.UnityLinkerBuildPipelineData data) { }

        public void OnAfterRun(UnityEditor.Build.Reporting.BuildReport report, UnityEditor.UnityLinker.UnityLinkerBuildPipelineData data) { }
    }
}
#endif
