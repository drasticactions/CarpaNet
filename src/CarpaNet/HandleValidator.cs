// <copyright file="HandleValidator.cs" company="Drastic Actions">
// Copyright (c) Drastic Actions. All rights reserved.
// </copyright>

using System.Text.RegularExpressions;

namespace CarpaNet;

/// <summary>
/// Handle Validator.
/// </summary>
internal static class HandleValidator
{
    /// <summary>
    /// Ensure Valid Handle.
    /// </summary>
    /// <param name="handle">String handle.</param>
    /// <returns>Returns a bool indicating if the handle is valid.</returns>
    internal static bool EnsureValidHandle(string handle)
    {
        if (!Regex.IsMatch(handle, "^[a-zA-Z0-9.-]*$"))
        {
            return false;
        }

        if (handle.Length > 253)
        {
            return false;
        }

        string[] labels = handle.Split('.');
        if (labels.Length < 2)
        {
            return false;
        }

        for (int i = 0; i < labels.Length; i++)
        {
            string l = labels[i];

            if (l.Length < 1)
            {
                return false;
            }

            if (l.Length > 63)
            {
                return false;
            }

            if (l.EndsWith("-") || l.StartsWith("-"))
            {
                return false;
            }

            if (i + 1 == labels.Length && !Regex.IsMatch(l, "^[a-zA-Z]"))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Ensure Valid Handle Regex.
    /// </summary>
    /// <param name="handle">String handle.</param>
    /// <returns>Returns a bool indicating if the handle is valid.</returns>
    internal static bool EnsureValidHandleRegex(string handle)
    {
        if (!Regex.IsMatch(handle, "^([a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\\.)+[a-zA-Z]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?$"))
        {
            return false;
        }

        if (handle.Length > 253)
        {
            return false;
        }

        return true;
    }
}
