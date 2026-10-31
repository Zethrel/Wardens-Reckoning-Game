// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
namespace WardensReckoning.World;

/// <summary>The folder or file is not an exported world this game can read.</summary>
public sealed class WorldFileException : Exception
{
    public WorldFileException(string message) : base(message) { }
}
