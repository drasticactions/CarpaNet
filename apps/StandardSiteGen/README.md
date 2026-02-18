# StandardSiteGen

This app is a demo of generating static HTML for [standard.site](https://standard.site) lexicon entries. The static HTML generators are LLMed (Claude, specifically).

## Resolving Lexicons

To fetch the lexicons, we're using CarpaNet.SourceGen's resolvers

```xml
 <CarpaNet_LexiconAutoResolve>true</CarpaNet_LexiconAutoResolve>
```

```xml
  <ItemGroup>
    <LexiconResolve Include="com.atproto.repo.listRecords" />
    <LexiconResolveAuthority Include="blog.pckt" />
    <LexiconResolveAuthority Include="pub.leaflet" />
    <LexiconResolveAuthority Include="site.standard" />
  </ItemGroup>
```

`LexiconResolve` fetches the specific `com.atproto.repo.listRecords` lexicon entries, and any entries it requests.


`LexiconResolveAuthority` fetches all of the listed lexicons by calling into ATProtocol and fetching all of the schemas they have attached, and generating them.