using System;
using System.Collections.Generic;
using System.Linq;
using CarpaNet.Models;
using CarpaNet.Utilities;

namespace CarpaNet.Generation;

/// <summary>
/// Generates CBOR serialization context and type info classes.
/// Follows the same structural pattern as JsonContextGenerator: all CborTypeInfo classes
/// are emitted into a single namespace with encoded class names (e.g., AppBsky_Actor_DefsProfileViewBasicCborTypeInfo).
/// </summary>
public static class CborContextGenerator
{
    /// <summary>
    /// Converts a fully-qualified type name to a safe class name suffix.
    /// Replaces dots with underscores, strips @ escapes.
    /// Example: "AppBsky.Actor.DefsProfileViewBasic" -> "AppBsky_Actor_DefsProfileViewBasic"
    /// </summary>
    public static string ToClassSuffix(string qualifiedTypeName)
    {
        return qualifiedTypeName.Replace(".", "_").Replace("@", "");
    }

    /// <summary>
    /// Generates a CborObjectTypeInfo subclass for an object or record type.
    /// </summary>
    /// <param name="sb">Source builder.</param>
    /// <param name="qualifiedTypeName">Fully qualified C# type name (e.g., "AppBsky.Actor.DefsProfileViewBasic").</param>
    /// <param name="classSuffix">Encoded name for the CborTypeInfo class (no dots).</param>
    /// <param name="def">Lexicon definition for the type.</param>
    /// <param name="currentNsid">Current NSID context.</param>
    /// <param name="registry">Type registry.</param>
    /// <param name="options">Generator options.</param>
    /// <param name="isRecord">Whether this is a record type with $type discriminator.</param>
    /// <param name="generatedTypes">Set to track already-generated types.</param>
    public static void GenerateCborTypeInfo(
        SourceBuilder sb,
        string qualifiedTypeName,
        string classSuffix,
        LexiconDefinition def,
        string currentNsid,
        TypeRegistry registry,
        GeneratorOptions options,
        bool isRecord = false,
        HashSet<string>? generatedTypes = null)
    {
        generatedTypes ??= new HashSet<string>();

        if (!generatedTypes.Add(classSuffix))
        {
            return;
        }

        var properties = def.Properties ?? new Dictionary<string, LexiconDefinition>();
        var requiredProps = new HashSet<string>(def.Required ?? Enumerable.Empty<string>());

        // Extract short class name and namespace for property type resolution
        var shortClassName = ExtractShortName(qualifiedTypeName);
        var typeNamespace = ExtractNamespace(qualifiedTypeName);

        // Generate nested type infos first (inline objects, unions)
        foreach (var prop in properties)
        {
            GenerateNestedTypeInfosForProperty(sb, qualifiedTypeName, classSuffix, prop.Key, prop.Value, currentNsid, registry, options, generatedTypes);
        }

        var globalType = ResolveToGlobalType(qualifiedTypeName);

        sb.WriteSummary($"CBOR type info for {shortClassName}.");
        sb.AppendLine($"public sealed class {classSuffix}CborTypeInfo : CarpaNet.Cbor.CborObjectTypeInfo<{globalType}>");
        sb.OpenBrace();

        // CreateInstance override using Activator to bypass required member checks
        sb.AppendLine($"public override {globalType} CreateInstance() => ({globalType})System.Activator.CreateInstance(typeof({globalType}))!;");
        sb.AppendLine();

        // Type discriminator for records
        if (isRecord)
        {
            sb.AppendLine($"public override string? TypeDiscriminator => \"{currentNsid}\";");
            sb.AppendLine();
        }

        // Properties array
        sb.AppendLine($"private static readonly CarpaNet.Cbor.CborPropertyInfo<{globalType}>[] _properties = new CarpaNet.Cbor.CborPropertyInfo<{globalType}>[]");
        sb.OpenBrace();

        var propList = properties.ToList();
        for (int i = 0; i < propList.Count; i++)
        {
            var prop = propList[i];
            var propName = NsidHelper.ToPascalCase(prop.Key);
            var jsonName = prop.Key;

            // Property name collision handling (matching ObjectGenerator/JsonContextGenerator)
            var cleanClassName = NsidHelper.StripEscapePrefix(shortClassName);
            if (propName.Equals(cleanClassName, StringComparison.OrdinalIgnoreCase))
            {
                propName = propName + "Value";
            }

            // Records auto-generate a "Type" property for the $type discriminator,
            // so rename any lexicon property that would also become "Type"
            if (isRecord && propName == "Type")
            {
                propName = "TypeValue";
            }

            propName = NsidHelper.EscapeIdentifier(propName);

            var propType = GetPropertyCSharpType(prop.Value, currentNsid, registry, shortClassName, prop.Key, typeNamespace);
            var converterExpr = GetConverterExpression(prop.Value, currentNsid, registry, shortClassName, prop.Key, typeNamespace, sb, options, generatedTypes, qualifiedTypeName, classSuffix);
            var isRequired = requiredProps.Contains(prop.Key) || prop.Value.IsRequired;
            var isNullable = IsNullableProperty(prop.Value, def, prop.Key);
            var isValueType = !IsReferenceType(propType);

            // Resolve property type for the CborPropertyInfo generic parameter
            var globalPropType = ResolveToGlobalType(propType);

            // Generate CborPropertyInfo with appropriate getter/setter for nullable handling
            if (isNullable && isValueType && !propType.EndsWith("?"))
            {
                // Nullable value type: use non-nullable for CborPropertyInfo, getter uses GetValueOrDefault
                sb.AppendLine($"new CarpaNet.Cbor.CborPropertyInfo<{globalType}, {globalPropType}>(");
                sb.AppendLine($"    \"{jsonName}\",");
                sb.AppendLine($"    obj => obj.{propName}.GetValueOrDefault(),");
                sb.AppendLine($"    (obj, val) => obj.{propName} = val,");
            }
            else if (isNullable && IsReferenceType(propType))
            {
                // Nullable reference type - use null-forgiving on getter to suppress CS8603
                sb.AppendLine($"new CarpaNet.Cbor.CborPropertyInfo<{globalType}, {globalPropType}>(");
                sb.AppendLine($"    \"{jsonName}\",");
                sb.AppendLine($"    obj => obj.{propName}!,");
                sb.AppendLine($"    (obj, val) => obj.{propName} = val,");
            }
            else if (!isNullable && IsReferenceType(propType))
            {
                // Non-nullable reference type
                sb.AppendLine($"new CarpaNet.Cbor.CborPropertyInfo<{globalType}, {globalPropType}>(");
                sb.AppendLine($"    \"{jsonName}\",");
                sb.AppendLine($"    obj => obj.{propName},");
                sb.AppendLine($"    (obj, val) => obj.{propName} = val ?? throw new System.InvalidOperationException(\"Property {propName} cannot be null\"),");
            }
            else
            {
                // Non-nullable value type
                sb.AppendLine($"new CarpaNet.Cbor.CborPropertyInfo<{globalType}, {globalPropType}>(");
                sb.AppendLine($"    \"{jsonName}\",");
                sb.AppendLine($"    obj => obj.{propName},");
                sb.AppendLine($"    (obj, val) => obj.{propName} = val,");
            }

            sb.AppendLine($"    {converterExpr},");

            // ShouldSerialize predicate
            if (isRequired)
            {
                sb.AppendLine("    _ => true)");
            }
            else if (isNullable && isValueType)
            {
                sb.AppendLine("    _ => true)");
            }
            else
            {
                sb.AppendLine("    val => val != null)");
            }

            if (i < propList.Count - 1)
            {
                sb.AppendLine(",");
            }
        }

        sb.CloseBrace(withSemicolon: true);
        sb.AppendLine();

        sb.AppendLine($"protected override System.Collections.Generic.IReadOnlyList<CarpaNet.Cbor.CborPropertyInfo<{globalType}>> Properties => _properties;");

        sb.CloseBrace();
        sb.AppendLine();
    }

