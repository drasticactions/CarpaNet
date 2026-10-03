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

## Missing definitions

A lexicon can reference a definition that is not loaded, for example a published schema that
names a definition its authority never published. CarpaNet reports warning `ATPG002` and still
generates compilable code:

- A property, array item or query output that references the missing definition is typed as
  `System.Text.Json.JsonElement` and round-trips as raw JSON (and CBOR).
- A union member that references it is left out of the union. In an open union, a value with
  that `$type` becomes an `Unknown_*` member; in a closed union it is rejected.

Add the lexicon JSON file that defines it to get a typed property instead.
