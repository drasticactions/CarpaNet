using System;
using System.IO;
using System.Linq;
using CarpaNet;
using CarpaNet.Repo;
using Xunit;

namespace CarpaNet.UnitTests.Repo;

public class RepositoryTests
{
    private static readonly string TestCarPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "..", "..", "..", "test.car");

    [Fact]
    public void LoadFromFile_ReadsCarHeader()
    {
        var repo = Repository.LoadFromFile(TestCarPath);

        Assert.NotNull(repo);
        Assert.True(repo.RootCid.IsValid);
        Assert.Single(repo.Roots);
    }

    [Fact]
    public void LoadFromFile_ReadsCommit()
    {
        var repo = Repository.LoadFromFile(TestCarPath);

        var commit = repo.Commit;
        Assert.NotNull(commit);
        Assert.NotEmpty(commit.Did);
        Assert.StartsWith("did:", commit.Did);
        Assert.True(commit.Version >= 2); // v2 or v3
        Assert.True(commit.Data.IsValid);
        Assert.NotEmpty(commit.Rev);
        Assert.NotEmpty(commit.Sig);
    }

    [Fact]
    public void LoadFromFile_ReadsRecords()
    {
        var repo = Repository.LoadFromFile(TestCarPath);

        var records = repo.Records;
        Assert.NotNull(records);
        Assert.NotEmpty(records);

        // All records should have valid paths (collection/recordKey format)
        foreach (var record in records.Values)
        {
            Assert.Contains("/", record.Path);
            Assert.NotEmpty(record.Collection);
            Assert.NotEmpty(record.RecordKey);
            Assert.True(record.Cid.IsValid);
            Assert.NotEmpty(record.Data);
        }
    }

    [Fact]
    public void LoadFromFile_GetCollections_ReturnsDistinctCollections()
    {
        var repo = Repository.LoadFromFile(TestCarPath);

        var collections = repo.GetCollections().ToList();
        Assert.NotEmpty(collections);

        // Collections should be unique
        Assert.Equal(collections.Count, collections.Distinct().Count());

        // Collections should be NSIDs (contain dots)
        foreach (var collection in collections)
        {
            Assert.Contains(".", collection);
        }
    }

    [Fact]
    public void LoadFromFile_GetRecordsByCollection_FiltersCorrectly()
    {
        var repo = Repository.LoadFromFile(TestCarPath);

        var collections = repo.GetCollections().ToList();
        if (collections.Count == 0)
            return; // Skip if no collections

        var firstCollection = collections.First();
        var records = repo.GetRecordsByCollection(firstCollection).ToList();

        Assert.NotEmpty(records);
        Assert.All(records, r => Assert.Equal(firstCollection, r.Collection));
    }

    [Fact]
    public void LoadFromFile_Records_HaveTypeField()
    {
        var repo = Repository.LoadFromFile(TestCarPath);

        var recordsWithType = repo.Records.Values
            .Where(r => r.Type != null)
            .ToList();

        // Most records should have a $type field
        Assert.NotEmpty(recordsWithType);

        foreach (var record in recordsWithType)
        {
            // Type is either an NSID (contains dots) or a special type like "blob"
            Assert.NotEmpty(record.Type!);
        }
    }

    [Fact]
    public void LoadFromFile_BlockCount_IsPositive()
    {
        var repo = Repository.LoadFromFile(TestCarPath);

        Assert.True(repo.BlockCount > 0);
    }

    [Fact]
    public void LoadFromFile_GetBlock_ReturnsDataForValidCid()
    {
        var repo = Repository.LoadFromFile(TestCarPath);

        // Should be able to get the commit block
        var commitData = repo.GetBlock(repo.RootCid);
        Assert.NotNull(commitData);
        Assert.NotEmpty(commitData);
    }

    [Fact]
    public void LoadFromFile_GetBlock_ReturnsNullForInvalidCid()
    {
        var repo = Repository.LoadFromFile(TestCarPath);

        var data = repo.GetBlock("bafynonexistent123456789");
        Assert.Null(data);
    }

    [Fact]
    public void LoadFromFile_CanAccessMstRoot()
    {
        var repo = Repository.LoadFromFile(TestCarPath);

        var mstRoot = repo.GetRootMstNode();
        Assert.NotNull(mstRoot);
        Assert.NotNull(mstRoot!.Entries);
    }

    [Fact]
    public void LoadFromFile_MstEntries_HaveValidKeys()
    {
        var repo = Repository.LoadFromFile(TestCarPath);

        var mstRoot = repo.GetRootMstNode();
        Assert.NotNull(mstRoot);

        foreach (var entry in mstRoot!.Entries)
        {
            Assert.NotEmpty(entry.Key);
            Assert.True(entry.Value.IsValid);
        }
    }

    [Fact]
    public void LoadFromFile_TryGetRecord_FindsExistingRecord()
    {
        var repo = Repository.LoadFromFile(TestCarPath);

        var records = repo.Records;
        if (records.Count == 0)
            return;

        var firstRecord = records.Values.First();
        var found = repo.TryGetRecord(firstRecord.Collection, firstRecord.RecordKey, out var record);

        Assert.True(found);
        Assert.NotNull(record);
        Assert.Equal(firstRecord.Path, record!.Path);
    }

    [Fact]
    public void LoadFromFile_TryGetRecord_ReturnsFalseForMissing()
    {
        var repo = Repository.LoadFromFile(TestCarPath);

        var found = repo.TryGetRecord("nonexistent.collection", "nonexistent-key", out var record);

        Assert.False(found);
        Assert.Null(record);
    }

    [Fact]
    public void ComputeMstDepth_ReturnsCorrectValues()
    {
        // These are test cases from the ATProtocol spec
        Assert.Equal(0, Repository.ComputeMstDepth("2653ae71"));
        Assert.Equal(1, Repository.ComputeMstDepth("blue"));
        Assert.Equal(4, Repository.ComputeMstDepth("app.bsky.feed.post/454397e440ec"));
        Assert.Equal(8, Repository.ComputeMstDepth("app.bsky.feed.post/9adeb165882c"));
    }
}