    /// <summary>
    /// Marker added to the generated-types set when a union type info that reads in place is emitted,
    /// signalling that the shared <see cref="UnionHelperClassName"/> helper must be generated.
    /// </summary>
    public const string UnionHelperMarker = "<UnionCborHelper>";

    /// <summary>
    /// Name of the internal helper class emitted into the CBOR context namespace for data unions.
    /// </summary>
    public const string UnionHelperClassName = "UnionCborHelper";

    /// <summary>
    /// Generates a CborUnionTypeInfo subclass for a union type.
    /// </summary>
    /// <param name="dispatchInPlace">
    /// When true (unions inside data), members with a known <c>$type</c> are read directly from the caller's
    /// reader, so the reader stays positioned inside its enclosing map or array and following values can
    /// still be read. Subscription message unions pass false: their bodies carry no <c>$type</c> (the frame
    /// header does), so they keep the base class behavior.
    /// </param>
    /// <param name="preserveUnknown">
    /// When true (open unions in data, requires <paramref name="dispatchInPlace"/>), any non-null value
    /// that is not a member with a known <c>$type</c> (unknown or missing <c>$type</c>) is read into the
    /// union's <c>Unknown_*</c> class with its raw DAG-CBOR bytes, and written back unchanged.
    /// </param>
    public static void GenerateCborUnionTypeInfo(
        SourceBuilder sb,
        string qualifiedTypeName,
        string classSuffix,
        List<string> refs,
        string currentNsid,
        TypeRegistry registry,
        GeneratorOptions options,
        HashSet<string>? generatedTypes = null,
        bool dispatchInPlace = false,
        bool preserveUnknown = false)
    {
        generatedTypes ??= new HashSet<string>();

        if (!generatedTypes.Add(classSuffix))
        {
            return;
        }

        var globalType = ResolveToGlobalType(qualifiedTypeName);

        sb.WriteSummary($"CBOR type info for union {qualifiedTypeName}.");
        sb.AppendLine($"public sealed class {classSuffix}CborTypeInfo : CarpaNet.Cbor.CborUnionTypeInfo<{globalType}>");
        sb.OpenBrace();

        sb.AppendLine("private static readonly System.Collections.Generic.Dictionary<string, CarpaNet.Cbor.ICborTypeInfo> _derivedTypes = new()");
        sb.OpenBrace();

        foreach (var refString in refs)
        {
            // A member whose definition is missing is left out; an open union keeps it as an unknown member.
            if (!registry.IsResolvable(refString, currentNsid))
            {
                continue;
            }

            var typeName = registry.ResolveToCSharpType(refString, currentNsid);
            typeName = QualifyUnionNames(typeName, refString, currentNsid, registry);
            var discriminator = GetTypeDiscriminator(refString, currentNsid, registry);

            if (registry.RefGeneratesClass(refString, currentNsid))
            {
                var refSuffix = ToClassSuffix(typeName);
                sb.AppendLine($"{{ \"{discriminator}\", new {refSuffix}CborTypeInfo() }},");
            }
        }

        sb.CloseBrace(withSemicolon: true);
        sb.AppendLine();

        sb.AppendLine("protected override System.Collections.Generic.IReadOnlyDictionary<string, CarpaNet.Cbor.ICborTypeInfo> DerivedTypes => _derivedTypes;");

        if (dispatchInPlace && refs.Count > 0)
        {
            generatedTypes.Add(UnionHelperMarker);
            var unknownType = preserveUnknown
                ? ResolveToGlobalType(UnionGenerator.GetUnknownTypeName(qualifiedTypeName))
                : null;

            sb.AppendLine();
            sb.WriteSummary(unknownType != null
                ? "Reads a union member in place; members with an unknown $type are kept as raw DAG-CBOR."
                : "Reads a union member in place, so the reader stays positioned within its enclosing container.");
            sb.AppendLine($"public override {globalType}? Read(ref CarpaNet.Cbor.DagCborReader reader)");
            sb.OpenBrace();
            sb.AppendLine("var state = reader.PeekState();");
            sb.AppendLine("string? discriminator = null;");
            sb.AppendLine("if (state == System.Formats.Cbor.CborReaderState.StartMap)");
            sb.OpenBrace();
            sb.AppendLine($"discriminator = {UnionHelperClassName}.PeekTypeDiscriminator(reader.GetRemainingData());");
            sb.AppendLine("if (discriminator != null && _derivedTypes.TryGetValue(discriminator, out var typeInfo))");
            sb.OpenBrace();
            sb.AppendLine($"return ({globalType}?)typeInfo.ReadObject(ref reader);");
            sb.CloseBrace();
            sb.CloseBrace();
            sb.AppendLine();

            if (unknownType != null)
            {
                // Open union: anything other than null or a known member is kept verbatim
                sb.AppendLine("if (state != System.Formats.Cbor.CborReaderState.Null)");
                sb.OpenBrace();
                sb.AppendLine($"var rawCbor = {UnionHelperClassName}.ReadRawValue(ref reader);");
                sb.AppendLine($"return new {unknownType}(discriminator ?? string.Empty, {UnionHelperClassName}.ToJson(rawCbor), rawCbor);");
                sb.CloseBrace();
                sb.AppendLine();
            }

            sb.AppendLine("return base.Read(ref reader);");
            sb.CloseBrace();

            sb.AppendLine();
            sb.WriteSummary(unknownType != null
                ? "Writes a union member with its $type; unknown members are written back from their raw data."
                : "Writes a union member with its $type discriminator.");
            sb.AppendLine($"public override void Write(ref CarpaNet.Cbor.DagCborWriter writer, {globalType}? value)");
            sb.OpenBrace();

            if (unknownType != null)
            {
                sb.AppendLine($"if (value is {unknownType} unknown)");
                sb.OpenBrace();
                sb.AppendLine($"{UnionHelperClassName}.WriteRawValue(ref writer, unknown.Raw, unknown.RawCbor);");
                sb.AppendLine("return;");
                sb.CloseBrace();
                sb.AppendLine();
            }

            // Non-record members do not write $type themselves, but union members must carry it
            sb.AppendLine("if (value != null)");
            sb.OpenBrace();
            sb.AppendLine("var runtimeType = value.GetType();");
            sb.AppendLine("foreach (var kvp in _derivedTypes)");
            sb.OpenBrace();
            sb.AppendLine("if (kvp.Value.TargetType == runtimeType && kvp.Value.TypeDiscriminator == null)");
            sb.OpenBrace();
            sb.AppendLine($"{UnionHelperClassName}.WriteWithTypeDiscriminator(ref writer, kvp.Key, kvp.Value, value);");
            sb.AppendLine("return;");
            sb.CloseBrace();
            sb.CloseBrace();
            sb.CloseBrace();
            sb.AppendLine();
            sb.AppendLine("base.Write(ref writer, value);");
            sb.CloseBrace();
        }

        sb.CloseBrace();
        sb.AppendLine();
    }

