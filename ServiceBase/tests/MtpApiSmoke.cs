// Optional integration harness: compile together with ServiceBaseBridge.cs and
// reference the target project's real MTP assembly. Runs outside Unity, so its
// startup/installation callbacks are never executed. Does not alter the project.
using System;
using System.IO;
using Editor.Data.Models;
using MagicTavern.FrontEngine.MTPManager;
using MagicTavern.FrontEngine.MTPManager.LitJson;
using ServiceBase.UnityBridge;

internal static class MtpApiSmoke
{
    private static int Main(string[] args)
    {
        var data = JsonMapper.ToObject<MTPackageInfoList>(File.ReadAllText(args[0]));
        var service = new MTPackageInfoService();
        foreach (var entry in data.Dependencies) service.AddDependency(entry.Value);
        ServiceLocator.Register<IMTPackageInfoService>(service);
        var result = MtpSnapshotReader.CaptureForInstallation(new MtpInstallation());
        if (result.state != "ready" || result.packages.Count != data.Dependencies.Count)
            throw new Exception("MTP export failed: " + result.state);
        var installedCount = result.packages.Count;
        var root = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(args[0])));
        var expectedCatalog = JsonMapper.ToObject<ProjectInfoList>(File.ReadAllText(Path.Combine(root,"Library/MTPCache/upm-list.json"))).Packages.Count;
        MtpCatalogReader.Read(root, result);
        if (result.catalog_state != "ready" || result.catalog_count != expectedCatalog || result.packages.Count <= installedCount)
            throw new Exception("Full catalog merge failed: " + result.catalog_state + " / " + result.catalog_count);
        if (result.packages.FindAll(p => p.installation_status == "installed").Count != installedCount)
            throw new Exception("Installed records lost in catalog merge");
        if (result.packages.FindAll(p => !string.IsNullOrEmpty(p.display_name)).Count != result.packages.Count)
            throw new Exception("Catalog display names missing");
        if (MtpCatalogReader.LatestTag(new System.Collections.Generic.List<string> { "test-1.9.0", "test-1.10.0", "test-2.0.0-debug" }) != "test-1.10.0")
            throw new Exception("Catalog semantic version ordering failed");
        File.WriteAllText(args[1], JsonMapper.ToJson(result));
        Console.WriteLine("Full 647 catalog: " + result.catalog_count + "; installed: " + installedCount + "; total: " + result.packages.Count);
        Console.WriteLine("Actual MTP API export passed: " + result.packages.Count + " packages");
        return 0;
    }
}
