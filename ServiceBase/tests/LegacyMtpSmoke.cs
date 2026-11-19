using System;
using System.Collections.Generic;
using System.IO;
using MagicTavern.FrontEngine.MTPManager;
using MagicTavern.FrontEngine.MTPManager.LitJson;
using ServiceBase.UnityBridge;

internal static class UnknownOwner { }
internal class MetadataFixture
{
    public string displayName = "测试展示名", version = "1.2.3", description = "说明", unity = "2022.3";
    public string license = "MIT", changelog = "修复内容", url = "https://secret@example.test";
    public MetadataAuthor author = new MetadataAuthor();
}
internal class MetadataAuthor { public string name = "作者", email = "author@example.test"; }
internal static class LegacyMtpSmoke
{
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); }
    private static void Read(string root, MtpSnapshot result)
    {
        LegacyMtpReader.ReadCache(typeof(MTPManager), typeof(PackageDepInfoList), typeof(JsonUtils), root, result);
    }
    private static int Main(string[] args)
    {
        var metadata = new MtpPackageSnapshot();
        PackageMetadataReader.CopyCached(new MetadataFixture(), metadata);
        Check(metadata.display_name == "测试展示名" && metadata.version == "1.2.3" && metadata.author.Contains("author@example.test"), "metadata whitelist");
        Check(metadata.installation_status == "unknown" && !JsonMapper.ToJson(metadata).Contains("secret"), "metadata does not imply installed or expose URL");
        foreach (string version in new[] {"0.9.99", "1.2.99", "1.3.0", "1.3.18", "1.3.19"})
            Check(MtpPackageDetector.Classify(version, "").legacy, version);
        foreach (string version in new[] {"1.3.20", "1.4.0", "1.19.0", "2.0.0"})
            Check(!MtpPackageDetector.Classify(version, "").legacy, version);
        Check(MtpPackageDetector.Classify("9.0.0", "mtpmanager-1.3.19-debug").legacy, "tag takes priority");
        Check(!MtpPackageDetector.Classify("1.3.19", "mtpmanager-1.3.20").legacy, "new tag takes priority");
        Check(!MtpPackageDetector.Classify("unknown", "abc123").versionKnown, "unknown version");
        var root = Path.Combine(Path.GetTempPath(), "servicebase-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Library"));
        try {
        var cache = Path.Combine(root, "Library/upm-dependencies.json");
        var missing = new MtpSnapshot(); Read(root, missing);
        Check(missing.state == "error" && !File.Exists(cache), "missing cache does not create file");
        var incompatible = new MtpSnapshot();
        LegacyMtpReader.ReadCache(typeof(UnknownOwner), typeof(PackageDepInfoList), typeof(JsonUtils), root, incompatible);
        Check(incompatible.state == "incompatible", "unknown structure");
        var original = File.ReadAllText(args[0]);
        File.WriteAllText(cache, original);
        var data = JsonMapper.ToObject<PackageDepInfoList>(original);
        var result = new MtpSnapshot(); Read(root, result);
        Check(result.state == "ready" && result.packages.Count == data.Dependencies.Count, "real cache loader snapshot");
        Check(File.ReadAllText(cache) == original, "cache unchanged");
        Check(result.source == "Library/upm-dependencies.json", "cache source");
        foreach (var p in result.packages) {
            Check(p.tag == data.Dependencies[p.name].Revision, "legacy Revision is Tag");
            Check(p.revision == "", "no invented commit");
        }
        var fixture = new PackageDepInfoList();
        fixture.Dependencies["root"] = new PackageDepInfo {PackageName="root", Revision="root-1.0", Depth=0,
            Dependencies=new Dictionary<string,string>{{"dep", "https://oauth2:secret@example/repo.git#dep-2.0"}}};
        File.WriteAllText(cache, JsonMapper.ToJson(fixture));
        var safe = new MtpSnapshot(); Read(root, safe);
        Check(safe.packages[0].dependencies[0].requested_tag == "dep-2.0", "dependency tag");
        Check(!JsonMapper.ToJson(safe).Contains("secret"), "credential omission");
        File.WriteAllText(cache, "{broken");
        var corrupt = new MtpSnapshot(); Read(root, corrupt);
        Check(corrupt.state == "error" && corrupt.packages.Count == 0, "corrupt cache");
        File.WriteAllText(cache, "null");
        var empty = new MtpSnapshot(); Read(root, empty);
        Check(empty.state == "error", "null cache");
        File.WriteAllText(cache, JsonMapper.ToJson(new PackageDepInfoList()));
        var validEmpty = new MtpSnapshot(); Read(root, validEmpty);
        Check(validEmpty.state == "ready" && validEmpty.packages.Count == 0, "valid empty cache");
        Console.WriteLine("Legacy actual-model snapshot: " + result.packages.Count + " packages; version/missing/corrupt/empty/incompatible/tag/credential/read-only checks passed");
        var full = new MtpSnapshot {state="ready",adapter="legacy",packages=new List<MtpPackageSnapshot>(result.packages)};
        MtpCatalogReader.Read(Path.GetDirectoryName(Path.GetDirectoryName(args[0])),full);
        var expectedCatalog = JsonMapper.ToObject<ProjectInfoList>(File.ReadAllText(Path.Combine(Path.GetDirectoryName(args[0]),"upm-list.json"))).Packages.Count;
        Check(full.catalog_state == "ready" && full.catalog_count == expectedCatalog, "actual old full catalog");
        Check(full.packages.FindAll(p => p.installation_status == "installed").Count == result.packages.Count, "legacy installed join");
        Console.WriteLine("Legacy full catalog: " + full.catalog_count + "; installed: " + result.packages.Count);
        return 0;
        } finally { Directory.Delete(root, true); }
    }
}