    /// <summary>
    /// Generates the internal helper used by data union type infos to peek <c>$type</c>,
    /// add <c>$type</c> to members that do not write it, capture raw DAG-CBOR values,
    /// and copy them back out unchanged.
    /// </summary>
    public static void GenerateUnionHelper(SourceBuilder sb)
    {
        sb.WriteSummary("Helpers for reading union members in place and preserving unknown open-union members in DAG-CBOR.");
        sb.AppendLine($"internal static class {UnionHelperClassName}");
        sb.OpenBrace();

        // PeekTypeDiscriminator
        sb.WriteSummary("Returns the $type text value of the CBOR map at the start of the data, or null if it has none.");
        sb.AppendLine("public static string? PeekTypeDiscriminator(System.ReadOnlyMemory<byte> data)");
        sb.OpenBrace();
        sb.AppendLine("var reader = new CarpaNet.Cbor.DagCborReader(data);");
        sb.AppendLine("var count = reader.ReadStartMap();");
        sb.AppendLine("var remaining = count ?? int.MaxValue;");
        sb.AppendLine("while (remaining > 0 && reader.PeekState() != System.Formats.Cbor.CborReaderState.EndMap)");
        sb.OpenBrace();
        sb.AppendLine("if (reader.PeekState() != System.Formats.Cbor.CborReaderState.TextString)");
        sb.OpenBrace();
        sb.AppendLine("return null;");
        sb.CloseBrace();
        sb.AppendLine();
        sb.AppendLine("var key = reader.ReadTextString();");
        sb.AppendLine("if (key == \"$type\")");
        sb.OpenBrace();
        sb.AppendLine("return reader.PeekState() == System.Formats.Cbor.CborReaderState.TextString ? reader.ReadTextString() : null;");
        sb.CloseBrace();
        sb.AppendLine();
        sb.AppendLine("reader.SkipValue();");
        sb.AppendLine("remaining--;");
        sb.CloseBrace();
        sb.AppendLine();
        sb.AppendLine("return null;");
        sb.CloseBrace();
        sb.AppendLine();

        // ReadRawValue
        sb.WriteSummary("Reads the next complete value and returns its encoded bytes.");
        sb.AppendLine("public static byte[] ReadRawValue(ref CarpaNet.Cbor.DagCborReader reader)");
        sb.OpenBrace();
        sb.AppendLine("var data = reader.GetRemainingData();");
        sb.AppendLine("var start = reader.BytesRead;");
        sb.AppendLine("reader.SkipValue();");
        sb.AppendLine("return data.Slice(0, reader.BytesRead - start).ToArray();");
        sb.CloseBrace();
        sb.AppendLine();

        // ToJson
        sb.WriteSummary("Converts an encoded CBOR value to JSON (CID links and byte strings become strings).");
        sb.AppendLine("public static System.Text.Json.JsonElement ToJson(byte[] rawCbor)");
        sb.OpenBrace();
        sb.AppendLine("var reader = new CarpaNet.Cbor.DagCborReader(rawCbor);");
        sb.AppendLine("return new CarpaNet.Cbor.Converters.JsonElementCborConverter().ReadTyped(ref reader);");
        sb.CloseBrace();
        sb.AppendLine();

        // WriteRawValue
        sb.WriteSummary("Writes an unknown member: its original CBOR bytes when available, otherwise its JSON value converted to CBOR.");
        sb.AppendLine("public static void WriteRawValue(ref CarpaNet.Cbor.DagCborWriter writer, System.Text.Json.JsonElement raw, byte[]? rawCbor)");
        sb.OpenBrace();
        sb.AppendLine("if (rawCbor != null)");
        sb.OpenBrace();
        sb.AppendLine("var reader = new CarpaNet.Cbor.DagCborReader(rawCbor);");
        sb.AppendLine("CopyValue(ref reader, ref writer);");
        sb.AppendLine("return;");
        sb.CloseBrace();
        sb.AppendLine();
        sb.AppendLine("new CarpaNet.Cbor.Converters.JsonElementCborConverter().WriteTyped(ref writer, raw);");
        sb.CloseBrace();
        sb.AppendLine();

        // WriteWithTypeDiscriminator
        sb.WriteSummary("Writes an object through its type info, adding a $type entry to the map in DAG-CBOR canonical key order.");
        sb.AppendLine("public static void WriteWithTypeDiscriminator(ref CarpaNet.Cbor.DagCborWriter writer, string discriminator, CarpaNet.Cbor.ICborTypeInfo typeInfo, object value)");
        sb.OpenBrace();
        sb.AppendLine("var temp = new CarpaNet.Cbor.DagCborWriter();");
        sb.AppendLine("typeInfo.WriteObject(ref temp, value);");
        sb.AppendLine("var reader = new CarpaNet.Cbor.DagCborReader(temp.Encode());");
        sb.AppendLine("if (reader.PeekState() != System.Formats.Cbor.CborReaderState.StartMap)");
        sb.OpenBrace();
        sb.AppendLine("CopyValue(ref reader, ref writer);");
        sb.AppendLine("return;");
        sb.CloseBrace();
        sb.AppendLine();
        sb.AppendLine("var count = reader.ReadStartMap();");
        sb.AppendLine("writer.WriteStartMap(count + 1);");
        sb.AppendLine();
        sb.AppendLine("// The member's keys are already in DAG-CBOR canonical order; insert $type at its canonical position");
        sb.AppendLine("var typeWritten = false;");
        sb.AppendLine("while (reader.PeekState() != System.Formats.Cbor.CborReaderState.EndMap)");
        sb.OpenBrace();
        sb.AppendLine("var key = reader.ReadTextString();");
        sb.AppendLine("if (!typeWritten && CarpaNet.Cbor.DagCborKeyComparer.Instance.Compare(\"$type\", key) < 0)");
        sb.OpenBrace();
        sb.AppendLine("writer.WriteTextString(\"$type\");");
        sb.AppendLine("writer.WriteTextString(discriminator);");
        sb.AppendLine("typeWritten = true;");
        sb.CloseBrace();
        sb.AppendLine();
        sb.AppendLine("writer.WriteTextString(key);");
        sb.AppendLine("CopyValue(ref reader, ref writer); // value");
        sb.CloseBrace();
        sb.AppendLine();
        sb.AppendLine("if (!typeWritten)");
        sb.OpenBrace();
        sb.AppendLine("writer.WriteTextString(\"$type\");");
        sb.AppendLine("writer.WriteTextString(discriminator);");
        sb.CloseBrace();
        sb.AppendLine();
        sb.AppendLine("reader.ReadEndMap();");
        sb.AppendLine("writer.WriteEndMap();");
        sb.CloseBrace();
        sb.AppendLine();

        // CopyValue
        sb.WriteSummary("Copies one CBOR value (recursively) from the reader to the writer.");
        sb.AppendLine("private static void CopyValue(ref CarpaNet.Cbor.DagCborReader reader, ref CarpaNet.Cbor.DagCborWriter writer)");
        sb.OpenBrace();
        sb.AppendLine("switch (reader.PeekState())");
        sb.OpenBrace();
        sb.AppendLine("case System.Formats.Cbor.CborReaderState.UnsignedInteger:");
        sb.AppendLine("    writer.WriteUInt64(reader.ReadUInt64());");
        sb.AppendLine("    break;");
        sb.AppendLine("case System.Formats.Cbor.CborReaderState.NegativeInteger:");
        sb.AppendLine("    writer.WriteInt64(reader.ReadInt64());");
        sb.AppendLine("    break;");
        sb.AppendLine("case System.Formats.Cbor.CborReaderState.HalfPrecisionFloat:");
        sb.AppendLine("case System.Formats.Cbor.CborReaderState.SinglePrecisionFloat:");
        sb.AppendLine("case System.Formats.Cbor.CborReaderState.DoublePrecisionFloat:");
        sb.AppendLine("    writer.WriteDouble(reader.ReadDouble());");
        sb.AppendLine("    break;");
        sb.AppendLine("case System.Formats.Cbor.CborReaderState.TextString:");
        sb.AppendLine("    writer.WriteTextString(reader.ReadTextString());");
        sb.AppendLine("    break;");
        sb.AppendLine("case System.Formats.Cbor.CborReaderState.ByteString:");
        sb.AppendLine("    writer.WriteByteString(reader.ReadByteString());");
        sb.AppendLine("    break;");
        sb.AppendLine("case System.Formats.Cbor.CborReaderState.Boolean:");
        sb.AppendLine("    writer.WriteBoolean(reader.ReadBoolean());");
        sb.AppendLine("    break;");
        sb.AppendLine("case System.Formats.Cbor.CborReaderState.Null:");
        sb.AppendLine("    reader.ReadNull();");
        sb.AppendLine("    writer.WriteNull();");
        sb.AppendLine("    break;");
        sb.AppendLine("case System.Formats.Cbor.CborReaderState.Tag:");
        sb.AppendLine("    writer.WriteTag(reader.ReadTag());");
        sb.AppendLine("    CopyValue(ref reader, ref writer);");
        sb.AppendLine("    break;");
        sb.AppendLine("case System.Formats.Cbor.CborReaderState.StartArray:");
        sb.OpenBrace();
        sb.AppendLine("writer.WriteStartArray(reader.ReadStartArray());");
        sb.AppendLine("while (reader.PeekState() != System.Formats.Cbor.CborReaderState.EndArray)");
        sb.OpenBrace();
        sb.AppendLine("CopyValue(ref reader, ref writer);");
        sb.CloseBrace();
        sb.AppendLine("reader.ReadEndArray();");
        sb.AppendLine("writer.WriteEndArray();");
        sb.AppendLine("break;");
        sb.CloseBrace();
        sb.AppendLine("case System.Formats.Cbor.CborReaderState.StartMap:");
        sb.OpenBrace();
        sb.AppendLine("writer.WriteStartMap(reader.ReadStartMap());");
        sb.AppendLine("while (reader.PeekState() != System.Formats.Cbor.CborReaderState.EndMap)");
        sb.OpenBrace();
        sb.AppendLine("CopyValue(ref reader, ref writer); // key");
        sb.AppendLine("CopyValue(ref reader, ref writer); // value");
        sb.CloseBrace();
        sb.AppendLine("reader.ReadEndMap();");
        sb.AppendLine("writer.WriteEndMap();");
        sb.AppendLine("break;");
        sb.CloseBrace();
        sb.AppendLine("default:");
        sb.AppendLine("    throw new System.InvalidOperationException($\"Unsupported CBOR state when copying an unknown union member: {reader.PeekState()}\");");
        sb.CloseBrace();
        sb.CloseBrace();

        sb.CloseBrace();
        sb.AppendLine();
    }

