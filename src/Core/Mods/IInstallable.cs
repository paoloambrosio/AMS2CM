﻿namespace Core.Mods;

public interface IInstallable
{
    string PackageName { get; }
    InstalledState Installed { get; }
    IReadOnlyCollection<string> InstalledFiles { get; }
    int? PackageFsHash { get; }

    ConfigEntries Install(string dstPath, Predicate<string> beforeFileCallback);

    public enum InstalledState
    {
        NotInstalled = 0,
        PartiallyInstalled = 1,
        Installed = 2
    }
}