public class CarReaderTests
{
    private static readonly string TestCarPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "..", "..", "..", "test.car");

    [Fact]
    public void CarReader_ReadsHeader()
    {
        using var stream = File.OpenRead(TestCarPath);
        using var reader = new CarReader(stream);

        var header = reader.Header;
        Assert.Equal(1, header.Version);
        Assert.NotEmpty(header.Roots);
        Assert.True(header.Roots[0].IsValid);
    }

    [Fact]
    public void CarReader_ReadBlocks_ReturnsBlocks()
    {
        using var stream = File.OpenRead(TestCarPath);
        using var reader = new CarReader(stream);

        var blocks = reader.ReadBlocks().ToList();
        Assert.NotEmpty(blocks);

        foreach (var block in blocks)
        {
            Assert.True(block.Cid.IsValid);
            Assert.NotEmpty(block.Data);
        }
    }

    [Fact]
    public void CarReader_ReadAllBlocks_CreatesDictionary()
    {
        using var stream = File.OpenRead(TestCarPath);
        using var reader = new CarReader(stream);

        var blocks = reader.ReadAllBlocks();
        Assert.NotEmpty(blocks);

        // First root should be in the blocks
        var rootCid = reader.Header.Roots[0].Value;
        Assert.True(blocks.ContainsKey(rootCid));
    }

    [Fact]
    public void CarReader_FromByteArray_Works()
    {
        var data = File.ReadAllBytes(TestCarPath);
        using var reader = new CarReader(data);

        var header = reader.Header;
        Assert.Equal(1, header.Version);
    }
}

public class RepoCommitTests
{
    private static readonly string TestCarPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "..", "..", "..", "test.car");

    [Fact]
    public void RepoCommit_FromCbor_ParsesCorrectly()
    {
        var repo = Repository.LoadFromFile(TestCarPath);
        var commitData = repo.GetBlock(repo.RootCid);
        Assert.NotNull(commitData);

        var commit = RepoCommit.FromCbor(commitData!);

        Assert.NotEmpty(commit.Did);
        Assert.True(commit.Version >= 2);
        Assert.True(commit.Data.IsValid);
        Assert.NotEmpty(commit.Rev);
        Assert.NotEmpty(commit.Sig);
    }
}

public class MstNodeTests
{
    private static readonly string TestCarPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "..", "..", "..", "test.car");

    [Fact]
    public void MstNode_FromCbor_ParsesCorrectly()
    {
        var repo = Repository.LoadFromFile(TestCarPath);
        var mstRootCid = repo.Commit.Data;
        var mstData = repo.GetBlock(mstRootCid);
        Assert.NotNull(mstData);

        var node = MstNode.FromCbor(mstData!);

        Assert.NotNull(node.Entries);
    }

    [Fact]
    public void MstEntry_PrefixCompression_DecompressesCorrectly()
    {
        var repo = Repository.LoadFromFile(TestCarPath);
        var records = repo.Records;

        // Verify that all record paths are valid (properly decompressed)
        foreach (var record in records.Values)
        {
            // Path should be collection/recordKey format
            var parts = record.Path.Split('/');
            Assert.Equal(2, parts.Length);

            // Collection should be an NSID
            Assert.Contains(".", parts[0]);

            // Record key should not be empty
            Assert.NotEmpty(parts[1]);
        }
    }

    [Fact]
    public void MstNode_GetEntries_ReturnsKeyValuePairs()
    {
        var repo = Repository.LoadFromFile(TestCarPath);
        var mstRoot = repo.GetRootMstNode();
        Assert.NotNull(mstRoot);

        var entries = mstRoot!.GetEntries().ToList();

        foreach (var (key, value) in entries)
        {
            Assert.NotEmpty(key);
            Assert.True(value.IsValid);
        }
    }
}