    /// <summary>
    /// Generates nested type infos for a property (handles inline objects, unions, array items).
    /// </summary>
    private static void GenerateNestedTypeInfosForProperty(
        SourceBuilder sb,
        string parentQualifiedTypeName,
        string parentClassSuffix,
        string propertyName,
        LexiconDefinition prop,
        string currentNsid,
        TypeRegistry registry,
        GeneratorOptions options,
        HashSet<string> generatedTypes)
    {
        // Handle inline objects
        if (prop.Type == "object" && prop.Properties != null && prop.Properties.Count > 0)
        {
            var pascalProp = NsidHelper.ToPascalCase(propertyName);
            var nestedQualified = $"{parentQualifiedTypeName}{pascalProp}";
            var nestedSuffix = $"{parentClassSuffix}_{pascalProp}";
            GenerateCborTypeInfo(sb, nestedQualified, nestedSuffix, prop, currentNsid, registry, options, generatedTypes: generatedTypes);
        }

        // Handle unions (skip open unions with no refs - those use JsonElement directly)
        if (prop.Type == "union" && prop.Refs != null
            && !(prop.Closed != true && prop.Refs.Count == 0))
        {
            var pascalProp = NsidHelper.ToPascalCase(propertyName);
            var shortParent = ExtractShortName(parentQualifiedTypeName);
            var ns = ExtractNamespace(parentQualifiedTypeName);
            var cleanParent = NsidHelper.StripEscapePrefix(shortParent);
            var cleanProp = NsidHelper.StripEscapePrefix(pascalProp);
            var interfaceShort = $"I{cleanParent}{cleanProp}";
            var interfaceQualified = string.IsNullOrEmpty(ns) ? interfaceShort : $"{ns}.{interfaceShort}";
            var interfaceSuffix = ToClassSuffix(interfaceQualified);
            GenerateCborUnionTypeInfo(sb, interfaceQualified, interfaceSuffix, prop.Refs, currentNsid, registry, options, generatedTypes, dispatchInPlace: true, preserveUnknown: prop.Closed != true);
        }

        // Handle arrays
        if (prop.Type == "array" && prop.Items != null)
        {
            // Arrays of inline objects
            if (prop.Items.Type == "object" && prop.Items.Properties != null && prop.Items.Properties.Count > 0)
            {
                var pascalProp = NsidHelper.ToPascalCase(propertyName);
                var nestedQualified = $"{parentQualifiedTypeName}{pascalProp}Item";
                var nestedSuffix = $"{parentClassSuffix}_{pascalProp}Item";
                GenerateCborTypeInfo(sb, nestedQualified, nestedSuffix, prop.Items, currentNsid, registry, options, generatedTypes: generatedTypes);
            }

            // Arrays of unions (no Item suffix, matching ObjectGenerator)
            if (prop.Items.Type == "union" && prop.Items.Refs != null
                && !(prop.Items.Closed != true && prop.Items.Refs.Count == 0))
            {
                var pascalProp = NsidHelper.ToPascalCase(propertyName);
                var shortParent = ExtractShortName(parentQualifiedTypeName);
                var ns = ExtractNamespace(parentQualifiedTypeName);
                var cleanParent = NsidHelper.StripEscapePrefix(shortParent);
                var cleanProp = NsidHelper.StripEscapePrefix(pascalProp);
                var interfaceShort = $"I{cleanParent}{cleanProp}";
                var interfaceQualified = string.IsNullOrEmpty(ns) ? interfaceShort : $"{ns}.{interfaceShort}";
                var interfaceSuffix = ToClassSuffix(interfaceQualified);
                GenerateCborUnionTypeInfo(sb, interfaceQualified, interfaceSuffix, prop.Items.Refs, currentNsid, registry, options, generatedTypes, dispatchInPlace: true, preserveUnknown: prop.Items.Closed != true);
            }
        }
    }

