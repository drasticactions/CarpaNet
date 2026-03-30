# Working with Custom Lexicons

You can use your own or third-party lexicons beyond Bluesky's. Place JSON files in your project and reference them:

```xml
<ItemGroup>
  <LexiconFiles Include="lexicons/**/*.json" />

  <!-- Or resolve third-party lexicons by authority -->
  <LexiconResolveAuthority Include="blog.pckt" />
  <LexiconResolveAuthority Include="site.standard" />
</ItemGroup>
```

The source generator produces the same type-safe bindings for custom lexicons. Use the generated `FromJson()` method to parse records:

```csharp
var records = await client.ComAtprotoRepoListRecordsAsync(
    new ComAtproto.Repo.ListRecordsParameters
    {
        Repo = "did:plc:example",
        Collection = MyCustom.Namespace.MyRecord.RecordType,
    });

foreach (var record in records.Records)
{
    var parsed = MyCustom.Namespace.MyRecord.FromJson(record.Value);
    Console.WriteLine(parsed?.SomeField);
}
```
