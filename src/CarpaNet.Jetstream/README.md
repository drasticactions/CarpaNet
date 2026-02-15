# CarpaNet.Jetstream

[![NuGet Version](https://img.shields.io/nuget/v/CarpaNet.Jetstream.svg)](https://www.nuget.org/packages/CarpaNet.Jetstream/) ![License](https://img.shields.io/badge/License-MIT-blue.svg)

![CarpaNet Logo](https://user-images.githubusercontent.com/898335/253740405-4b0ae177-cc49-4c26-b6b0-ab8e835a0e62.png)

CarpaNet.Jetstream lets you connect to a Bluesky Jetstream instance.

![1444070256569233](https://user-images.githubusercontent.com/898335/167266846-1ad2648f-91c1-4a04-a18d-6dd4d6c7d21c.gif)

This library is experimental and not stable. Expect issues and bugs!

# How to use

```csharp

byte[]? zstdDictionary = null;
if (zstdDictionaryPath != null)
{
    zstdDictionary = File.ReadAllBytes(zstdDictionaryPath);
    Console.WriteLine($"Loaded zstd dictionary from {zstdDictionaryPath} ({zstdDictionary.Length} bytes)");
}
else if (compress)
{
    Console.WriteLine("Warning: --compress specified without --zstd-dictionary. Binary frames will fail to decompress.");
}

using var client = new JetstreamClient(new Uri(endpoint), zstdDictionary);

var options = new JetstreamSubscribeOptions
{
    Cursor = cursor,
    WantedCollections = collections.Count > 0 ? collections : null,
    WantedDids = dids.Count > 0 ? dids : null,
    Compress = compress,
};

try
{
    await foreach (var evt in client.SubscribeAsync(options, cts.Token))
    {
        switch (evt.Kind)
        {
            // ... switch on events...
        }
    }
}
```