    /// <summary>
    /// Gets the C# type for a property, fully qualified with namespace.
    /// Mirrors JsonContextGenerator.GetPropertyCSharpType.
    /// </summary>
    private static string GetPropertyCSharpType(
        LexiconDefinition prop,
        string currentNsid,
        TypeRegistry registry,
        string? parentClassName = null,
        string? propertyName = null,
        string? parentNamespace = null)
    {
        if (!string.IsNullOrEmpty(prop.Ref))
        {
            var resolved = registry.ResolveToCSharpType(prop.Ref, currentNsid);
            return QualifyUnionNames(resolved, prop.Ref!, currentNsid, registry);
        }

        var nsPrefix = !string.IsNullOrEmpty(parentNamespace) ? $"{parentNamespace}." : "";

        return prop.Type switch
        {
            "string" => prop.Format switch
            {
                "datetime" => "System.DateTimeOffset",
                "at-uri" => "CarpaNet.ATUri",
                "did" => "CarpaNet.ATDid",
                "handle" => "CarpaNet.ATHandle",
                "at-identifier" => "CarpaNet.ATIdentifier",
                "uri" => "string",
                "cid" => "string",
                "language" => "string",
                "nsid" => "string",
                "record-key" => "string",
                "tid" => "string",
                _ => "string"
            },
            "integer" => "long",
            "boolean" => "bool",
            "bytes" => "byte[]",
            "cid-link" => "CarpaNet.ATCid",
            "blob" => "CarpaNet.ATBlob",
            "unknown" => "System.Text.Json.JsonElement",
            "token" => "string",
            "array" => GetArrayType(prop, currentNsid, registry, parentClassName, propertyName, parentNamespace),
            "object" when prop.Properties != null && prop.Properties.Count > 0 && parentClassName != null && propertyName != null =>
                $"{nsPrefix}{parentClassName}{NsidHelper.ToPascalCase(propertyName)}",
            "union" when prop.Closed != true && (prop.Refs == null || prop.Refs.Count == 0)
                => "System.Text.Json.JsonElement",
            "union" when prop.Refs != null && parentClassName != null && propertyName != null =>
                $"{nsPrefix}I{NsidHelper.StripEscapePrefix(parentClassName)}{NsidHelper.StripEscapePrefix(NsidHelper.ToPascalCase(propertyName))}",
            _ => "object"
        };
    }

