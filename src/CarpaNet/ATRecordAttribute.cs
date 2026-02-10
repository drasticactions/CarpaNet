using System;

namespace CarpaNet;

/// <summary>
/// Marks a class as an ATProtocol record type.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ATRecordAttribute : Attribute
{
    /// <summary>
    /// The NSID of the record type.
    /// </summary>
    public string Nsid { get; }

    /// <summary>
    /// Creates a new ATRecordAttribute.
    /// </summary>
    /// <param name="nsid">The NSID of the record type.</param>
    public ATRecordAttribute(string nsid)
    {
        Nsid = nsid ?? throw new ArgumentNullException(nameof(nsid));
    }
}
