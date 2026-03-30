# Repository & CAR File Reading

Read ATProtocol repositories from CAR (Content Addressable aRchive) files:

```csharp
using CarpaNet.Repo;

// Load from file
var repo = Repository.LoadFromFile("repository.car");

// Or from stream/bytes
var repo = Repository.Load(carStream);
var repo = Repository.Load(carBytes);

// Inspect
Console.WriteLine($"Owner: {repo.Did}");
Console.WriteLine($"Revision: {repo.Rev}");
Console.WriteLine($"Root CID: {repo.RootCid}");

// Low-level CAR block reading
using var reader = new CarReader(stream);
foreach (var block in reader.ReadBlocks())
{
    Console.WriteLine($"CID: {block.Cid}, Size: {block.Data.Length}");
}
```