    /// <summary>
    /// Gets the C# type for an array property.
    /// </summary>
    private static string GetArrayType(
        LexiconDefinition prop,
        string currentNsid,
        TypeRegistry registry,
        string? parentClassName = null,
        string? propertyName = null,
        string? parentNamespace = null)
    {
        if (prop.Items == null)
        {
            return "System.Collections.Generic.List<object>";
        }

        var items = prop.Items;
        var nsPrefix = !string.IsNullOrEmpty(parentNamespace) ? $"{parentNamespace}." : "";

        // Inline object array items
        if (items.Type == "object" && items.Properties != null && items.Properties.Count > 0 && parentClassName != null && propertyName != null)
        {
            var nestedClassName = $"{nsPrefix}{parentClassName}{NsidHelper.ToPascalCase(propertyName)}Item";
            return $"System.Collections.Generic.List<{nestedClassName}>";
        }

        // Inline union array items (no Item suffix, matching ObjectGenerator)
        if (items.Type == "union" && items.Refs != null && parentClassName != null && propertyName != null
            && !(items.Closed != true && items.Refs.Count == 0))
        {
            var cleanParent = NsidHelper.StripEscapePrefix(parentClassName);
            var cleanProp = NsidHelper.StripEscapePrefix(NsidHelper.ToPascalCase(propertyName));
            var interfaceName = $"{nsPrefix}I{cleanParent}{cleanProp}";
            return $"System.Collections.Generic.List<{interfaceName}>";
        }

        var elementType = GetPropertyCSharpType(items, currentNsid, registry, parentClassName, propertyName != null ? $"{propertyName}Item" : null, parentNamespace);
        return $"System.Collections.Generic.List<{elementType}>";
    }

    /// <summary>
    /// Gets the converter expression for a property.
    /// Returns a string like "new CarpaNet.Cbor.Converters.StringCborConverter()"
    /// or "new AppBsky_Actor_DefsProfileAssociatedCborTypeInfo()".
    /// </summary>
    private static string GetConverterExpression(
        LexiconDefinition prop,
        string currentNsid,
        TypeRegistry registry,
        string parentClassName,
        string propertyName,
        string parentNamespace,
        SourceBuilder sb,
        GeneratorOptions options,
        HashSet<string> generatedTypes,
        string parentQualifiedTypeName,
        string parentClassSuffix)
    {
        if (!string.IsNullOrEmpty(prop.Ref))
        {
            return GetConverterExpressionForRef(prop.Ref!, currentNsid, registry);
        }

        var nsPrefix = !string.IsNullOrEmpty(parentNamespace) ? $"{parentNamespace}." : "";

        return prop.Type switch
        {
            "string" => GetStringFormatConverter(prop.Format),
            "integer" => "new CarpaNet.Cbor.Converters.Int64CborConverter()",
            "boolean" => "new CarpaNet.Cbor.Converters.BooleanCborConverter()",
            "bytes" => "new CarpaNet.Cbor.Converters.ByteArrayCborConverter()",
            "cid-link" => "new CarpaNet.Cbor.Converters.ATCidCborConverter()",
            "blob" => "new CarpaNet.Cbor.Converters.ATBlobCborConverter()",
            "token" => "new CarpaNet.Cbor.Converters.StringCborConverter()",
            "unknown" => "new CarpaNet.Cbor.Converters.JsonElementCborConverter()",
            "array" => GetArrayConverterExpression(prop, currentNsid, registry, parentClassName, propertyName, parentNamespace),
            "object" when prop.Properties != null && prop.Properties.Count > 0 =>
                CborTypeInfoRef($"{nsPrefix}{parentClassName}{NsidHelper.ToPascalCase(propertyName)}"),
            "union" when prop.Closed != true && (prop.Refs == null || prop.Refs.Count == 0) =>
                "new CarpaNet.Cbor.Converters.JsonElementCborConverter()",
            "union" when prop.Refs != null =>
                CborTypeInfoRef($"{nsPrefix}I{NsidHelper.StripEscapePrefix(parentClassName)}{NsidHelper.StripEscapePrefix(NsidHelper.ToPascalCase(propertyName))}"),
            _ => "new CarpaNet.Cbor.Converters.StringCborConverter()" // Fallback
        };
    }

    /// <summary>
    /// Gets the converter expression for a ref property.
    /// </summary>
    private static string GetConverterExpressionForRef(string refString, string currentNsid, TypeRegistry registry)
    {
        var refKind = registry.GetRefKind(refString, currentNsid);
        var fullRef = ResolveLocalRef(refString, currentNsid);
        var typeInfo = registry.Lookup(fullRef);

        switch (refKind)
        {
            case LexiconTypeKind.String:
                return GetStringFormatConverter(typeInfo?.Definition?.Format);

            case LexiconTypeKind.Boolean:
                return "new CarpaNet.Cbor.Converters.BooleanCborConverter()";

            case LexiconTypeKind.Integer:
                return "new CarpaNet.Cbor.Converters.Int64CborConverter()";

            case LexiconTypeKind.Bytes:
                return "new CarpaNet.Cbor.Converters.ByteArrayCborConverter()";

            case LexiconTypeKind.CidLink:
                return "new CarpaNet.Cbor.Converters.ATCidCborConverter()";

            case LexiconTypeKind.Blob:
                return "new CarpaNet.Cbor.Converters.ATBlobCborConverter()";

            case LexiconTypeKind.Token:
                return "new CarpaNet.Cbor.Converters.StringCborConverter()";

            case LexiconTypeKind.Any:
                return "new CarpaNet.Cbor.Converters.JsonElementCborConverter()";

            case LexiconTypeKind.Object:
            case LexiconTypeKind.Record:
            {
                var typeName = registry.ResolveToCSharpType(refString, currentNsid);
                typeName = QualifyUnionNames(typeName, refString, currentNsid, registry);
                return CborTypeInfoRef(typeName);
            }

            case LexiconTypeKind.Union:
            {
                var typeName = registry.ResolveToCSharpType(refString, currentNsid);
                typeName = QualifyUnionNames(typeName, refString, currentNsid, registry);
                return CborTypeInfoRef(typeName);
            }

            case LexiconTypeKind.Array:
                return GetConverterExpressionForArrayRef(refString, currentNsid, registry);

            default:
            {
                var typeName = registry.ResolveToCSharpType(refString, currentNsid);
                typeName = QualifyUnionNames(typeName, refString, currentNsid, registry);
                return CborTypeInfoRef(typeName);
            }
        }
    }

    /// <summary>
    /// Gets the converter expression for a ref that points to an array type.
    /// </summary>
    private static string GetConverterExpressionForArrayRef(string refString, string currentNsid, TypeRegistry registry)
    {
        var fullRef = ResolveLocalRef(refString, currentNsid);
        var arrayTypeInfo = registry.Lookup(fullRef);

        if (arrayTypeInfo?.Definition?.Items != null)
        {
            var items = arrayTypeInfo.Definition.Items;
            var arrayNsid = arrayTypeInfo.Nsid;

            // Array of union
            if (items.Type == "union" && items.Refs != null)
            {
                var elementType = $"{arrayTypeInfo.CSharpNamespace}.I{arrayTypeInfo.CSharpTypeName}";
                return ListConverterRef(elementType, CborTypeInfoRef(elementType));
            }

            // Array of ref type
            if (!string.IsNullOrEmpty(items.Ref))
            {
                var itemTypeName = registry.ResolveToCSharpType(items.Ref, arrayNsid);
                itemTypeName = QualifyUnionNames(itemTypeName, items.Ref!, arrayNsid, registry);
                var itemRefKind = registry.GetRefKind(items.Ref!, arrayNsid);

                if (itemRefKind == LexiconTypeKind.Union || registry.RefGeneratesClass(items.Ref!, arrayNsid))
                {
                    return ListConverterRef(itemTypeName, CborTypeInfoRef(itemTypeName));
                }

                if (itemRefKind == LexiconTypeKind.Array)
                {
                    var innerConverter = GetConverterExpressionForArrayRef(items.Ref!, arrayNsid, registry);
                    return ListConverterRef(itemTypeName, innerConverter);
                }

                var itemConverter = GetPrimitiveConverterForRef(items.Ref!, arrayNsid, registry);
                return ListConverterRef(itemTypeName, itemConverter);
            }

            // Primitive array items
            var elemType = GetPropertyCSharpType(items, arrayNsid, registry);
            var elemConverter = GetPrimitiveConverter(items);
            return ListConverterRef(elemType, elemConverter);
        }

        return "new CarpaNet.Cbor.Converters.StringCborConverter()"; // Fallback
    }

    /// <summary>
    /// Gets the converter expression for an inline array property.
    /// </summary>
    private static string GetArrayConverterExpression(
        LexiconDefinition prop,
        string currentNsid,
        TypeRegistry registry,
        string parentClassName,
        string propertyName,
        string parentNamespace)
    {
        if (prop.Items == null)
        {
            return "/* TODO: array without items type */";
        }

        var items = prop.Items;
        var nsPrefix = !string.IsNullOrEmpty(parentNamespace) ? $"{parentNamespace}." : "";

        // Union arrays (no Item suffix)
        if (items.Type == "union" && items.Refs != null
            && !(items.Closed != true && items.Refs.Count == 0))
        {
            var cleanParent = NsidHelper.StripEscapePrefix(parentClassName);
            var cleanProp = NsidHelper.StripEscapePrefix(NsidHelper.ToPascalCase(propertyName));
            var interfaceName = $"{nsPrefix}I{cleanParent}{cleanProp}";
            return ListConverterRef(interfaceName, CborTypeInfoRef(interfaceName));
        }

        // Open union with no refs -> List<JsonElement>
        if (items.Type == "union" && items.Closed != true && (items.Refs == null || items.Refs.Count == 0))
        {
            return ListConverterRef("System.Text.Json.JsonElement", "new CarpaNet.Cbor.Converters.JsonElementCborConverter()");
        }

        // Nested object arrays
        if (items.Type == "object" && items.Properties != null && items.Properties.Count > 0)
        {
            var nestedClassName = $"{nsPrefix}{parentClassName}{NsidHelper.ToPascalCase(propertyName)}Item";
            return ListConverterRef(nestedClassName, CborTypeInfoRef(nestedClassName));
        }

        // Ref arrays
        if (!string.IsNullOrEmpty(items.Ref))
        {
            var refType = registry.ResolveToCSharpType(items.Ref, currentNsid);
            refType = QualifyUnionNames(refType, items.Ref!, currentNsid, registry);
            var refKind = registry.GetRefKind(items.Ref!, currentNsid);

            if (refKind == LexiconTypeKind.Union || registry.RefGeneratesClass(items.Ref!, currentNsid))
            {
                return ListConverterRef(refType, CborTypeInfoRef(refType));
            }

            if (refKind == LexiconTypeKind.Array)
            {
                var innerConverter = GetConverterExpressionForArrayRef(items.Ref!, currentNsid, registry);
                return ListConverterRef(refType, innerConverter);
            }

            var converter = GetPrimitiveConverterForRef(items.Ref!, currentNsid, registry);
            return ListConverterRef(refType, converter);
        }

        // Primitive element type
        var elementType = GetPropertyCSharpType(items, currentNsid, registry, parentClassName, $"{propertyName}Item", parentNamespace);
        var elementConverter = GetPrimitiveConverter(items);
        return ListConverterRef(elementType, elementConverter);
    }

    // ---- Helper methods ----

    /// <summary>
    /// Creates a CborTypeInfo instantiation expression from a qualified type name.
    /// Example: "AppBsky.Actor.DefsProfileAssociated" -> "new AppBsky_Actor_DefsProfileAssociatedCborTypeInfo()"
    /// </summary>
    private static string CborTypeInfoRef(string qualifiedTypeName)
    {
        return $"new {ToClassSuffix(qualifiedTypeName)}CborTypeInfo()";
    }

    /// <summary>
    /// Creates a CborListTypeInfo expression.
    /// </summary>
    private static string ListConverterRef(string elementQualifiedType, string elementConverterExpr)
    {
        var globalElem = ResolveToGlobalType(elementQualifiedType);
        return $"new CarpaNet.Cbor.CborListTypeInfo<{globalElem}>({elementConverterExpr})";
    }

    private static string GetStringFormatConverter(string? format)
    {
        return format switch
        {
            "datetime" => "new CarpaNet.Cbor.Converters.DateTimeOffsetCborConverter()",
            "at-uri" => "new CarpaNet.Cbor.Converters.ATUriCborConverter()",
            "did" => "new CarpaNet.Cbor.Converters.ATDidCborConverter()",
            "handle" => "new CarpaNet.Cbor.Converters.ATHandleCborConverter()",
            "at-identifier" => "new CarpaNet.Cbor.Converters.ATIdentifierCborConverter()",
            "uri" => "new CarpaNet.Cbor.Converters.StringCborConverter()",
            "cid" => "new CarpaNet.Cbor.Converters.StringCborConverter()",
            _ => "new CarpaNet.Cbor.Converters.StringCborConverter()"
        };
    }

    private static string GetPrimitiveConverter(LexiconDefinition prop)
    {
        return prop.Type switch
        {
            "string" => GetStringFormatConverter(prop.Format),
            "integer" => "new CarpaNet.Cbor.Converters.Int64CborConverter()",
            "boolean" => "new CarpaNet.Cbor.Converters.BooleanCborConverter()",
            "bytes" => "new CarpaNet.Cbor.Converters.ByteArrayCborConverter()",
            "cid-link" => "new CarpaNet.Cbor.Converters.ATCidCborConverter()",
            "blob" => "new CarpaNet.Cbor.Converters.ATBlobCborConverter()",
            "unknown" => "new CarpaNet.Cbor.Converters.JsonElementCborConverter()",
            _ => "new CarpaNet.Cbor.Converters.StringCborConverter()"
        };
    }

    private static string GetPrimitiveConverterForRef(string refString, string currentNsid, TypeRegistry registry)
    {
        var refKind = registry.GetRefKind(refString, currentNsid);
        var fullRef = ResolveLocalRef(refString, currentNsid);
        var typeInfo = registry.Lookup(fullRef);

        return refKind switch
        {
            LexiconTypeKind.String => GetStringFormatConverter(typeInfo?.Definition?.Format),
            LexiconTypeKind.Boolean => "new CarpaNet.Cbor.Converters.BooleanCborConverter()",
            LexiconTypeKind.Integer => "new CarpaNet.Cbor.Converters.Int64CborConverter()",
            LexiconTypeKind.Bytes => "new CarpaNet.Cbor.Converters.ByteArrayCborConverter()",
            LexiconTypeKind.CidLink => "new CarpaNet.Cbor.Converters.ATCidCborConverter()",
            LexiconTypeKind.Blob => "new CarpaNet.Cbor.Converters.ATBlobCborConverter()",
            LexiconTypeKind.Token => "new CarpaNet.Cbor.Converters.StringCborConverter()",
            // "unknown" definitions and dangling references are raw JSON.
            LexiconTypeKind.Any => "new CarpaNet.Cbor.Converters.JsonElementCborConverter()",
            _ => "new CarpaNet.Cbor.Converters.StringCborConverter()"
        };
    }

    private static string GetTypeDiscriminator(string refString, string currentNsid, TypeRegistry registry)
    {
        var fullRef = ResolveLocalRef(refString, currentNsid);
        var typeInfo = registry.Lookup(fullRef);
        return typeInfo?.FullRef ?? fullRef;
    }

    /// <summary>
    /// Ensures union interface names are fully qualified.
    /// Same pattern as JsonContextGenerator.QualifyUnionNames.
    /// </summary>
    private static string QualifyUnionNames(string resolved, string refString, string currentNsid, TypeRegistry registry)
    {
        // Handle List<T> - qualify the inner type
        if (resolved.StartsWith("System.Collections.Generic.List<") && resolved.EndsWith(">"))
        {
            var inner = resolved.Substring("System.Collections.Generic.List<".Length, resolved.Length - "System.Collections.Generic.List<".Length - 1);
            var qualifiedInner = QualifyUnionNames(inner, refString, currentNsid, registry);
            return $"System.Collections.Generic.List<{qualifiedInner}>";
        }

        // Check if this is a bare interface name that needs qualification
        if (resolved.StartsWith("I") && resolved.Length > 1 && char.IsUpper(resolved[1]) && !resolved.Contains("."))
        {
            var fullRef = refString.StartsWith("#") ? $"{currentNsid}{refString}" : refString;
            var typeInfo = registry.Lookup(fullRef);
            if (typeInfo != null)
            {
                var fullName = typeInfo.FullCSharpTypeName;
                var lastDot = fullName.LastIndexOf('.');
                if (lastDot >= 0)
                {
                    var ns = fullName.Substring(0, lastDot);
                    return $"{ns}.{resolved}";
                }
            }
        }

        return resolved;
    }

    private static string ResolveLocalRef(string refString, string currentNsid)
    {
        if (refString.StartsWith("#"))
        {
            return $"{currentNsid}{refString}";
        }
        return refString;
    }

    private static bool IsNullableProperty(LexiconDefinition prop, LexiconDefinition parent, string propName)
    {
        var nullable = parent.Nullable ?? new List<string>();
        var required = parent.Required ?? new List<string>();
        return nullable.Contains(propName) || (!required.Contains(propName) && !prop.IsRequired);
    }

    private static bool IsReferenceType(string typeName)
    {
        return typeName switch
        {
            "string" => true,
            "byte[]" => true,
            "object" => true,
            "long" or "int" or "bool" or "double" or "float" => false,
            "System.DateTimeOffset" => false,
            "System.Text.Json.JsonElement" => false,
            "CarpaNet.ATBlob" => true,
            "CarpaNet.ATCid" => false,
            "CarpaNet.ATDid" => false,
            "CarpaNet.ATHandle" => false,
            "CarpaNet.ATIdentifier" => false,
            "CarpaNet.ATUri" => false,
            _ when typeName.StartsWith("System.Collections.Generic.List<") => true,
            _ when typeName.StartsWith("CarpaNet.AT") => false, // Remaining AT structs
            _ when typeName.StartsWith("I") && typeName.Length > 1 && char.IsUpper(typeName[1]) => true,
            _ when typeName.Contains(".") => true,
            _ => false
        };
    }

    /// <summary>
    /// Converts a C# type name to a global:: qualified form.
    /// Primitives remain as-is; namespace-qualified types get global:: prefix.
    /// Same pattern as JsonContextGenerator.ResolveToGlobalType.
    /// </summary>
    private static string ResolveToGlobalType(string typeName)
    {
        // Handle List<T>
        if (typeName.StartsWith("System.Collections.Generic.List<") && typeName.EndsWith(">"))
        {
            var inner = typeName.Substring("System.Collections.Generic.List<".Length, typeName.Length - "System.Collections.Generic.List<".Length - 1);
            return $"global::System.Collections.Generic.List<{ResolveToGlobalType(inner)}>";
        }

        return typeName switch
        {
            "string" => "string",
            "bool" => "bool",
            "long" => "long",
            "int" => "int",
            "byte[]" => "byte[]",
            "object" => "object",
            _ when typeName.Contains(".") => $"global::{typeName}",
            _ => typeName
        };
    }

    /// <summary>
    /// Extracts the short class name from a qualified type name.
    /// "AppBsky.Actor.DefsProfileViewBasic" -> "DefsProfileViewBasic"
    /// </summary>
    private static string ExtractShortName(string qualifiedTypeName)
    {
        return qualifiedTypeName.Contains(".")
            ? qualifiedTypeName.Substring(qualifiedTypeName.LastIndexOf('.') + 1)
            : qualifiedTypeName;
    }

    /// <summary>
    /// Extracts the namespace from a qualified type name.
    /// "AppBsky.Actor.DefsProfileViewBasic" -> "AppBsky.Actor"
    /// </summary>
    private static string ExtractNamespace(string qualifiedTypeName)
    {
        return qualifiedTypeName.Contains(".")
            ? qualifiedTypeName.Substring(0, qualifiedTypeName.LastIndexOf('.'))
            : "";
    }
}
